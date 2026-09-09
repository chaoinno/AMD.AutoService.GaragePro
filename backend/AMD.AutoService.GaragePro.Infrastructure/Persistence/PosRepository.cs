using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
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

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
