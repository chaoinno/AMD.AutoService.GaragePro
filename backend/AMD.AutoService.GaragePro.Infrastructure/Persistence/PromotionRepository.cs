using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class PromotionRepository(ServiceDbContext db, ICurrentUser user) : IPromotionRepository
{
    private IQueryable<Promotion> Items => db.Promotions.Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);
    public async Task<IReadOnlyList<Promotion>> SearchAsync(string? keyword, bool includeInactive, CancellationToken ct) => await Items.AsNoTracking().Where(x => includeInactive || x.IsActive).Where(x => keyword == null || x.Code.Contains(keyword) || x.Name.Contains(keyword)).OrderBy(x => x.Code).ToListAsync(ct);
    public Task<Promotion?> GetAsync(Guid id, CancellationToken ct) => Items.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<bool> CodeExistsAsync(string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct) => db.Promotions.AnyAsync(x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId && x.Code == code && (!excludingId.HasValue || x.Id != excludingId), ct);
    public async Task AddAsync(Promotion promotion, CancellationToken ct) => await db.Promotions.AddAsync(promotion, ct);
    public async Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct) => await db.ActivityEvents.AddAsync(activityEvent, ct);
    public Task<int> SaveChangesAsync(CancellationToken ct) => db.SaveChangesAsync(ct);
}