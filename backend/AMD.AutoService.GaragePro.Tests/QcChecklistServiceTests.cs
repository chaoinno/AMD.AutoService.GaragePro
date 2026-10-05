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
    public async Task GetOrCreateAsync_adds_lines_approved_on_a_later_quotation_without_touching_existing_results()
    {
        static Quotation WithApprovedLine(string code, string name)
        {
            var q = new Quotation { JobId = TestJobId, Code = code };
            q.Lines.Add(new QuotationLine
            {
                QuotationId = q.Id, CatalogCode = name == "ผ้าเบรกหน้า" ? "PRT-001" : "PRT-002", Name = name,
                Type = LineType.Part, Quantity = 1, ApprovalStatus = LineApprovalStatus.Approved
            });
            return q;
        }

        var first = WithApprovedLine("QT-1", "ผ้าเบรกหน้า");
        var repo = new FakeQcChecklistRepository();
        var created = await CreateService(repo, first).GetOrCreateAsync(TestJobId);
        var firstItemId = created.Data!.Items.Single().Id;
        await CreateService(repo, first).SaveItemAsync(TestJobId, firstItemId, new SaveQcChecklistItemRequest("pass", null));

        var second = WithApprovedLine("QT-2", "ใบปัดน้ำฝน");
        var result = await CreateService(repo, first, second).GetOrCreateAsync(TestJobId);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().HaveCount(2);
        result.Data.Items.Single(i => i.Id == firstItemId).Result.Should().Be("pass");
        result.Data.Items.Should().ContainSingle(i => i.Name == "ใบปัดน้ำฝน" && i.Result == "pending");
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
        FakeQcChecklistRepository? repo = null, Quotation? quotation = null, params Quotation[] others)
    {
        var quotations = new FakeQuotationRepository(quotation);
        quotations.Others.AddRange(others);
        return new(repo ?? new FakeQcChecklistRepository(), new FakeJobRepository(),
            quotations, new StubCurrentUser(), TimeProvider.System);
    }

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
        public Task<IReadOnlyList<Job>> GetAppointmentsAsync(JobAppointmentQuery query, CancellationToken ct = default) =>
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
        public Task<IReadOnlyList<Quotation>> GetActiveForJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>(
                new[] { quotation }.Concat(Others).Where(q => q is not null && q.Status != QuotationStatus.Superseded)
                    .Select(q => q!).ToList());
        /// <summary>ใบเสนอราคาอื่นของจ๊อบเดียวกัน — จำลองจ๊อบที่มีหลายใบ (บิลแยก)</summary>
        public List<Quotation> Others { get; } = [];
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
