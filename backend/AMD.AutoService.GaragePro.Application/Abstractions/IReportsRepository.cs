using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>อ่านอย่างเดียวจาก ServiceDb (svc_*) เพื่อรวมยอด/นับ ไม่มีคำสั่งเขียน — ไม่ใช่ legacy จึงไม่ต้อง READUNCOMMITTED</summary>
public interface IReportsRepository
{
    Task<IReadOnlyList<Job>> GetJobsAsync(string shardKey, int branchId, CancellationToken ct);

    Task<decimal> GetCollectedAmountAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    Task<int> GetReceiptsIssuedCountAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    /// <summary>event ที่ใช้สร้างไทม์ไลน์สถานะของแต่ละจ๊อบ — job.opened และ job.status.changed เท่านั้น</summary>
    Task<IReadOnlyList<ActivityEvent>> GetJobLifecycleEventsAsync(
        string shardKey, int branchId, CancellationToken ct);

    /// <summary>ใบเสนอราคาที่สร้างในช่วงเวลาที่กำหนด พร้อมบรรทัด (สำหรับรายงานยอดขาย-กำไร)</summary>
    Task<IReadOnlyList<Quotation>> GetQuotationsCreatedInRangeAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    Task<IReadOnlyList<StockLot>> GetStockLotsWithRemainingAsync(
        string shardKey, int branchId, CancellationToken ct);

    Task<IReadOnlyList<CatalogItem>> GetCatalogItemsAsync(string shardKey, int branchId, CancellationToken ct);

    Task<IReadOnlyList<Warehouse>> GetWarehousesAsync(string shardKey, int branchId, CancellationToken ct);

    /// <summary>บิลขายหน้าร้านที่ชำระเงินในช่วง (Completed + Voided ตาม CompletedAt) พร้อมบรรทัดและรายการรับเงิน</summary>
    Task<IReadOnlyList<Sale>> GetRetailSalesCompletedInRangeAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct);

    Task<int> CountRetailDraftsAsync(string shardKey, int branchId, CancellationToken ct);

    /// <summary>จ๊อบที่ทะเบียนหรือเบอร์โทร (snapshot ใน svc_Job) มีข้อความนี้ — เทียบแบบตัดช่องว่าง/ขีดออกทั้งสองฝั่ง
    /// ใหม่สุดก่อน จำกัด take แถว</summary>
    Task<IReadOnlyList<Job>> SearchJobsByVehicleOrPhoneAsync(
        string shardKey, int branchId, string normalizedTerm, int take, CancellationToken ct);

    /// <summary>ทุกอย่างที่ใช้สร้างประวัติรถหนึ่งคัน: จ๊อบ + ใบเสนอราคาที่ยังใช้อยู่ (พร้อมบรรทัด) + ใบเสร็จ + ใบส่งมอบ</summary>
    Task<VehicleHistoryData> GetVehicleHistoryAsync(
        string shardKey, int branchId, long vehicleId, CancellationToken ct);

    /// <summary>ใบส่งมอบที่เซ็นแล้วและวันนัดครั้งถัดไปอยู่ในช่วง [from, to] (รวมปลายทั้งสอง) พร้อม Job
    /// และจ๊อบ/ใบส่งมอบอื่นของรถคันเดียวกัน เพื่อตัดคันที่กลับมาแล้วหรือมีการส่งมอบที่ใหม่กว่า</summary>
    /// <summary>movement ประเภท issue/issue-return/sale/sale-void ที่เกิดใน [fromUtc, toUtc) + ใบคืน/ยกเลิกของใบที่อยู่ในช่วง
    /// (แม้เกิดนอกช่วง) เพื่อคิดจำนวนสุทธิหลังคืน</summary>
    Task<StockUsageData> GetStockUsageAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, StockUsageFilter filter, CancellationToken ct);

    Task<ServiceDueData> GetServiceDueAsync(
        string shardKey, int branchId, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>ทุกสาขาในฐานที่เชื่อมอยู่ (ไม่กรองสาขา/shard ของผู้เรียก) ที่มีใบส่งมอบเซ็นแล้วซึ่งมีรายการนัดครบกำหนดอยู่ใน [from, to]
    /// — ให้งานแจ้งเตือนรายวันรู้ว่าต้องไล่สาขาไหนบ้าง · ใช้ index บนวันนัดของรายการ (ไม่สแกนทุกใบส่งมอบ)</summary>
    Task<IReadOnlyList<ServiceDueBranch>> GetBranchesWithServiceDueAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

public sealed record StockUsageFilter(Guid? WarehouseId, Guid? CatalogItemId, long? RequesterStaffId);

/// <summary>movement ที่ใช้ทำรายงานเบิกอะไหล่ + ข้อมูลอ้างอิงสำหรับแสดงผล (สินค้า · จ๊อบ+ทะเบียน · เลขใบเสร็จขาย)
/// Truncated = เกินเพดานแถวแล้วตัดทิ้ง ผู้เรียกต้องบอกผู้ใช้</summary>
public sealed record StockUsageData(
    IReadOnlyList<StockMovement> Movements,
    IReadOnlyDictionary<Guid, (string Code, string Name, string Unit)> Items,
    IReadOnlyDictionary<Guid, (string JobNo, string? Plate)> Jobs,
    IReadOnlyDictionary<Guid, string?> SaleReceipts,
    bool Truncated);

public sealed record ServiceDueBranch(string ShardKey, int BranchId);

public sealed record VehicleHistoryData(
    IReadOnlyList<Job> Jobs,
    IReadOnlyList<Quotation> Quotations,
    IReadOnlyList<Receipt> Receipts,
    IReadOnlyList<HandoverRecord> Handovers);

/// <summary>จ๊อบอื่นของรถคันเดียวกัน — ใช้ตัดสินว่ารถกลับมาเข้าอู่หลังส่งมอบแล้วหรือยัง</summary>
public sealed record VehicleVisitRef(long VehicleId, Guid JobId, DateTime CreatedAt, Domain.Enums.JobStatus Status);

/// <summary>ใบส่งมอบที่เซ็นแล้วของรถคันเดียวกัน — ใช้หาว่าครั้งไหนล่าสุด</summary>
public sealed record VehicleHandoverRef(long VehicleId, Guid JobId, DateTime SubmittedAt);

public sealed record ServiceDueData(
    IReadOnlyList<HandoverRecord> Candidates,
    IReadOnlyList<VehicleVisitRef> Visits,
    IReadOnlyList<VehicleHandoverRef> Handovers);
