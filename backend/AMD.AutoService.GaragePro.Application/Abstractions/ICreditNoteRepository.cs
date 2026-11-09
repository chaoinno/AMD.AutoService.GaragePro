using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>ใบลดหนี้ + ค่าตั้งภาษีของสาขา (docs/20) — ทุก Get รวมบรรทัดและรายการคืนเงินเสมอ</summary>
public interface ICreditNoteRepository
{
    Task<IReadOnlyList<CreditNote>> GetByJobAsync(Guid jobId, CancellationToken ct = default);
    Task<CreditNote?> GetAsync(Guid id, CancellationToken ct = default);
    Task<CreditNote?> GetByRequestIdAsync(Guid requestId, CancellationToken ct = default);
    Task<bool> RefundRequestExistsAsync(Guid requestId, CancellationToken ct = default);

    /// <summary>ใบลดหนี้ของสาขาที่ออกในช่วง [fromUtc, toUtc) — สำหรับรายงาน</summary>
    Task<IReadOnlyList<CreditNote>> SearchAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>CN-{yy}-{ลำดับ:D4} ต่อ (ชาร์ด, สาขา, ปี) แยกชุดจาก IV-/RC-</summary>
    Task<string> NextDocumentNoAsync(string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default);

    Task<BranchTaxProfile?> GetProfileAsync(string shardKey, int branchId, CancellationToken ct = default);
    Task AddProfileAsync(BranchTaxProfile profile, CancellationToken ct = default);

    Task AddAsync(CreditNote note, CancellationToken ct = default);
    Task AddRefundAsync(CreditNoteRefund refund, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
