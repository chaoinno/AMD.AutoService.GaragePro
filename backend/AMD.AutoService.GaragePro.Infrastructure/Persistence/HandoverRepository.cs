using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class HandoverRepository(ServiceDbContext db) : IHandoverRepository
{
    public Task<HandoverRecord?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        db.HandoverRecords
          .Include(r => r.Items)
          .FirstOrDefaultAsync(r => r.JobId == jobId, ct);

    public async Task AddAsync(HandoverRecord record, CancellationToken ct = default) =>
        await db.HandoverRecords.AddAsync(record, ct);

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
