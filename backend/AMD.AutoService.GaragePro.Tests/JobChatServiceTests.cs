using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.JobChat;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class JobChatServiceTests
{
    private static readonly Guid TestJobId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task SendAsync_rejects_when_job_is_in_another_branch()
    {
        var service = CreateService(out _, out _, jobBranchId: 999);

        var result = await service.SendAsync(TestJobId, new SendJobChatMessageRequest("สวัสดี", null, null, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_OTHER_BRANCH");
    }

    [Fact]
    public async Task SendAsync_rejects_empty_message_with_no_attachments()
    {
        var service = CreateService(out _, out _);

        var result = await service.SendAsync(TestJobId, new SendJobChatMessageRequest(null, null, null, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("CHAT_MESSAGE_EMPTY");
    }

    [Fact]
    public async Task SendAsync_accepts_attachment_only_message_with_no_body()
    {
        var service = CreateService(out _, out var attachments);
        attachments.Seed(new Attachment
        {
            Id = Guid.NewGuid(), JobId = TestJobId, Kind = "chat", EntityId = null, FileName = "a.png"
        });
        var attachmentId = attachments.All.Single().Id;

        var result = await service.SendAsync(
            TestJobId, new SendJobChatMessageRequest(null, null, null, [attachmentId]));

        result.Success.Should().BeTrue();
        result.Data!.Attachments.Should().ContainSingle(a => a.Id == attachmentId);
        attachments.All.Single().EntityId.Should().Be(result.Data.Id);
    }

    [Fact]
    public async Task SendAsync_rejects_an_attachment_already_linked_to_another_message()
    {
        var service = CreateService(out _, out var attachments);
        attachments.Seed(new Attachment
        {
            Id = Guid.NewGuid(), JobId = TestJobId, Kind = "chat", EntityId = Guid.NewGuid(), FileName = "a.png"
        });
        var attachmentId = attachments.All.Single().Id;

        var result = await service.SendAsync(
            TestJobId, new SendJobChatMessageRequest("รูปนี้", null, null, [attachmentId]));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("CHAT_ATTACHMENT_INVALID");
    }

    [Fact]
    public async Task SendAsync_rejects_an_attachment_belonging_to_another_job()
    {
        var service = CreateService(out _, out var attachments);
        attachments.Seed(new Attachment
        {
            Id = Guid.NewGuid(), JobId = Guid.NewGuid(), Kind = "chat", EntityId = null, FileName = "a.png"
        });
        var attachmentId = attachments.All.Single().Id;

        var result = await service.SendAsync(
            TestJobId, new SendJobChatMessageRequest("รูปนี้", null, null, [attachmentId]));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("CHAT_ATTACHMENT_INVALID");
    }

    [Fact]
    public async Task SendAsync_rejects_mentioning_an_inactive_staff()
    {
        var service = CreateService(out _, out _, staff: [new(1, "สมชาย", "ใจดี", 105, false)]);

        var result = await service.SendAsync(TestJobId, new SendJobChatMessageRequest("@สมชาย ช่วยดูที", null, [1], null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("CHAT_MENTION_STAFF_INVALID");
    }

    [Fact]
    public async Task SendAsync_rejects_mentioning_a_staff_from_another_branch()
    {
        var service = CreateService(out _, out _, staff: [new(1, "สมชาย", "ใจดี", 999, true)]);

        var result = await service.SendAsync(TestJobId, new SendJobChatMessageRequest("@สมชาย ช่วยดูที", null, [1], null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("CHAT_MENTION_STAFF_INVALID");
    }

    [Fact]
    public async Task SendAsync_creates_message_with_mentions_and_writes_activity_event()
    {
        var service = CreateService(out var chats, out _, staff: [new(1, "สมชาย", "ใจดี", 105, true)]);

        var result = await service.SendAsync(
            TestJobId, new SendJobChatMessageRequest("@[1:สมชาย ใจดี] ช่วยดูที", null, [1], null));

        result.Success.Should().BeTrue();
        result.Data!.Mentions.Should().ContainSingle(m => m.StaffId == 1 && m.StaffName == "สมชาย ใจดี");
        chats.Events.Should().ContainSingle(e => e.EventType == "jobchat.message_sent");
    }

    [Fact]
    public async Task SendAsync_rejects_reply_to_a_message_from_another_job()
    {
        var service = CreateService(out var chats, out _);
        var otherJobMessage = new JobChatMessage { JobId = Guid.NewGuid(), Body = "ข้อความจากงานอื่น" };
        chats.Seed(otherJobMessage);

        var result = await service.SendAsync(
            TestJobId, new SendJobChatMessageRequest("ตอบกลับ", otherJobMessage.Id, null, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("CHAT_REPLY_NOT_FOUND");
    }

    [Fact]
    public async Task SendAsync_attaches_reply_preview_including_deleted_flag()
    {
        var service = CreateService(out var chats, out _);
        var original = new JobChatMessage { JobId = TestJobId, Body = "ข้อความเดิม", IsDeleted = true };
        chats.Seed(original);

        var result = await service.SendAsync(
            TestJobId, new SendJobChatMessageRequest("ตอบกลับ", original.Id, null, null));

        result.Success.Should().BeTrue();
        result.Data!.ReplyTo.Should().NotBeNull();
        result.Data.ReplyTo!.IsDeleted.Should().BeTrue();
        result.Data.ReplyTo.Body.Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_only_allows_the_author_to_soft_delete_their_own_message()
    {
        var service = CreateService(out var chats, out _);
        var message = new JobChatMessage { JobId = TestJobId, Body = "ลบทิ้ง", CreatedByUserId = 999 };
        chats.Seed(message);

        var forbidden = await service.DeleteAsync(TestJobId, message.Id);
        forbidden.Success.Should().BeFalse();
        forbidden.Error!.Code.Should().Be("CHAT_FORBIDDEN");

        message.CreatedByUserId = 7; // matches StubCurrentUser.UserId below
        var ok = await service.DeleteAsync(TestJobId, message.Id);
        ok.Success.Should().BeTrue();
        message.IsDeleted.Should().BeTrue();
    }

    // ---------- helpers ----------

    private static JobChatService CreateService(
        out FakeJobChatRepository chats, out FakeAttachmentRepository attachments,
        int jobBranchId = 105, IReadOnlyList<Staff>? staff = null)
    {
        chats = new FakeJobChatRepository();
        attachments = new FakeAttachmentRepository();
        return new JobChatService(
            chats, new FakeJobRepository(jobBranchId), attachments,
            new FakeStaffRepository(staff ?? []), new StubCurrentUser(), TimeProvider.System);
    }

    private sealed record Staff(long Id, string FirstName, string LastName, int BranchId, bool IsActive);

    private sealed class StubCurrentUser : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "ธุรการ ทดสอบ";
        public UserRole Role => UserRole.Office;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public Guid? SessionId => null;
        public bool IsAdministrator => false;
    }

    private sealed class FakeJobRepository(int branchId) : IJobRepository
    {
        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<Job?>(new Job { Id = jobId, LegacyShardKey = "db2", BranchId = branchId, JobNo = "JB0000" });
        public Task<Job?> GetOpenByVehicleAsync(string shardKey, int branchId2, long vehicleId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<int> CountOpenAsync(string shardKey, int branchId2, int? jobTypeId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<JobStatusTally>> CountOpenByStatusAsync(
            string shardKey, int branchId, int? jobTypeId, DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddAsync(Job job, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeStaffRepository(IReadOnlyList<Staff> staff) : IStaffRepository
    {
        public Task<PagedResult<StaffSummaryDto>> SearchAsync(LegacyRequestScope scope, bool isAdministrator, StaffSearchQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffDetailDto?> GetAsync(LegacyRequestScope scope, bool isAdministrator, long id, CancellationToken ct = default)
        {
            var s = staff.SingleOrDefault(x => x.Id == id);
            return Task.FromResult(s is null ? null : new StaffDetailDto(s.Id, s.BranchId, "สาขาทดสอบ", "S001", s.FirstName, s.LastName,
                null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null,
                s.IsActive, new StaffAccountDto(s.Id, "user", false, s.IsActive), [], null, null));
        }
        public Task<StaffDetailDto?> CreateAsync(LegacyRequestScope scope, bool isAdministrator, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffDetailDto?> UpdateAsync(LegacyRequestScope scope, bool isAdministrator, long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> SetStatusAsync(LegacyRequestScope scope, bool isAdministrator, long id, StaffStatusRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffCodePreviewDto> PreviewCodeAsync(LegacyRequestScope scope, int branchId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string?> GetImagePathAsync(LegacyRequestScope scope, bool isAdministrator, long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<StaffReferenceDataDto> GetReferenceDataAsync(LegacyRequestScope scope, bool isAdministrator, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<LookupItemDto>> GetSectorsAsync(LegacyRequestScope scope, int? departmentId, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeAttachmentRepository : IAttachmentRepository
    {
        private readonly List<Attachment> _items = [];
        public IReadOnlyList<Attachment> All => _items;

        public void Seed(Attachment attachment) => _items.Add(attachment);

        public Task AddAsync(Attachment attachment, CancellationToken ct = default)
        {
            _items.Add(attachment);
            return Task.CompletedTask;
        }
        public Task<Attachment?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_items.SingleOrDefault(a => a.Id == id));
        public Task<Attachment?> GetByPathAsync(string relativePath, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Attachment>> GetForJobAsync(Guid jobId, string? kind, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Attachment>>(
                _items.Where(a => a.JobId == jobId && (kind == null || a.Kind == kind)).ToList());
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeJobChatRepository : IJobChatRepository
    {
        private readonly List<JobChatMessage> _items = [];
        public List<ActivityEvent> Events { get; } = [];

        public void Seed(JobChatMessage message) => _items.Add(message);

        public Task<IReadOnlyList<JobChatMessage>> GetPageAsync(
            Guid jobId, DateTime? beforeAt, Guid? beforeId, DateTime? afterAt, Guid? afterId, int take,
            CancellationToken ct = default)
        {
            var q = _items.Where(m => m.JobId == jobId).AsEnumerable();

            if (beforeAt is { } bAt && beforeId is { } bId)
                q = q.Where(m => m.CreatedAt < bAt || (m.CreatedAt == bAt && m.Id.CompareTo(bId) < 0));

            if (afterAt is { } aAt && afterId is { } aId)
            {
                q = q.Where(m => m.CreatedAt > aAt || (m.CreatedAt == aAt && m.Id.CompareTo(aId) > 0));
                return Task.FromResult<IReadOnlyList<JobChatMessage>>(
                    q.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id).Take(take).ToList());
            }

            return Task.FromResult<IReadOnlyList<JobChatMessage>>(
                q.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id).Take(take).ToList());
        }

        public Task<JobChatMessage?> GetByIdAsync(Guid messageId, CancellationToken ct = default) =>
            Task.FromResult(_items.SingleOrDefault(m => m.Id == messageId));

        public Task AddAsync(JobChatMessage message, CancellationToken ct = default)
        {
            _items.Add(message);
            return Task.CompletedTask;
        }

        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
