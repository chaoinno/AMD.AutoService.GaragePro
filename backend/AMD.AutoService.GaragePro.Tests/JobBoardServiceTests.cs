using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Jobs;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class JobBoardServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Returns_every_open_status_column_in_lifecycle_order_even_when_empty()
    {
        var result = await CreateService(new FakeBoardRepository()).GetAsync(null);

        result.Success.Should().BeTrue();
        result.Data!.Columns.Select(c => c.Status).Should().Equal(
            "waitinspect", "waitquote", "waitapprove", "approved", "inprogress", "waitparts", "qc", "ready");
        result.Data.Columns.Should().OnlyContain(c => c.Cards.Count == 0);
        result.Data.Total.Should().Be(0);
        result.Data.Truncated.Should().BeFalse();
        result.Data.GeneratedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Status_since_falls_back_to_created_at_and_longest_waiting_comes_first()
    {
        var fresh = NewJob("JB-A", JobStatus.InProgress, Now.AddDays(-5));
        var stale = NewJob("JB-B", JobStatus.InProgress, Now.AddDays(-1));
        var neverChanged = NewJob("JB-C", JobStatus.WaitInspect, Now.AddHours(-3));
        var repo = new FakeBoardRepository { Jobs = [fresh, stale, neverChanged] };
        repo.LastChange[fresh.Id] = Now.AddMinutes(-30);
        repo.LastChange[stale.Id] = Now.AddHours(-20);

        var board = (await CreateService(repo).GetAsync(null)).Data!;

        var inProgress = board.Columns.Single(c => c.Status == "inprogress").Cards;
        inProgress.Select(c => c.Job.JobNo).Should().Equal("JB-B", "JB-A");
        inProgress[0].StatusSince.Should().Be(Now.AddHours(-20));
        board.Columns.Single(c => c.Status == "waitinspect").Cards.Single().StatusSince.Should().Be(Now.AddHours(-3));
        board.Total.Should().Be(3);
    }

    [Fact]
    public async Task Truncates_at_the_row_limit_and_reports_it()
    {
        var repo = new FakeBoardRepository
        {
            Jobs = Enumerable.Range(0, 301).Select(i => NewJob($"JB{i:D3}", JobStatus.Qc, Now.AddMinutes(-i))).ToList()
        };

        var board = (await CreateService(repo).GetAsync(null)).Data!;

        board.Truncated.Should().BeTrue();
        board.Limit.Should().Be(300);
        board.Total.Should().Be(300);
        repo.RequestedTake.Should().Be(301);
    }

    [Fact]
    public async Task Maps_open_work_intervals_as_working_or_paused_technicians()
    {
        var job = NewJob("JB-1", JobStatus.InProgress, Now.AddHours(-2));
        var repo = new FakeBoardRepository
        {
            Jobs = [job],
            Intervals =
            [
                new WorkInterval { JobId = job.Id, TechnicianName = "สมศักดิ์", Kind = WorkIntervalKind.Pause, StartedAt = Now.AddMinutes(-5) },
                new WorkInterval { JobId = job.Id, TechnicianName = "สมชาย", Kind = WorkIntervalKind.Work, StartedAt = Now.AddMinutes(-40) }
            ]
        };

        var card = (await CreateService(repo).GetAsync(null)).Data!.Columns.Single(c => c.Status == "inprogress").Cards.Single();

        card.ActiveWorkers.Select(w => (w.TechnicianName, w.Kind)).Should().Equal(("สมชาย", "work"), ("สมศักดิ์", "pause"));
    }

    [Fact]
    public async Task Lists_only_quotations_still_awaiting_the_customer()
    {
        var job = NewJob("JB-1", JobStatus.WaitApprove, Now.AddHours(-2));
        var waiting = SentQuotation(job.Id, "QT-02", 2, LineApprovalStatus.Pending);
        var allRejected = SentQuotation(job.Id, "QT-03", 3, LineApprovalStatus.Rejected);
        var signed = SentQuotation(job.Id, "QT-01", 1, LineApprovalStatus.Approved);
        signed.Approval = new QuotationApproval { QuotationId = signed.Id, SignedAt = Now };
        var repo = new FakeBoardRepository { Jobs = [job], SentQuotations = [allRejected, waiting, signed] };

        var card = (await CreateService(repo).GetAsync(null)).Data!.Columns.Single(c => c.Status == "waitapprove").Cards.Single();

        card.AwaitingCustomerQuotationCodes.Should().Equal("QT-02");
    }

    [Fact]
    public async Task Attaches_the_latest_chat_message_id_per_job()
    {
        var withChat = NewJob("JB-1", JobStatus.Ready, Now.AddHours(-2));
        var withoutChat = NewJob("JB-2", JobStatus.Ready, Now.AddHours(-1));
        var latest = Guid.NewGuid();
        var chat = new FakeChatRepository { Latest = [new JobChatLatest(withChat.Id, latest, Now)] };

        var cards = (await CreateService(new FakeBoardRepository { Jobs = [withChat, withoutChat] }, chat).GetAsync(null))
            .Data!.Columns.Single(c => c.Status == "ready").Cards;

        cards.Single(c => c.Job.JobNo == "JB-1").LatestChatMessageId.Should().Be(latest);
        cards.Single(c => c.Job.JobNo == "JB-2").LatestChatMessageId.Should().BeNull();
    }

    [Fact]
    public async Task Passes_the_current_branch_scope_and_keyword_to_the_repository()
    {
        var repo = new FakeBoardRepository();

        await CreateService(repo).GetAsync("กข 1234");

        repo.RequestedScope.Should().Be(("db2", 105, "กข 1234"));
    }

    private static JobBoardService CreateService(FakeBoardRepository repo, FakeChatRepository? chat = null) =>
        new(repo, chat ?? new FakeChatRepository(), new StubCurrentUser(), new FixedClock(Now));

    private static Job NewJob(string jobNo, JobStatus status, DateTime createdAt) => new()
    {
        JobNo = jobNo, Status = status, CreatedAt = createdAt, JobTypeId = 9, JobTypeName = "รถในอู่",
        BranchId = 105, LegacyShardKey = "db2", VehicleRegistration = "กข 1234", CustomerName = "ลูกค้า"
    };

    private static Quotation SentQuotation(Guid jobId, string code, int version, LineApprovalStatus lineStatus)
    {
        var quotation = new Quotation { JobId = jobId, Code = code, Version = version, Status = QuotationStatus.Sent };
        quotation.Lines.Add(new QuotationLine { QuotationId = quotation.Id, Name = "งาน", ApprovalStatus = lineStatus });
        return quotation;
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class StubCurrentUser : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "ช่าง ทดสอบ";
        public UserRole Role => UserRole.Technician;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public Guid? SessionId => null;
        public bool IsAdministrator => false;
    }

    private sealed class FakeBoardRepository : IJobBoardRepository
    {
        public List<Job> Jobs { get; init; } = [];
        public Dictionary<Guid, DateTime> LastChange { get; } = [];
        public List<WorkInterval> Intervals { get; init; } = [];
        public List<Quotation> SentQuotations { get; init; } = [];
        public int? RequestedTake { get; private set; }
        public (string, int, string?)? RequestedScope { get; private set; }

        public Task<IReadOnlyList<Job>> GetOpenInShopJobsAsync(
            string shardKey, int branchId, string? keyword, int take, CancellationToken ct = default)
        {
            RequestedTake = take;
            RequestedScope = (shardKey, branchId, keyword);
            return Task.FromResult<IReadOnlyList<Job>>(Jobs.Take(take).ToList());
        }

        public Task<IReadOnlyDictionary<Guid, DateTime>> GetLastStatusChangeAsync(
            IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, DateTime>>(
                LastChange.Where(kv => jobIds.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value));

        public Task<IReadOnlyList<WorkInterval>> GetOpenWorkIntervalsAsync(
            IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkInterval>>(Intervals.Where(w => jobIds.Contains(w.JobId)).ToList());

        public Task<IReadOnlyList<Quotation>> GetSentQuotationsAsync(
            IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>(SentQuotations.Where(q => jobIds.Contains(q.JobId)).ToList());
    }

    private sealed class FakeChatRepository : IJobChatRepository
    {
        public List<JobChatLatest> Latest { get; init; } = [];

        public Task<IReadOnlyList<JobChatLatest>> GetLatestPerJobAsync(
            string shardKey, int branchId, IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<JobChatLatest>>(Latest.Where(m => jobIds.Contains(m.JobId)).ToList());

        public Task<IReadOnlyList<JobChatMessage>> GetPageAsync(
            Guid jobId, DateTime? beforeAt, Guid? beforeId, DateTime? afterAt, Guid? afterId, int take,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<JobChatMessage?> GetByIdAsync(Guid messageId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task AddAsync(JobChatMessage message, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
