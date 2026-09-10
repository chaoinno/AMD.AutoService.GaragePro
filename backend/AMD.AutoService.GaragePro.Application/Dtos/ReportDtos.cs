namespace AMD.AutoService.GaragePro.Application.Dtos;

// ---------- แดชบอร์ดภาพรวมวันนี้ ----------

public sealed record JobStatusCountDto(string Status, string StatusLabelTh, int Count);

public sealed record OverdueJobDto(
    Guid JobId, string JobNo, string CustomerName, string VehicleRegistration,
    string Status, string StatusLabelTh, DateTime PromiseAt);

public sealed record DashboardReportDto(
    IReadOnlyList<JobStatusCountDto> JobsByStatus,
    int OverdueCount,
    IReadOnlyList<OverdueJobDto> OverdueJobs,
    int WaitingQcCount,
    int WaitingPaymentCount,
    decimal CollectedToday,
    int ReceiptsIssuedToday);

// ---------- รอบเวลาต่อขั้นตอนงาน (Job Cycle Time / SLA) ----------

public sealed record StatusDurationDto(
    string Status, string StatusLabelTh, int SegmentCount, double AverageHours);

public sealed record StuckJobDto(
    Guid JobId, string JobNo, string CustomerName, string Status, string StatusLabelTh,
    double HoursInStatus, DateTime? PromiseAt, bool IsOverdue);

public sealed record CycleTimeReportDto(
    DateTime FromDate, DateTime ToDate,
    IReadOnlyList<StatusDurationDto> AverageDurationByStatus,
    int CompletedJobCount,
    double? AverageTotalHours,
    double? P90TotalHours,
    IReadOnlyList<StuckJobDto> TopStuckJobs);

// ---------- ยอดขาย-ต้นทุน-กำไร (Sales & Margin) ----------

public sealed record LineTypeTotalsDto(
    string Type, decimal NetAmount, decimal? CostAmount, decimal? MarginAmount);

public sealed record TechnicianRevenueDto(
    string TechnicianName, int LineCount, decimal NetAmount);

public sealed record SalesMarginReportDto(
    DateTime FromDate, DateTime ToDate,
    int QuotationCount,
    decimal NetAmount,
    decimal? CostAmount,
    decimal? MarginAmount,
    decimal? MarginPercent,
    IReadOnlyList<LineTypeTotalsDto> ByType,
    IReadOnlyList<TechnicianRevenueDto> ByTechnician);

// ---------- สต็อกสินค้า ----------

public sealed record StockAgingBucketDto(string BucketLabelTh, int MinDays, int? MaxDays, decimal Value);

public sealed record AgingStockLotDto(
    string CatalogCode, string CatalogName, string WarehouseName,
    int RemainingQuantity, decimal UnitCost, int AgeDays);

public sealed record StockReportDto(
    decimal TotalValuation,
    decimal DamagedValuation,
    IReadOnlyList<StockAgingBucketDto> AgingBuckets,
    IReadOnlyList<AgingStockLotDto> OldestLots);
