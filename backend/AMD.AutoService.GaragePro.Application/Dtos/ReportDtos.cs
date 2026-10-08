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
    int ReceiptsIssuedToday,
    RetailSalesTodayDto? RetailToday = null);

/// <summary>widget ขายหน้าร้านบนแดชบอร์ด — นับเฉพาะบิลที่ชำระแล้วและยังไม่ถูกยกเลิก (docs/11)</summary>
public sealed record RetailSalesTodayDto(int BillCount, decimal TotalAmount, int ItemQuantity, int DraftCount);

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

// ---------- ขายหน้าร้าน (Retail POS) — docs/11-retail-sale-pos.md ----------

/// <summary>
/// [BIZ] ยอดขายนับเฉพาะบิล Completed ตาม "วันที่ชำระเงิน" (CompletedAt, วันตามเวลาไทย)
/// บิลที่ชำระในช่วงแล้วถูกยกเลิกภายหลังไม่นับในยอด แต่แสดงแยกใน VoidedSales · ต้นทุน/กำไร strip ตาม role
/// </summary>
public sealed record RetailSalesReportDto(
    DateOnly FromDate, DateOnly ToDate,
    int BillCount, decimal TotalAmount, decimal NetAmount, decimal VatAmount, decimal AverageBillAmount,
    decimal DiscountAmount, int ItemQuantity,
    decimal? CostAmount, decimal? MarginAmount, decimal? MarginPercent,
    int VoidedCount, decimal VoidedAmount,
    IReadOnlyList<RetailDailyDto> Daily,
    IReadOnlyList<RetailPaymentMethodDto> ByPaymentMethod,
    IReadOnlyList<RetailTopProductDto> TopProducts,
    IReadOnlyList<RetailPromotionUsageDto> Promotions,
    IReadOnlyList<RetailSellerDto> BySeller,
    IReadOnlyList<RetailVoidedSaleDto> VoidedSales);

public sealed record RetailDailyDto(DateOnly Date, int BillCount, decimal TotalAmount);
public sealed record RetailPaymentMethodDto(string Method, int PaymentCount, decimal Amount);

/// <summary>ยอดต่อสินค้าเป็น "ยอดตามบรรทัด" หลังส่วนลด/โปรรายบรรทัด แต่ก่อนส่วนลดท้ายบิลและ VAT</summary>
public sealed record RetailTopProductDto(
    string Code, string Name, string Unit, int Quantity, int BillCount, decimal NetAmount,
    decimal? CostAmount, decimal? MarginAmount);

public sealed record RetailPromotionUsageDto(string Name, string Scope, int UseCount, decimal DiscountAmount);
public sealed record RetailSellerDto(string SellerName, int BillCount, decimal TotalAmount);
public sealed record RetailVoidedSaleDto(
    Guid SaleId, string? ReceiptNo, DateTime? CompletedAt, DateTime? VoidedAt, string? VoidedByName,
    string? VoidReason, decimal TotalAmount);

// ---------- ประวัติรถ (เปิดทุกบทบาท — ช่าง/หัวหน้าช่างไม่เห็นตัวเงิน) ----------

public sealed record VehicleHistoryMatchDto(
    long VehicleId, string VehicleRegistration, string? VehicleModel, string CustomerName, string? CustomerPhone,
    int VisitCount, DateTime LastVisitAt, string LastJobNo);

/// <param name="Truncated">จ๊อบที่ตรงคำค้นเกินเพดาน — รายการรถอาจไม่ครบ ให้หน้าจอบอกให้พิมพ์ให้เจาะจงขึ้น</param>
public sealed record VehicleHistorySearchDto(IReadOnlyList<VehicleHistoryMatchDto> Items, bool Truncated);

/// <param name="Amount">ยอดก่อน VAT ของบรรทัด — null เมื่อบทบาทนี้ไม่เห็นตัวเงิน</param>
public sealed record VehicleHistoryLineDto(
    string QuotationCode, string Name, string Type, decimal Quantity, string Unit, decimal? Amount, string? TechnicianName);

public sealed record VehicleHistoryVisitDto(
    Guid JobId, string JobNo, DateTime OpenedAt, DateTime? HandedOverAt, string? JobTypeName,
    string Status, string StatusLabelTh, string? Detail,
    int? MileageAtIntake, int? MileageAtHandover,
    IReadOnlyList<VehicleHistoryLineDto> Lines,
    string? ReceiptDocumentNo, decimal? ReceiptTotal,
    int? NextServiceMileage, DateOnly? NextServiceDueOn);

/// <param name="ShowAmounts">false = บทบาทนี้ไม่เห็นตัวเงิน (ช่าง/หัวหน้าช่าง) — ยอดเงินทุกช่องเป็น null</param>
/// <param name="NextService">นัดครั้งถัดไปจากการส่งมอบล่าสุด (ถ้ามี)</param>
public sealed record VehicleHistoryDto(
    long VehicleId, string VehicleRegistration, string? VehicleModel, string? VehicleVin,
    string CustomerName, string? CustomerPhone, bool ShowAmounts,
    VehicleNextServiceDto? NextService,
    IReadOnlyList<VehicleHistoryVisitDto> Visits);

public sealed record VehicleNextServiceDto(string FromJobNo, int? Mileage, DateOnly DueOn);

// ---------- รถใกล้ครบรอบบริการ (CRM — ผู้จัดการ/ธุรการ) ----------

/// <param name="DaysUntilDue">ติดลบ = เลยกำหนดมาแล้วกี่วัน</param>
public sealed record ServiceDueItemDto(
    long VehicleId, string VehicleRegistration, string? VehicleModel, string CustomerName, string? CustomerPhone,
    Guid LastJobId, string LastJobNo, DateTime HandedOverAt, int? MileageAtHandover,
    int? NextServiceMileage, DateOnly NextServiceDueOn, int DaysUntilDue);

public sealed record ServiceDueReportDto(
    DateOnly FromDate, DateOnly ToDate, DateOnly Today,
    int OverdueCount, int DueWithin7DaysCount, int DueWithin30DaysCount,
    IReadOnlyList<ServiceDueItemDto> Items);
