using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IQcChecklistRepository
{
    Task<QcChecklist?> GetByJobAsync(Guid jobId, CancellationToken ct = default);

    Task AddAsync(QcChecklist checklist, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
