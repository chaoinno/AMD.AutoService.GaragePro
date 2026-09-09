using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IPosRepository
{
    Task<IReadOnlyList<Payment>> GetPaymentsByJobAsync(Guid jobId, CancellationToken ct = default);
    Task<Payment?> GetPaymentByRequestIdAsync(Guid requestId, CancellationToken ct = default);
    Task<Payment?> GetPaymentAsync(Guid jobId, Guid paymentId, CancellationToken ct = default);
    Task AddPaymentAsync(Payment payment, CancellationToken ct = default);
    Task RemovePaymentAsync(Payment payment, CancellationToken ct = default);

    Task<Receipt?> GetReceiptByJobAsync(Guid jobId, CancellationToken ct = default);
    Task AddReceiptAsync(Receipt receipt, CancellationToken ct = default);

    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
