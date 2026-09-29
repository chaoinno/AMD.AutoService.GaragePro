using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IPromotionRepository
{
    Task<IReadOnlyList<Promotion>> SearchAsync(string? keyword, bool includeInactive, CancellationToken ct);
    Task<Promotion?> GetAsync(Guid id, CancellationToken ct);
    Task<bool> CodeExistsAsync(string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct);
    Task AddAsync(Promotion promotion, CancellationToken ct);
    Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct);
    Task<int> SaveChangesAsync(CancellationToken ct);
}