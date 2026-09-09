using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IHandoverRepository
{
    Task<HandoverRecord?> GetByJobAsync(Guid jobId, CancellationToken ct = default);

    Task AddAsync(HandoverRecord record, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
