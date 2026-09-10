using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Reports;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class ReportsServiceTests
{
    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.FrontDesk)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Lead)]
    public async Task GetDashboardAsync_rejects_roles_outside_manager_or_office(UserRole role)
    {
        var service = CreateService(new FakeReportsRepository(), role);

        var result = await service.GetDashboardAsync();

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("REPORTS_FORBIDDEN");
    }

    [Theory]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Office)]
    public async Task GetDashboardAsync_allows_manager_and_office(UserRole role)
    {
        var service = CreateService(new FakeReportsRepository(), role);

        var result = await service.GetDashboardAsync();

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task GetSalesMarginAsync_strips_cost_and_margin_for_non_manager_roles_but_keeps_revenue()
    {
        var quotation = new Quotation { JobId = Guid.NewGuid(), Code = "QT-1", CreatedAt = DateTime.UtcNow };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, Type = LineType.Part, ApprovalStatus = LineApprovalStatus.Approved,
            NetAmount = 1000m, CostAmount = 600m, MarginAmount = 400m
        });
        // บรรทัดที่ยังไม่อนุมัติต้องไม่ถูกนับ (invariant #3: เฉพาะบรรทัดที่ approved เท่านั้นเข้าสู่การเรียกเก็บเงิน/รายงาน)
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, Type = LineType.Part, ApprovalStatus = LineApprovalStatus.Pending,
            NetAmount = 9999m, CostAmount = 1m, MarginAmount = 9998m
        });
        var repo = new FakeReportsRepository { Quotations = [quotation] };

        var officeResult = await CreateService(repo, UserRole.Office).GetSalesMarginAsync(null, null);
        officeResult.Success.Should().BeTrue();
        officeResult.Data!.NetAmount.Should().Be(1000m);
        officeResult.Data.CostAmount.Should().BeNull();
        officeResult.Data.MarginAmount.Should().BeNull();
        officeResult.Data.MarginPercent.Should().BeNull();

        var managerResult = await CreateService(repo, UserRole.Manager).GetSalesMarginAsync(null, null);
        managerResult.Data!.NetAmount.Should().Be(1000m);
        managerResult.Data.CostAmount.Should().Be(600m);
        managerResult.Data.MarginAmount.Should().Be(400m);
        managerResult.Data.MarginPercent.Should().Be(40m);
    }

    [Fact]
    public async Task GetSalesMarginAsync_attributes_labor_revenue_to_the_assigned_technician_only()
    {
        var quotation = new Quotation { JobId = Guid.NewGuid(), Code = "QT-2", CreatedAt = DateTime.UtcNow };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, Type = LineType.Labor, ApprovalStatus = LineApprovalStatus.Approved,
            NetAmount = 500m, CostAmount = 0m, MarginAmount = 500m, AssignedTechnicianName = "ช่างเอ"
        });
        quotation.Lines.Add(new QuotationLine
        {
            // อะไหล่ไม่มีเจ้าของงาน — ต้องไม่ถูกนับในรายงานต่อช่าง
            QuotationId = quotation.Id, Type = LineType.Part, ApprovalStatus = LineApprovalStatus.Approved,
            NetAmount = 300m, CostAmount = 200m, MarginAmount = 100m
        });
        var repo = new FakeReportsRepository { Quotations = [quotation] };

        var result = await CreateService(repo, UserRole.Manager).GetSalesMarginAsync(null, null);

        result.Data!.ByTechnician.Should().ContainSingle();
        result.Data.ByTechnician.Single().TechnicianName.Should().Be("ช่างเอ");
        result.Data.ByTechnician.Single().NetAmount.Should().Be(500m);
    }

    [Fact]
    public async Task GetCycleTimeAsync_averages_status_durations_within_range_and_ranks_stuck_jobs_outside_it()
    {
        var now = DateTime.UtcNow;

        // Job A: เปิดในช่วงที่กรอง — ใช้คำนวณค่าเฉลี่ยเวลาต่อสถานะและรอบเวลารวม (ปิดงานแล้ว)
        var jobA = new Job
        {
            LegacyShardKey = "db2", BranchId = 105, JobNo = "JB-A", CustomerName = "ลูกค้าเอ",
            Status = JobStatus.Completed, CreatedAt = now.AddDays(-2)
        };
        // Job B/C: เปิดนอกช่วงที่กรอง — ไม่นับในค่าเฉลี่ย แต่ยังต้องโผล่ใน "จ๊อบที่ค้างนานที่สุด" เพราะรายการนี้ดูสถานะปัจจุบันจริง ไม่กรองตามช่วงวันที่
        var jobB = new Job
        {
            LegacyShardKey = "db2", BranchId = 105, JobNo = "JB-B", CustomerName = "ลูกค้าบี",
            Status = JobStatus.WaitQuote, CreatedAt = jobA.CreatedAt.AddDays(1)
        };
        var jobC = new Job
        {
            LegacyShardKey = "db2", BranchId = 105, JobNo = "JB-C", CustomerName = "ลูกค้าซี",
            Status = JobStatus.WaitApprove, CreatedAt = jobA.CreatedAt.AddDays(-1)
        };

        var events = new List<ActivityEvent>
        {
            new() { JobId = jobA.Id, EventType = "job.opened", OccurredAt = jobA.CreatedAt },
            new() { JobId = jobA.Id, EventType = "job.status.changed", OccurredAt = jobA.CreatedAt.AddHours(10),
                    PayloadJson = """{"from":"WaitInspect","to":"WaitQuote"}""" },
            new() { JobId = jobA.Id, EventType = "job.status.changed", OccurredAt = jobA.CreatedAt.AddHours(34),
                    PayloadJson = """{"from":"WaitQuote","to":"Completed"}""" },

            new() { JobId = jobB.Id, EventType = "job.opened", OccurredAt = jobB.CreatedAt },
            new() { JobId = jobB.Id, EventType = "job.status.changed", OccurredAt = now.AddHours(-12),
                    PayloadJson = """{"from":"WaitInspect","to":"WaitQuote"}""" },

            new() { JobId = jobC.Id, EventType = "job.opened", OccurredAt = jobC.CreatedAt },
            new() { JobId = jobC.Id, EventType = "job.status.changed", OccurredAt = now.AddHours(-20),
                    PayloadJson = """{"from":"WaitInspect","to":"WaitApprove"}""" },
        };

        var repo = new FakeReportsRepository { Jobs = [jobA, jobB, jobC], Events = events };
        var service = CreateService(repo, UserRole.Manager);

        // ช่วงแคบพอที่จะจับเฉพาะ Job A (Job B อยู่ +1 วัน, Job C อยู่ -1 วัน จาก Job A)
        var result = await service.GetCycleTimeAsync(jobA.CreatedAt.AddHours(-1), jobA.CreatedAt.AddHours(1));

        result.Success.Should().BeTrue();
        result.Data!.CompletedJobCount.Should().Be(1);
        result.Data.AverageTotalHours.Should().Be(34);
        result.Data.P90TotalHours.Should().Be(34);
        result.Data.AverageDurationByStatus.Should().ContainSingle(d => d.Status == "waitinspect" && d.AverageHours == 10);
        result.Data.AverageDurationByStatus.Should().ContainSingle(d => d.Status == "waitquote" && d.AverageHours == 24);

        // จ๊อบที่ค้างนานกว่า (Job C ~20 ชม.) ต้องขึ้นก่อน Job B (~12 ชม.) แม้ Job C จะเปิดนอกช่วงวันที่กรองก็ตาม
        result.Data.TopStuckJobs.Select(j => j.JobNo).Should().ContainInOrder("JB-C", "JB-B");
        result.Data.TopStuckJobs.Should().NotContain(j => j.JobNo == "JB-A");
    }

    [Fact]
    public async Task GetStockAsync_buckets_lots_by_age_and_flags_damaged_value()
    {
        var now = DateTime.UtcNow;
        var catalogId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var repo = new FakeReportsRepository
        {
            StockLots =
            [
                new StockLot { CatalogItemId = catalogId, WarehouseId = warehouseId, RemainingQuantity = 10, UnitCost = 100m, ReceivedAt = now.AddDays(-10) },
                new StockLot { CatalogItemId = catalogId, WarehouseId = warehouseId, RemainingQuantity = 5, UnitCost = 100m, ReceivedAt = now.AddDays(-95) }
            ],
            CatalogItems = [new CatalogItem { Id = catalogId, Code = "P-1", Name = "อะไหล่ทดสอบ", Damaged = 2, Cost = 50m }],
            Warehouses = [new Warehouse { Id = warehouseId, Name = "คลังหลัก" }]
        };

        var result = await CreateService(repo, UserRole.Manager).GetStockAsync();

        result.Success.Should().BeTrue();
        result.Data!.TotalValuation.Should().Be(1500m);
        result.Data.DamagedValuation.Should().Be(100m);
        result.Data.AgingBuckets.Single(b => b.MinDays == 0).Value.Should().Be(1000m);
        result.Data.AgingBuckets.Single(b => b.MaxDays == null).Value.Should().Be(500m);
        result.Data.OldestLots.First().AgeDays.Should().BeGreaterThanOrEqualTo(95);
        result.Data.OldestLots.First().CatalogCode.Should().Be("P-1");
    }

    private static ReportsService CreateService(IReportsRepository repo, UserRole role) =>
        new(repo, new StubCurrentUser(role), TimeProvider.System);

    private sealed class StubCurrentUser(UserRole role) : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "ผู้จัดการ ทดสอบ";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public Guid? SessionId => null;
        public bool IsAdministrator => false;
    }

    private sealed class FakeReportsRepository : IReportsRepository
    {
        public List<Job> Jobs { get; set; } = [];
        public List<ActivityEvent> Events { get; set; } = [];
        public List<Quotation> Quotations { get; set; } = [];
        public List<StockLot> StockLots { get; set; } = [];
        public List<CatalogItem> CatalogItems { get; set; } = [];
        public List<Warehouse> Warehouses { get; set; } = [];
        public decimal CollectedAmount { get; set; }
        public int ReceiptsIssuedCount { get; set; }

        public Task<IReadOnlyList<Job>> GetJobsAsync(string shardKey, int branchId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Job>>(Jobs);

        public Task<decimal> GetCollectedAmountAsync(string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
            Task.FromResult(CollectedAmount);

        public Task<int> GetReceiptsIssuedCountAsync(string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
            Task.FromResult(ReceiptsIssuedCount);

        public Task<IReadOnlyList<ActivityEvent>> GetJobLifecycleEventsAsync(string shardKey, int branchId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<ActivityEvent>>(Events);

        public Task<IReadOnlyList<Quotation>> GetQuotationsCreatedInRangeAsync(string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Quotation>>(Quotations);

        public Task<IReadOnlyList<StockLot>> GetStockLotsWithRemainingAsync(string shardKey, int branchId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<StockLot>>(StockLots);

        public Task<IReadOnlyList<CatalogItem>> GetCatalogItemsAsync(string shardKey, int branchId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CatalogItem>>(CatalogItems);

        public Task<IReadOnlyList<Warehouse>> GetWarehousesAsync(string shardKey, int branchId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Warehouse>>(Warehouses);
    }
}
