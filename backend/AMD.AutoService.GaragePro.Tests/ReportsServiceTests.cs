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

    // ---------- ขายหน้าร้าน ----------

    private static readonly DateOnly Day1 = new(2026, 9, 1);

    /// <summary>เวลาไทย (UTC+7) → UTC ที่เก็บในฐานข้อมูล</summary>
    private static DateTime ThaiTime(int day, int hour) =>
        DateTime.SpecifyKind(new DateTime(2026, 9, day, hour, 0, 0).AddHours(-7), DateTimeKind.Utc);

    private static Sale RetailSale(int day, int hour, decimal total, decimal cost, SaleStatus status = SaleStatus.Completed,
        string seller = "แคชเชียร์ ก", params (string Code, int Qty, decimal Net, decimal Cost)[] lines)
    {
        var sale = new Sale
        {
            Status = status, CompletedAt = ThaiTime(day, hour), CompletedByName = seller, TotalAmount = total,
            NetAmount = Math.Round(total / 1.07m, 2), VatAmount = total - Math.Round(total / 1.07m, 2), CostTotal = cost,
            ReceiptNo = $"SL-26-{day:D2}{hour:D2}",
        };
        if (status == SaleStatus.Voided) { sale.VoidedAt = sale.CompletedAt!.Value.AddHours(1); sale.VoidReason = "ลูกค้าคืนสินค้า"; }
        foreach (var (code, qty, net, lineCost) in lines)
            sale.Lines.Add(new SaleLine { CatalogItemId = CatalogId(code), Code = code, Name = $"สินค้า {code}", Unit = "ชิ้น", Quantity = qty, NetAmount = net, CostAmount = lineCost });
        sale.Payments.Add(new SalePayment { Method = PaymentMethod.Cash, Amount = total });
        return sale;
    }

    private static Guid CatalogId(string code) => new(System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(code)));

    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Technician)]
    public async Task GetRetailSalesAsync_rejects_roles_outside_manager_or_office(UserRole role)
    {
        var result = await CreateService(new FakeReportsRepository(), role).GetRetailSalesAsync(Day1, Day1);

        result.Error!.Code.Should().Be("REPORTS_FORBIDDEN");
    }

    [Fact]
    public async Task GetRetailSalesAsync_excludes_voided_bills_from_totals_but_lists_them_separately()
    {
        var repo = new FakeReportsRepository
        {
            Sales =
            [
                RetailSale(1, 10, 107m, 60m, lines: ("A", 2, 100m, 60m)),
                RetailSale(1, 11, 214m, 90m, lines: ("B", 1, 200m, 90m)),
                RetailSale(2, 9, 535m, 300m, SaleStatus.Voided, lines: ("A", 5, 500m, 300m)),
            ],
        };

        var report = (await CreateService(repo, UserRole.Manager).GetRetailSalesAsync(Day1, Day1.AddDays(2))).Data!;

        report.BillCount.Should().Be(2);
        report.TotalAmount.Should().Be(321m);
        report.ItemQuantity.Should().Be(3);
        report.CostAmount.Should().Be(150m);
        report.MarginAmount.Should().Be(report.NetAmount - 150m);
        report.VoidedCount.Should().Be(1);
        report.VoidedAmount.Should().Be(535m);
        report.VoidedSales.Single().VoidReason.Should().Be("ลูกค้าคืนสินค้า");
        report.TopProducts.Select(x => x.Code).Should().Equal("B", "A");
        report.TopProducts.Single(x => x.Code == "A").Quantity.Should().Be(2, "จำนวนของบิลที่ยกเลิกไม่นับ");
    }

    [Fact]
    public async Task GetRetailSalesAsync_groups_by_thai_calendar_day_and_fills_days_without_sales()
    {
        // 01:00 เวลาไทยวันที่ 2 = 18:00 UTC วันที่ 1 — ต้องนับเป็นวันที่ 2 ตามปฏิทินไทย
        var repo = new FakeReportsRepository { Sales = [RetailSale(2, 1, 100m, 0m), RetailSale(4, 23, 50m, 0m)] };

        var report = (await CreateService(repo, UserRole.Office).GetRetailSalesAsync(Day1, Day1.AddDays(3))).Data!;

        report.Daily.Select(x => x.Date.Day).Should().Equal(1, 2, 3, 4);
        report.Daily.Select(x => x.TotalAmount).Should().Equal(0m, 100m, 0m, 50m);
    }

    [Fact]
    public async Task GetRetailSalesAsync_strips_cost_and_margin_for_office_but_keeps_revenue()
    {
        var repo = new FakeReportsRepository { Sales = [RetailSale(1, 10, 107m, 60m, lines: ("A", 1, 100m, 60m))] };

        var report = (await CreateService(repo, UserRole.Office).GetRetailSalesAsync(Day1, Day1)).Data!;

        report.TotalAmount.Should().Be(107m);
        report.CostAmount.Should().BeNull();
        report.MarginAmount.Should().BeNull();
        report.MarginPercent.Should().BeNull();
        report.TopProducts.Single().CostAmount.Should().BeNull();
        report.TopProducts.Single().MarginAmount.Should().BeNull();
    }

    [Fact]
    public async Task GetRetailSalesAsync_rejects_inverted_or_too_long_ranges()
    {
        var service = CreateService(new FakeReportsRepository(), UserRole.Manager);

        (await service.GetRetailSalesAsync(Day1.AddDays(1), Day1)).Error!.Code.Should().Be("REPORTS_VALIDATION");
        (await service.GetRetailSalesAsync(Day1, Day1.AddDays(ReportsService.RetailReportMaxDays))).Error!.Code.Should().Be("REPORTS_VALIDATION");
    }

    [Fact]
    public async Task GetDashboardAsync_reports_todays_retail_sales_without_voided_bills()
    {
        var today = DateTime.UtcNow.AddHours(7).Date;
        Sale At(decimal total, SaleStatus status) => new()
        {
            Status = status, TotalAmount = total, CompletedAt = DateTime.SpecifyKind(today.AddHours(-7).AddMinutes(1), DateTimeKind.Utc),
            Lines = [new SaleLine { Quantity = 2 }],
        };
        var repo = new FakeReportsRepository { Sales = [At(100m, SaleStatus.Completed), At(999m, SaleStatus.Voided)], RetailDraftCount = 3 };

        var retail = (await CreateService(repo, UserRole.Manager).GetDashboardAsync()).Data!.RetailToday!;

        retail.BillCount.Should().Be(1);
        retail.TotalAmount.Should().Be(100m);
        retail.ItemQuantity.Should().Be(2);
        retail.DraftCount.Should().Be(3);
    }

    // ── ประวัติรถ + รถใกล้ครบรอบบริการ (เพิ่ม 2026-10-08) ───────────────────────

    private static Job VehicleJob(long vehicleId, string jobNo, DateTime createdAt, string plate = "1กก 1234",
        string? phone = "081-234-5678", JobStatus status = JobStatus.Completed) => new()
    {
        LegacyShardKey = "db2", BranchId = 105, VehicleId = vehicleId, JobNo = jobNo, CreatedAt = createdAt,
        VehicleRegistration = plate, CustomerPhone = phone, CustomerName = "ลูกค้า ทดสอบ", Status = status
    };

    [Theory]
    [InlineData("1กก-1234")]
    [InlineData("1กก1234")]
    [InlineData("0812345678")]
    [InlineData("081 234")]
    public async Task SearchVehicleHistoryAsync_ignores_spaces_and_dashes(string term)
    {
        var repo = new FakeReportsRepository
        {
            Jobs =
            [
                VehicleJob(1, "JB1", DateTime.UtcNow.AddDays(-30)),
                VehicleJob(1, "JB2", DateTime.UtcNow.AddDays(-1)),
                VehicleJob(2, "JB3", DateTime.UtcNow, plate: "9ขข 9999", phone: "0899999999")
            ]
        };

        var result = await CreateService(repo, UserRole.Technician).SearchVehicleHistoryAsync(term);

        var match = result.Data!.Items.Should().ContainSingle().Subject;
        match.VehicleId.Should().Be(1);
        match.VisitCount.Should().Be(2);
        match.LastJobNo.Should().Be("JB2");
    }

    [Fact]
    public async Task SearchVehicleHistoryAsync_requires_at_least_three_characters()
    {
        var result = await CreateService(new FakeReportsRepository(), UserRole.Manager).SearchVehicleHistoryAsync("1-ก");

        result.Error!.Code.Should().Be("VEHICLE_HISTORY_VALIDATION");
    }

    private static FakeReportsRepository HistoryRepo()
    {
        var job = VehicleJob(1, "JB1", DateTime.UtcNow.AddDays(-10));
        job.MileageAtIntake = 40_000;
        var superseded = new Quotation { JobId = job.Id, Code = "QT-1-01", Version = 1, Status = QuotationStatus.Superseded };
        superseded.Lines.Add(new QuotationLine { Name = "ของเก่า", ApprovalStatus = LineApprovalStatus.Approved, NetAmount = 9m });
        var active = new Quotation { JobId = job.Id, Code = "QT-1-02", Version = 2, Status = QuotationStatus.Approved };
        active.Lines.Add(new QuotationLine
        {
            Sequence = 1, Name = "เปลี่ยนน้ำมันเครื่อง", Type = LineType.Labor, Quantity = 1, Unit = "งาน",
            ApprovalStatus = LineApprovalStatus.Approved, NetAmount = 500m
        });
        active.Lines.Add(new QuotationLine { Sequence = 2, Name = "ไม่อนุมัติ", ApprovalStatus = LineApprovalStatus.Rejected, NetAmount = 100m });
        return new FakeReportsRepository
        {
            Jobs = [job],
            Quotations = [superseded, active],
            Receipts = [new Receipt { JobId = job.Id, DocumentNo = "RC-26-0001", TotalAmount = 535m }],
            Handovers =
            [
                new HandoverRecord
                {
                    JobId = job.Id, SubmittedAt = DateTime.UtcNow.AddDays(-9), MileageAtHandover = 40_020,
                    NextServiceMileage = 50_000, NextServiceMonths = 6, NextServiceDueOn = new DateOnly(2027, 4, 1)
                }
            ]
        };
    }

    [Fact]
    public async Task GetVehicleHistoryAsync_lists_only_approved_lines_of_active_quotations()
    {
        var result = await CreateService(HistoryRepo(), UserRole.FrontDesk).GetVehicleHistoryAsync(1);

        var visit = result.Data!.Visits.Should().ContainSingle().Subject;
        visit.Lines.Should().ContainSingle(l => l.Name == "เปลี่ยนน้ำมันเครื่อง" && l.Amount == 500m);
        visit.ReceiptTotal.Should().Be(535m);
        visit.MileageAtIntake.Should().Be(40_000);
        visit.MileageAtHandover.Should().Be(40_020);
        result.Data.ShowAmounts.Should().BeTrue();
        result.Data.NextService!.DueOn.Should().Be(new DateOnly(2027, 4, 1));
        result.Data.NextService.Mileage.Should().Be(50_000);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Lead)]
    public async Task GetVehicleHistoryAsync_hides_money_from_workshop_roles(UserRole role)
    {
        var result = await CreateService(HistoryRepo(), role).GetVehicleHistoryAsync(1);

        result.Data!.ShowAmounts.Should().BeFalse();
        result.Data.Visits.Single().ReceiptTotal.Should().BeNull();
        result.Data.Visits.Single().Lines.Should().OnlyContain(l => l.Amount == null);
    }

    [Fact]
    public async Task GetVehicleHistoryAsync_returns_not_found_for_an_unknown_vehicle()
    {
        var result = await CreateService(HistoryRepo(), UserRole.Manager).GetVehicleHistoryAsync(99);

        result.Error!.Code.Should().Be("VEHICLE_HISTORY_NOT_FOUND");
    }

    [Fact]
    public async Task GetServiceDueAsync_keeps_only_the_latest_handover_and_drops_vehicles_that_came_back()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        var now = DateTime.UtcNow;
        // รถ 1: ส่งมอบสองครั้ง — ครั้งเก่า (ครบกำหนดในช่วง) ต้องไม่ขึ้น เหลือครั้งใหม่
        var v1Old = VehicleJob(1, "JB-1A", now.AddDays(-200));
        var v1New = VehicleJob(1, "JB-1B", now.AddDays(-100));
        // รถ 2: ส่งมอบแล้วกลับมาเปิดจ๊อบใหม่ → ไม่ต้องตาม
        var v2 = VehicleJob(2, "JB-2A", now.AddDays(-150), plate: "2ขข 2222");
        var v2Back = VehicleJob(2, "JB-2B", now.AddDays(-5), plate: "2ขข 2222", status: JobStatus.InProgress);
        // รถ 3: กลับมาแต่จ๊อบถูกยกเลิก → ยังต้องตาม
        var v3 = VehicleJob(3, "JB-3A", now.AddDays(-150), plate: "3คค 3333");
        var v3Cancelled = VehicleJob(3, "JB-3B", now.AddDays(-5), plate: "3คค 3333", status: JobStatus.Cancelled);

        HandoverRecord Handed(Job job, int daysAgo, int dueInDays) => new()
        {
            JobId = job.Id, SubmittedAt = now.AddDays(-daysAgo), NextServiceDueOn = today.AddDays(dueInDays),
            MileageAtHandover = 10_000, NextServiceMileage = 15_000
        };
        var repo = new FakeReportsRepository
        {
            Jobs = [v1Old, v1New, v2, v2Back, v3, v3Cancelled],
            Handovers = [Handed(v1Old, 199, -10), Handed(v1New, 99, 5), Handed(v2, 149, 3), Handed(v3, 149, -2)]
        };

        var result = await CreateService(repo, UserRole.Office).GetServiceDueAsync(null, null);

        result.Data!.Items.Select(i => i.LastJobNo).Should().Equal("JB-3A", "JB-1B");
        result.Data.OverdueCount.Should().Be(1);
        result.Data.DueWithin7DaysCount.Should().Be(1);
        result.Data.Items[0].DaysUntilDue.Should().Be(-2);
    }

    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.FrontDesk)]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.Lead)]
    public async Task GetServiceDueAsync_is_manager_or_office_only(UserRole role)
    {
        var result = await CreateService(new FakeReportsRepository(), role).GetServiceDueAsync(null, null);

        result.Error!.Code.Should().Be("REPORTS_FORBIDDEN");
    }

    [Fact]
    public async Task GetServiceDueAsync_rejects_an_inverted_or_too_long_range()
    {
        var service = CreateService(new FakeReportsRepository(), UserRole.Manager);
        var d = new DateOnly(2026, 10, 1);

        (await service.GetServiceDueAsync(d, d.AddDays(-1))).Error!.Code.Should().Be("REPORTS_VALIDATION");
        (await service.GetServiceDueAsync(d, d.AddDays(400))).Error!.Code.Should().Be("REPORTS_VALIDATION");
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
        public List<Sale> Sales { get; set; } = [];
        public int RetailDraftCount { get; set; }
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

        // กรองช่วงเวลาเหมือน repository จริง — เทสต์ขอบวันตามเวลาไทยจึงมีความหมาย
        public Task<IReadOnlyList<Sale>> GetRetailSalesCompletedInRangeAsync(string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Sale>>(Sales.Where(x => x.CompletedAt >= fromUtc && x.CompletedAt < toUtc).ToList());

        public Task<int> CountRetailDraftsAsync(string shardKey, int branchId, CancellationToken ct) =>
            Task.FromResult(RetailDraftCount);

        public List<Receipt> Receipts { get; set; } = [];
        public List<HandoverRecord> Handovers { get; set; } = [];

        // มิเรอร์ ReportsRepository: เทียบแบบตัดช่องว่าง/ขีดทั้งสองฝั่ง ใหม่สุดก่อน
        public Task<IReadOnlyList<Job>> SearchJobsByVehicleOrPhoneAsync(
            string shardKey, int branchId, string normalizedTerm, int take, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Job>>(Jobs
                .Where(j => ReportsService.NormalizeSearchTerm(j.VehicleRegistration).Contains(normalizedTerm)
                    || ReportsService.NormalizeSearchTerm(j.CustomerPhone).Contains(normalizedTerm))
                .OrderByDescending(j => j.CreatedAt).Take(take).ToList());

        public Task<VehicleHistoryData> GetVehicleHistoryAsync(
            string shardKey, int branchId, long vehicleId, CancellationToken ct)
        {
            var jobs = Jobs.Where(j => j.VehicleId == vehicleId).ToList();
            var ids = jobs.Select(j => j.Id).ToHashSet();
            return Task.FromResult(new VehicleHistoryData(
                jobs,
                Quotations.Where(q => ids.Contains(q.JobId) && q.Status != QuotationStatus.Superseded).ToList(),
                Receipts.Where(r => ids.Contains(r.JobId)).ToList(),
                Handovers.Where(h => ids.Contains(h.JobId)).ToList()));
        }

        public Task<ServiceDueData> GetServiceDueAsync(
            string shardKey, int branchId, DateOnly from, DateOnly to, CancellationToken ct)
        {
            foreach (var h in Handovers) h.Job = Jobs.First(j => j.Id == h.JobId);
            var candidates = Handovers.Where(h => h.SubmittedAt != null && h.NextServiceDueOn >= from && h.NextServiceDueOn <= to).ToList();
            return Task.FromResult(new ServiceDueData(
                candidates,
                Jobs.Select(j => new VehicleVisitRef(j.VehicleId, j.Id, j.CreatedAt, j.Status)).ToList(),
                Handovers.Where(h => h.SubmittedAt != null)
                    .Select(h => new VehicleHandoverRef(h.Job!.VehicleId, h.JobId, h.SubmittedAt!.Value)).ToList()));
        }
    }
}
