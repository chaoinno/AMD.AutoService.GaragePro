using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IQuotationRepository
{
    Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default);

    /// <summary>ทุกใบของงานนี้ที่ยังไม่ถูกแทนที่ (รวมใบร่าง) พร้อมบรรทัด+การอนุมัติ เรียงตามเวอร์ชัน
    /// [BIZ] จ๊อบมีใบเสนอราคาได้หลายใบ (บิลแยก) — ห้ามสรุปจาก "ใบล่าสุด" ใบเดียวอีกต่อไป ดู Domain JobQuotations</summary>
    Task<IReadOnlyList<Quotation>> GetActiveForJobAsync(Guid jobId, CancellationToken ct = default);

    Task<IReadOnlyList<Quotation>> GetQueueAsync(
        string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default);

    Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default);

    Task AddAsync(Quotation quotation, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ICatalogRepository
{
    Task<IReadOnlyList<CatalogItem>> SearchAsync(
        string shardKey, int branchId, string? keyword, CancellationToken ct = default);

    Task<IReadOnlyList<CatalogItem>> GetByCodesAsync(
        string shardKey, int branchId, IEnumerable<string> codes, CancellationToken ct = default);

    Task<(IReadOnlyList<CatalogItem> Items, int Total)> SearchManagementAsync(
        string shardKey, int branchId, CatalogManagementQuery query, CancellationToken ct = default);
    Task<CatalogItem?> GetAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string shardKey, int branchId, string code, Guid? excludingId,
        CancellationToken ct = default);
    Task AddAsync(CatalogItem item, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
