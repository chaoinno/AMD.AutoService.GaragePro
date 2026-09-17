using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Handover;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class HandoverServiceTests
{
    private static readonly Guid TestJobId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task GetOrCreateAsync_creates_a_fixed_checklist_on_first_open()
    {
        var repo = new FakeHandoverRepository();
        var service = CreateService(repo);

        var result = await service.GetOrCreateAsync(TestJobId);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
        result.Data.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task SaveItemAsync_requires_a_note_when_item_is_not_returned()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        var service = CreateService(repo);

        var result = await service.SaveItemAsync(TestJobId, record.Items[0].Id, new SaveHandoverItemRequest(false, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("HANDOVER_NOTE_REQUIRED");
    }

    [Fact]
    public async Task SaveItemAsync_accepts_returned_items_without_a_note()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        var service = CreateService(repo);

        var result = await service.SaveItemAsync(TestJobId, record.Items[0].Id, new SaveHandoverItemRequest(true, null));

        result.Success.Should().BeTrue();
        record.Items[0].IsReturned.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitAsync_rejects_when_items_are_undecided_or_signature_missing()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);
        var service = CreateService(repo);

        var noSignature = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest(""));
        noSignature.Success.Should().BeFalse();
        noSignature.Error!.Code.Should().Be("HANDOVER_SIGNATURE_REQUIRED");

        var incomplete = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));
        incomplete.Success.Should().BeFalse();
        incomplete.Error!.Code.Should().Be("HANDOVER_INCOMPLETE");
    }

    [Fact]
    public async Task SubmitAsync_locks_the_record_once_every_item_is_decided_and_signed()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        foreach (var item in record.Items)
        {
            item.IsReturned = true;
            item.UpdatedAt = DateTime.UtcNow;
        }
        var service = CreateService(repo);

        var result = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));

        result.Success.Should().BeTrue();
        result.Data!.IsLocked.Should().BeTrue();

        var afterLock = await service.SaveItemAsync(TestJobId, record.Items[0].Id, new SaveHandoverItemRequest(false, "เหตุผล"));
        afterLock.Success.Should().BeFalse();
        afterLock.Error!.Code.Should().Be("HANDOVER_LOCKED");
    }

    // ---------- helpers ----------

    private static HandoverRecord SeedRecord(FakeHandoverRepository repo)
    {
        var record = new HandoverRecord { JobId = TestJobId };
        record.Items.Add(new HandoverChecklistItem
        {
            HandoverRecordId = record.Id, ItemCode = "key", Name = "กุญแจรถ"
        });
        record.Items.Add(new HandoverChecklistItem
        {
            HandoverRecordId = record.Id, ItemCode = "manual", Name = "คู่มือรถ"
        });
        repo.Seed(record);
        return record;
    }

    private static HandoverService CreateService(FakeHandoverRepository repo) =>
        new(repo, new FakeJobRepository(), new StubCurrentUser(), TimeProvider.System);

    private sealed class StubCurrentUser : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "แคชเชียร์ ทดสอบ";
        public UserRole Role => UserRole.Cashier;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public Guid? SessionId => null;
        public bool IsAdministrator => false;
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<Job?>(new Job
            {
                Id = jobId, LegacyShardKey = "db2", BranchId = 105, JobNo = "JB2608310105001",
                CustomerName = "ลูกค้าทดสอบ", VehicleRegistration = "1กก-1234", JobTypeId = 9,
            });
        public Task<Job?> GetOpenByVehicleAsync(string shardKey, int branchId, long vehicleId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Job>> GetAppointmentsAsync(JobAppointmentQuery query, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<int> CountOpenAsync(string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddAsync(Job job, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeHandoverRepository : IHandoverRepository
    {
        private HandoverRecord? _record;

        public void Seed(HandoverRecord record) => _record = record;

        public Task<HandoverRecord?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(_record);

        public Task AddAsync(HandoverRecord record, CancellationToken ct = default)
        {
            _record = record;
            return Task.CompletedTask;
        }

        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
