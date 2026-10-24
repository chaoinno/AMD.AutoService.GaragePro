using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class ServiceReminderTemplateRepository(ServiceDbContext db) : IServiceReminderTemplateRepository
{
    private IQueryable<ServiceReminderTemplate> Scope(string shardKey, int branchId) =>
        db.ServiceReminderTemplates.Where(t => t.LegacyShardKey == shardKey && t.LegacyBranchId == branchId);

    public Task<bool> AnyAsync(string shardKey, int branchId, CancellationToken ct = default) =>
        Scope(shardKey, branchId).AnyAsync(ct);

    public async Task<IReadOnlyList<ServiceReminderTemplate>> SearchAsync(
        string shardKey, int branchId, bool includeInactive, CancellationToken ct = default)
    {
        var q = Scope(shardKey, branchId).AsNoTracking();
        if (!includeInactive) q = q.Where(t => t.IsActive);
        return await q.OrderBy(t => t.SortOrder).ThenBy(t => t.Name).Take(500).ToListAsync(ct);
    }

    public Task<ServiceReminderTemplate?> GetAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
        Scope(shardKey, branchId).FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<bool> NameExistsAsync(
        string shardKey, int branchId, string name, Guid? excludingId, CancellationToken ct = default) =>
        Scope(shardKey, branchId).AnyAsync(
            t => t.Name == name && (excludingId == null || t.Id != excludingId), ct);

    public async Task AddAsync(ServiceReminderTemplate template, CancellationToken ct = default) =>
        await db.ServiceReminderTemplates.AddAsync(template, ct);

    public async Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(activityEvent, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            var constraint = KnownConstraints.FirstOrDefault(sql.Message.Contains) ?? "UNIQUE_CONSTRAINT";
            throw new MasterDataConflictException(constraint, ex);
        }
    }

    public void DiscardPending()
    {
        foreach (var entry in db.ChangeTracker.Entries().Where(e => e.State == EntityState.Added).ToList())
            entry.State = EntityState.Detached;
    }

    private static readonly string[] KnownConstraints = ["UX_svc_ServiceReminderTemplate_Name"];
}
