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
}
