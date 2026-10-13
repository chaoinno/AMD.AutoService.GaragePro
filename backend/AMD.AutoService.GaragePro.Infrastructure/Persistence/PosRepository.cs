using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class PosRepository(ServiceDbContext db) : IPosRepository
{
    public async Task<IReadOnlyList<Payment>> GetPaymentsByJobAsync(Guid jobId, CancellationToken ct = default) =>
        await db.Payments.Where(p => p.JobId == jobId).OrderBy(p => p.ReceivedAt).ToListAsync(ct);

    public Task<Payment?> GetPaymentByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.RequestId == requestId, ct);

    public Task<Payment?> GetPaymentAsync(Guid jobId, Guid paymentId, CancellationToken ct = default) =>
        db.Payments.FirstOrDefaultAsync(p => p.JobId == jobId && p.Id == paymentId, ct);

    public async Task AddPaymentAsync(Payment payment, CancellationToken ct = default) =>
        await db.Payments.AddAsync(payment, ct);

    public Task RemovePaymentAsync(Payment payment, CancellationToken ct = default)
    {
        db.Payments.Remove(payment);
        return Task.CompletedTask;
    }

    public Task<Receipt?> GetReceiptByJobAsync(Guid jobId, CancellationToken ct = default) =>
        db.Receipts.FirstOrDefaultAsync(r => r.JobId == jobId, ct);

    public async Task AddReceiptAsync(Receipt receipt, CancellationToken ct = default) =>
        await db.Receipts.AddAsync(receipt, ct);

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    /// <summary>
    /// ชน unique ของตารางในโมดูลนี้ (เลขใบเสร็จซ้ำในสาขาเดียวกัน · ออกใบเสร็จงานเดิมพร้อมกันสองคำขอ · RequestId
    /// ของการชำระเงินซ้ำ) — แปลงเป็น exception ของ Application แทนที่จะหลุดไปเป็น HTTP 500 ดิบ
    /// (pattern เดียวกับ MasterDataRepository.SaveChangesAsync)
    /// </summary>
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            var constraint = KnownConstraints.FirstOrDefault(sql.Message.Contains) ?? "UNIQUE_CONSTRAINT";
            throw new UniqueConstraintViolationException(constraint, ex);
        }
    }

    private static readonly string[] KnownConstraints =
    [
        "UX_svc_Receipt_Scope_DocumentNo",
        "IX_svc_Receipt_JobId",
        "IX_svc_Payment_RequestId"
    ];
}
