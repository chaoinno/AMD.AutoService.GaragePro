using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface ITaxInvoiceRepository
{
    /// <summary>รวมบรรทัดเสมอ — เอกสารที่ไม่มีรายการใช้พิมพ์ไม่ได้</summary>
    Task<TaxInvoice?> GetByJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>IV-{yy}-{ลำดับ:D4} — ลำดับเริ่มนับใหม่ทุกปีต่อ (ชาร์ด, สาขา) แยกชุดจากใบเสร็จ RC-</summary>
    Task<string> NextDocumentNoAsync(string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default);

    Task AddAsync(TaxInvoice invoice, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
