using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class QcChecklistRepository(ServiceDbContext db) : IQcChecklistRepository
{
    public Task<QcChecklist?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        db.QcChecklists
          .Include(c => c.Items)
          .FirstOrDefaultAsync(c => c.JobId == jobId, ct);

    public async Task AddAsync(QcChecklist checklist, CancellationToken ct = default) =>
        await db.QcChecklists.AddAsync(checklist, ct);

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
