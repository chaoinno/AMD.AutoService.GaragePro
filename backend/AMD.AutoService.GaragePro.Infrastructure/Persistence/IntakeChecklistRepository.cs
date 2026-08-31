using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class IntakeChecklistRepository(ServiceDbContext db) : IIntakeChecklistRepository
{
    public Task<IntakeChecklist?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        db.IntakeChecklists
          .Include(c => c.Items)
          .FirstOrDefaultAsync(c => c.JobId == jobId, ct);

    public async Task AddAsync(IntakeChecklist checklist, CancellationToken ct = default) =>
        await db.IntakeChecklists.AddAsync(checklist, ct);

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
