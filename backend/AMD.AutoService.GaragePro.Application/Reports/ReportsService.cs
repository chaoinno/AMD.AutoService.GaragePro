using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;

namespace AMD.AutoService.GaragePro.Application.Reports;

/// <summary>
/// รายงานอ่านอย่างเดียวจากข้อมูลที่มีอยู่แล้ว (Job/ActivityEvent/Quotation/StockLot) — ไม่มีคำสั่งเขียน
/// [BIZ] เฉพาะผู้จัดการ/ธุรการเท่านั้นที่ดูรายงานได้ · ต้นทุน/กำไร strip ตาม role เหมือน QuotationMapper (invariant #7)
/// </summary>
public sealed class ReportsService(IReportsRepository repo, ICurrentUser user, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool Allowed => user.Role is UserRole.Manager or UserRole.Office;

    private static Result<T> Forbidden<T>() =>
        Result<T>.Fail("REPORTS_FORBIDDEN", "เฉพาะผู้จัดการหรือธุรการเท่านั้นที่ดูรายงานได้");

    public async Task<Result<DashboardReportDto>> GetDashboardAsync(CancellationToken ct = default)
    {
        if (!Allowed) return Forbidden<DashboardReportDto>();

        var now = Now;
        var jobs = await repo.GetJobsAsync(user.ShardKey, user.BranchId, ct);

        var byStatus = Enum.GetValues<JobStatus>()
            .Select(s => new JobStatusCountDto(
                JobStateMachine.ToToken(s), JobStateMachine.Describe(s), jobs.Count(j => j.Status == s)))
            .ToList();

        var overdueJobs = jobs.Where(j => j.IsOverdue(now)).OrderBy(j => j.PromiseAt).ToList();

        var (dayStartUtc, dayEndUtc) = ThaiDayBoundsUtc(now);
        var collectedToday = await repo.GetCollectedAmountAsync(user.ShardKey, user.BranchId, dayStartUtc, dayEndUtc, ct);
        var receiptsToday = await repo.GetReceiptsIssuedCountAsync(user.ShardKey, user.BranchId, dayStartUtc, dayEndUtc, ct);
        var retailToday = (await repo.GetRetailSalesCompletedInRangeAsync(user.ShardKey, user.BranchId, dayStartUtc, dayEndUtc, ct))
            .Where(x => x.Status == SaleStatus.Completed).ToList();
        var retailDrafts = await repo.CountRetailDraftsAsync(user.ShardKey, user.BranchId, ct);

        return Result<DashboardReportDto>.Ok(new DashboardReportDto(
            JobsByStatus: byStatus,
            OverdueCount: overdueJobs.Count,
            OverdueJobs: overdueJobs.Take(10).Select(j => new OverdueJobDto(
                j.Id, j.JobNo, j.CustomerName, j.VehicleRegistration,
                JobStateMachine.ToToken(j.Status), JobStateMachine.Describe(j.Status), j.PromiseAt!.Value)).ToList(),
            WaitingQcCount: jobs.Count(j => j.Status == JobStatus.Qc),
            WaitingPaymentCount: jobs.Count(j => j.Status == JobStatus.Ready),
            CollectedToday: collectedToday,
            ReceiptsIssuedToday: receiptsToday,
            RetailToday: new RetailSalesTodayDto(
                retailToday.Count, retailToday.Sum(x => x.TotalAmount),
                retailToday.Sum(x => x.Lines.Sum(l => l.Quantity)), retailDrafts)));
    }

    public async Task<Result<CycleTimeReportDto>> GetCycleTimeAsync(
        DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        if (!Allowed) return Forbidden<CycleTimeReportDto>();

        var now = Now;
        var to = toDate ?? now;
        var from = fromDate ?? to.AddDays(-30);

        var jobs = await repo.GetJobsAsync(user.ShardKey, user.BranchId, ct);
        var jobsById = jobs.ToDictionary(j => j.Id);
        var events = await repo.GetJobLifecycleEventsAsync(user.ShardKey, user.BranchId, ct);
        var eventsByJob = events.GroupBy(e => e.JobId!.Value).ToDictionary(g => g.Key, g => g.OrderBy(e => e.OccurredAt).ToList());

        // ---- ค่าเฉลี่ยเวลาต่อสถานะ + รอบเวลารวม — เฉพาะจ๊อบที่เปิดในช่วงที่เลือก ----
        var segments = new List<(JobStatus Status, double Hours)>();
        var totalHoursOfClosedJobs = new List<double>();

        foreach (var job in jobs.Where(j => j.CreatedAt >= from && j.CreatedAt <= to))
        {
            if (!eventsByJob.TryGetValue(job.Id, out var timeline) || timeline.Count == 0) continue;

            for (var i = 0; i < timeline.Count - 1; i++)
            {
                var status = i == 0 ? JobStatus.WaitInspect : ParsePayloadTo(timeline[i].PayloadJson);
                if (status is null) continue;
                segments.Add((status.Value, (timeline[i + 1].OccurredAt - timeline[i].OccurredAt).TotalHours));
            }

            if (JobStateMachine.IsTerminal(job.Status))
                totalHoursOfClosedJobs.Add((timeline[^1].OccurredAt - timeline[0].OccurredAt).TotalHours);
        }

        var averageDurationByStatus = segments
            .GroupBy(s => s.Status)
            .OrderBy(g => g.Key)   // JobStatus values are declared in workflow order (WaitInspect=1..Cancelled=10)
            .Select(g => new StatusDurationDto(JobStateMachine.ToToken(g.Key), JobStateMachine.Describe(g.Key), g.Count(), Math.Round(g.Average(x => x.Hours), 1)))
            .ToList();

        // ---- จ๊อบที่ค้างในสถานะปัจจุบันนานที่สุด — จากสถานะจริงของ Job ปัจจุบัน ไม่พึ่ง payload ----
        var stuckJobs = jobs
            .Where(j => !JobStateMachine.IsTerminal(j.Status))
            .Select(j => new
            {
                Job = j,
                LastTransitionAt = eventsByJob.TryGetValue(j.Id, out var t) && t.Count > 0 ? t[^1].OccurredAt : j.CreatedAt
            })
            .OrderByDescending(x => (now - x.LastTransitionAt).TotalHours)
            .Take(10)
            .Select(x => new StuckJobDto(
                x.Job.Id, x.Job.JobNo, x.Job.CustomerName,
                JobStateMachine.ToToken(x.Job.Status), JobStateMachine.Describe(x.Job.Status),
                Math.Round((now - x.LastTransitionAt).TotalHours, 1),
                x.Job.PromiseAt, x.Job.IsOverdue(now)))
            .ToList();

        var sortedTotals = totalHoursOfClosedJobs.OrderBy(h => h).ToList();

        return Result<CycleTimeReportDto>.Ok(new CycleTimeReportDto(
            FromDate: from, ToDate: to,
            AverageDurationByStatus: averageDurationByStatus,
            CompletedJobCount: sortedTotals.Count,
            AverageTotalHours: sortedTotals.Count > 0 ? Math.Round(sortedTotals.Average(), 1) : null,
            P90TotalHours: sortedTotals.Count > 0 ? Math.Round(Percentile(sortedTotals, 0.9), 1) : null,
            TopStuckJobs: stuckJobs));
    }

    public async Task<Result<SalesMarginReportDto>> GetSalesMarginAsync(
        DateTime? fromDate, DateTime? toDate, CancellationToken ct = default)
    {
        if (!Allowed) return Forbidden<SalesMarginReportDto>();

        var now = Now;
        var to = toDate ?? now;
        var from = fromDate ?? new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var quotations = await repo.GetQuotationsCreatedInRangeAsync(user.ShardKey, user.BranchId, from, to, ct);
        var approvedLines = quotations.SelectMany(q => q.Lines)
            .Where(l => l.ApprovalStatus == LineApprovalStatus.Approved)
            .ToList();

        var net = approvedLines.Sum(l => l.NetAmount);
        var cost = approvedLines.Sum(l => l.CostAmount);
        var margin = net - cost;
        var marginPercent = net == 0m ? 0m : Math.Round((net - cost) / net * 100m, 2);
        var canSeeCost = user.CanSeeCost;

        var byType = approvedLines
            .GroupBy(l => l.Type)
            .Select(g => new LineTypeTotalsDto(
                g.Key.ToString(), g.Sum(l => l.NetAmount),
                canSeeCost ? g.Sum(l => l.CostAmount) : null,
                canSeeCost ? g.Sum(l => l.MarginAmount) : null))
            .ToList();

        // ค่าแรงเท่านั้นที่ผูกกับช่างคนเดียว — อะไหล่ไม่มีเจ้าของงาน จึงไม่รวมในรายงานนี้
        var byTechnician = approvedLines
            .Where(l => l.Type == LineType.Labor && !string.IsNullOrWhiteSpace(l.AssignedTechnicianName))
            .GroupBy(l => l.AssignedTechnicianName!)
            .Select(g => new TechnicianRevenueDto(g.Key, g.Count(), g.Sum(l => l.NetAmount)))
            .OrderByDescending(t => t.NetAmount)
            .ToList();

        return Result<SalesMarginReportDto>.Ok(new SalesMarginReportDto(
            FromDate: from, ToDate: to,
            QuotationCount: quotations.Count,
            NetAmount: net,
            CostAmount: canSeeCost ? cost : null,
            MarginAmount: canSeeCost ? margin : null,
            MarginPercent: canSeeCost ? marginPercent : null,
            ByType: byType,
            ByTechnician: byTechnician));
    }

    public async Task<Result<StockReportDto>> GetStockAsync(CancellationToken ct = default)
    {
        if (!Allowed) return Forbidden<StockReportDto>();

        var now = Now;
        var lots = await repo.GetStockLotsWithRemainingAsync(user.ShardKey, user.BranchId, ct);
        var catalogItems = await repo.GetCatalogItemsAsync(user.ShardKey, user.BranchId, ct);
        var warehouses = await repo.GetWarehousesAsync(user.ShardKey, user.BranchId, ct);
        var catalogById = catalogItems.ToDictionary(c => c.Id);
        var warehouseById = warehouses.ToDictionary(w => w.Id);

        var totalValuation = lots.Sum(l => l.RemainingQuantity * l.UnitCost);
        var damagedValuation = catalogItems.Sum(c => c.Damaged * c.Cost);

        (string Label, int Min, int? Max)[] bucketDefs =
        [
            ("0–30 วัน", 0, 30),
            ("31–60 วัน", 31, 60),
            ("61–90 วัน", 61, 90),
            ("มากกว่า 90 วัน", 91, null)
        ];
        var agingBuckets = bucketDefs
            .Select(b => new StockAgingBucketDto(b.Label, b.Min, b.Max, lots
                .Where(l => AgeDays(l, now) >= b.Min && (b.Max is null || AgeDays(l, now) <= b.Max))
                .Sum(l => l.RemainingQuantity * l.UnitCost)))
            .ToList();

        var oldestLots = lots
            .OrderByDescending(l => AgeDays(l, now))
            .Take(15)
            .Select(l => new AgingStockLotDto(
                catalogById.TryGetValue(l.CatalogItemId, out var c) ? c.Code : "—",
                catalogById.TryGetValue(l.CatalogItemId, out var c2) ? c2.Name : "ไม่พบสินค้าในแคตตาล็อกแล้ว",
                warehouseById.TryGetValue(l.WarehouseId, out var w) ? w.Name : "—",
                l.RemainingQuantity, l.UnitCost, AgeDays(l, now)))
            .ToList();

        return Result<StockReportDto>.Ok(new StockReportDto(totalValuation, damagedValuation, agingBuckets, oldestLots));
    }

    /// <summary>ช่วงวันที่ยาวสุดของรายงานขายหน้าร้าน — กันดึงบิลทั้งปีหลายปีมาคิดในหน่วยความจำครั้งเดียว</summary>
    public const int RetailReportMaxDays = 366;

    /// <summary>
    /// รายงานขายหน้าร้าน — fromDate/toDate เป็น "วันที่ตามปฏิทินไทย" (รวมทั้งสองวัน) ไม่ใช่เวลา UTC
    /// เพื่อไม่ให้บิลตอนเช้ามืด (00:00–07:00 ไทย) ไปตกวันก่อนหน้าแบบที่เกิดเมื่อส่งเวลา local มาเทียบกับ UTC
    /// </summary>
    public async Task<Result<RetailSalesReportDto>> GetRetailSalesAsync(
        DateOnly? fromDate, DateOnly? toDate, CancellationToken ct = default)
    {
        if (!Allowed) return Forbidden<RetailSalesReportDto>();

        var today = DateOnly.FromDateTime(Now.AddHours(7));
        var to = toDate ?? today;
        var from = fromDate ?? new DateOnly(to.Year, to.Month, 1);
        if (from > to)
            return Result<RetailSalesReportDto>.Fail("REPORTS_VALIDATION", "วันที่เริ่มต้องไม่เกินวันที่สิ้นสุด");
        if (to.DayNumber - from.DayNumber + 1 > RetailReportMaxDays)
            return Result<RetailSalesReportDto>.Fail("REPORTS_VALIDATION", $"เลือกช่วงวันที่ได้ไม่เกิน {RetailReportMaxDays} วัน");

        var fromUtc = ThaiDateStartUtc(from);
        var toUtc = ThaiDateStartUtc(to.AddDays(1));
        var sales = await repo.GetRetailSalesCompletedInRangeAsync(user.ShardKey, user.BranchId, fromUtc, toUtc, ct);
        var completed = sales.Where(x => x.Status == SaleStatus.Completed).ToList();
        var voided = sales.Where(x => x.Status == SaleStatus.Voided).OrderByDescending(x => x.VoidedAt).ToList();
        var canSeeCost = user.CanSeeCost;

        var total = completed.Sum(x => x.TotalAmount);
        var net = completed.Sum(x => x.NetAmount);
        var cost = completed.Sum(x => x.CostTotal);
        var margin = net - cost;

        var byDay = completed.GroupBy(x => ThaiDate(x.CompletedAt!.Value)).ToDictionary(g => g.Key, g => g.ToList());
        // ทุกวันในช่วง รวมวันที่ไม่มีบิล — กราฟต้องเห็น "วันที่ขายไม่ได้" ไม่ใช่ข้ามไปเงียบๆ
        var daily = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(i => from.AddDays(i))
            .Select(d => byDay.TryGetValue(d, out var list)
                ? new RetailDailyDto(d, list.Count, list.Sum(x => x.TotalAmount))
                : new RetailDailyDto(d, 0, 0m))
            .ToList();

        var byMethod = completed.SelectMany(x => x.Payments)
            .GroupBy(p => p.Method)
            .Select(g => new RetailPaymentMethodDto(PosMapper.ToMethodToken(g.Key), g.Count(), g.Sum(p => p.Amount)))
            .OrderByDescending(x => x.Amount)
            .ToList();

        var products = completed
            .SelectMany(x => x.Lines.Select(l => (Sale: x, Line: l)))
            .GroupBy(x => x.Line.CatalogItemId)
            .Select(g =>
            {
                var latest = g.OrderByDescending(x => x.Sale.CompletedAt).First().Line;
                var lineNet = g.Sum(x => x.Line.NetAmount);
                var lineCost = g.Sum(x => x.Line.CostAmount ?? 0m);
                return new RetailTopProductDto(
                    latest.Code, latest.Name, latest.Unit, g.Sum(x => x.Line.Quantity), g.Select(x => x.Sale.Id).Distinct().Count(),
                    lineNet, canSeeCost ? lineCost : null, canSeeCost ? lineNet - lineCost : null);
            })
            .OrderByDescending(x => x.NetAmount).ThenByDescending(x => x.Quantity)
            .Take(20)
            .ToList();

        var linePromos = completed.SelectMany(x => x.Lines)
            .Where(l => l.PromotionId.HasValue && l.PromotionAmount > 0)
            .GroupBy(l => l.PromotionId!.Value)
            .Select(g => new RetailPromotionUsageDto(g.First().PromotionName ?? g.First().PromotionCode ?? "โปรโมชัน", "line", g.Count(), g.Sum(l => l.PromotionAmount)));
        var billPromos = completed
            .Where(x => x.BillPromotionId.HasValue && x.BillPromotionAmount > 0)
            .GroupBy(x => x.BillPromotionId!.Value)
            .Select(g => new RetailPromotionUsageDto(g.First().BillPromotionName ?? g.First().BillPromotionCode ?? "โปรโมชันท้ายบิล", "bill", g.Count(), g.Sum(x => x.BillPromotionAmount)));
        var promotions = linePromos.Concat(billPromos).OrderByDescending(x => x.DiscountAmount).ToList();

        var bySeller = completed
            .GroupBy(x => string.IsNullOrWhiteSpace(x.CompletedByName) ? "ไม่ระบุ" : x.CompletedByName!)
            .Select(g => new RetailSellerDto(g.Key, g.Count(), g.Sum(x => x.TotalAmount)))
            .OrderByDescending(x => x.TotalAmount)
            .ToList();

        return Result<RetailSalesReportDto>.Ok(new RetailSalesReportDto(
            from, to,
            BillCount: completed.Count,
            TotalAmount: total,
            NetAmount: net,
            VatAmount: completed.Sum(x => x.VatAmount),
            AverageBillAmount: completed.Count == 0 ? 0m : Math.Round(total / completed.Count, 2, MidpointRounding.AwayFromZero),
            DiscountAmount: completed.Sum(x => x.LineDiscountAmount + x.LinePromotionAmount + x.BillDiscountAmount + x.BillPromotionAmount),
            ItemQuantity: completed.Sum(x => x.Lines.Sum(l => l.Quantity)),
            CostAmount: canSeeCost ? cost : null,
            MarginAmount: canSeeCost ? margin : null,
            MarginPercent: canSeeCost ? (net == 0m ? 0m : Math.Round(margin / net * 100m, 2)) : null,
            VoidedCount: voided.Count,
            VoidedAmount: voided.Sum(x => x.TotalAmount),
            Daily: daily,
            ByPaymentMethod: byMethod,
            TopProducts: products,
            Promotions: promotions,
            BySeller: bySeller,
            VoidedSales: voided.Select(x => new RetailVoidedSaleDto(
                x.Id, x.ReceiptNo, x.CompletedAt, x.VoidedAt, x.VoidedByName, x.VoidReason, x.TotalAmount)).ToList()));
    }

    private static DateOnly ThaiDate(DateTime utc) => DateOnly.FromDateTime(utc.AddHours(7));
    private static DateTime ThaiDateStartUtc(DateOnly date) =>
        DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc);

    private static int AgeDays(StockLot lot, DateTime now) => (int)(now - lot.ReceivedAt).TotalDays;

    private static JobStatus? ParsePayloadTo(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            if (doc.RootElement.TryGetProperty("to", out var el)
                && Enum.TryParse<JobStatus>(el.GetString(), out var status))
                return status;
        }
        catch (JsonException) { /* event เก่าก่อนมี payload นี้ — ข้ามช่วงนี้ไป ไม่นับผิด */ }
        return null;
    }

    /// <summary>เที่ยงคืนตามเวลาไทย (UTC+7) แปลงกลับเป็นขอบเขต UTC — แนวเดียวกับ AuthService.GetShiftsAsync</summary>
    private static (DateTime StartUtc, DateTime EndUtc) ThaiDayBoundsUtc(DateTime nowUtc)
    {
        var thaiDate = nowUtc.AddHours(7).Date;
        var startUtc = DateTime.SpecifyKind(thaiDate.AddHours(-7), DateTimeKind.Utc);
        return (startUtc, startUtc.AddDays(1));
    }

    private static double Percentile(IReadOnlyList<double> sortedAscending, double p)
    {
        if (sortedAscending.Count == 1) return sortedAscending[0];
        var rank = p * (sortedAscending.Count - 1);
        var lower = (int)Math.Floor(rank);
        var upper = (int)Math.Ceiling(rank);
        if (lower == upper) return sortedAscending[lower];
        return sortedAscending[lower] + (rank - lower) * (sortedAscending[upper] - sortedAscending[lower]);
    }
}
