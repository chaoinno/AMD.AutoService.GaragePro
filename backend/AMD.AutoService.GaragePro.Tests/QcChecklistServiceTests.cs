using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Qc;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class QcChecklistServiceTests
{
    private static readonly Guid TestJobId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task GetOrCreateAsync_fails_when_quotation_has_no_approved_lines()
    {
        var quotation = new Quotation { JobId = TestJobId, Code = "QT-1" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, ApprovalStatus = LineApprovalStatus.Rejected
        });

        var service = CreateService(quotation: quotation);

        var result = await service.GetOrCreateAsync(TestJobId);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("QC_NO_APPROVED_LINES");
    }

    [Fact]
    public async Task GetOrCreateAsync_creates_one_item_per_approved_line_only()
    {
        var quotation = new Quotation { JobId = TestJobId, Code = "QT-1" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, ApprovalStatus = LineApprovalStatus.Approved
        });
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "LBR-001", Name = "ค่าแรงเปลี่ยนผ้าเบรก",
            Type = LineType.Labor, Quantity = 1, ApprovalStatus = LineApprovalStatus.Rejected
        });

        var service = CreateService(quotation: quotation);

        var result = await service.GetOrCreateAsync(TestJobId);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().ContainSingle(i => i.CatalogCode == "PRT-001" && i.Result == "pending");
        result.Data.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task SaveItemAsync_rejects_unknown_item_and_invalid_result_token()
    {
        var repo = new FakeQcChecklistRepository();
        var checklist = SeedChecklist(repo);
        var service = CreateService(repo: repo);

        var badToken = await service.SaveItemAsync(TestJobId, checklist.Items[0].Id, new("fail", null));
        badToken.Success.Should().BeFalse();
        badToken.Error!.Code.Should().Be("QC_RESULT_INVALID");

        var unknownItem = await service.SaveItemAsync(TestJobId, Guid.NewGuid(), new("pass", null));
        unknownItem.Success.Should().BeFalse();
        unknownItem.Error!.Code.Should().Be("QC_ITEM_UNKNOWN");
    }

    [Fact]
    public async Task SaveItemAsync_marks_pass_without_requiring_a_note()
    {
        var repo = new FakeQcChecklistRepository();
        var checklist = SeedChecklist(repo);
        var service = CreateService(repo: repo);

        var result = await service.SaveItemAsync(TestJobId, checklist.Items[0].Id, new("pass", null));

        result.Success.Should().BeTrue();
        result.Data!.Result.Should().Be("pass");
        checklist.Items[0].Result.Should().Be(QcItemResult.Pass);
    }

    [Fact]
    public async Task SaveItemAsync_and_SaveTestDriveAsync_reject_edits_once_locked()
    {
        var repo = new FakeQcChecklistRepository();
        var checklist = SeedChecklist(repo);
        checklist.SubmittedAt = DateTime.UtcNow;
        var service = CreateService(repo: repo);

        var itemResult = await service.SaveItemAsync(TestJobId, checklist.Items[0].Id, new("pass", null));
        itemResult.Error!.Code.Should().Be("QC_LOCKED");

        var testDriveResult = await service.SaveTestDriveAsync(TestJobId, new(5, "ปกติ"));
        testDriveResult.Error!.Code.Should().Be("QC_LOCKED");
    }

    [Fact]
    public async Task SaveTestDriveAsync_requires_a_note()
    {
        var repo = new FakeQcChecklistRepository();
        SeedChecklist(repo);
        var service = CreateService(repo: repo);

        var result = await service.SaveTestDriveAsync(TestJobId, new(5, ""));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("QC_VALIDATION");
    }

    // ---------- helpers ----------

    private static QcChecklist SeedChecklist(FakeQcChecklistRepository repo)
    {
        var checklist = new QcChecklist { JobId = TestJobId };
        checklist.Items.Add(new QcChecklistItem
        {
            QcChecklistId = checklist.Id, QuotationLineId = Guid.NewGuid(),
            CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า", Type = LineType.Part
        });
        repo.Seed(checklist);
        return checklist;
    }

    private static QcChecklistService CreateService(
        FakeQcChecklistRepository? repo = null, Quotation? quotation = null) =>
        new(repo ?? new FakeQcChecklistRepository(), new FakeJobRepository(),
            new FakeQuotationRepository(quotation), new StubCurrentUser(), TimeProvider.System);

    private sealed class StubCurrentUser : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "พนักงาน ทดสอบ";
        public UserRole Role => UserRole.Manager;
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
        public Task<int> CountOpenAsync(string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<JobStatusTally>> CountOpenByStatusAsync(
            string shardKey, int branchId, int? jobTypeId, DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddAsync(Job job, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeQuotationRepository(Quotation? quotation) : IQuotationRepository
    {
        public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(quotation);
        public Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default) => Task.FromResult(quotation);
        public Task<Quotation?> GetLatestForJobAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(quotation);
        public Task<IReadOnlyList<Quotation>> GetQueueAsync(
            string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>(quotation is null ? [] : [quotation]);
        public Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(1);
        public Task AddAsync(Quotation q, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeQcChecklistRepository : IQcChecklistRepository
    {
        private QcChecklist? _checklist;

        public void Seed(QcChecklist checklist) => _checklist = checklist;

        public Task<QcChecklist?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(_checklist);

        public Task AddAsync(QcChecklist checklist, CancellationToken ct = default)
        {
            _checklist = checklist;
            return Task.CompletedTask;
        }

        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
