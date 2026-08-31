using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IIntakeChecklistRepository
{
    Task<IntakeChecklist?> GetByJobAsync(Guid jobId, CancellationToken ct = default);

    Task AddAsync(IntakeChecklist checklist, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
