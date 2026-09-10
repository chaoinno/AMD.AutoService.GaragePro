using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class ReportsRepository(ServiceDbContext db) : IReportsRepository
{
    public Task<IReadOnlyList<Job>> GetJobsAsync(string shardKey, int branchId, CancellationToken ct) =>
        Scope(db.Jobs, shardKey, branchId).ToListReadOnlyAsync(ct);

    public async Task<decimal> GetCollectedAmountAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        await db.Payments
            .Where(p => p.LegacyShardKey == shardKey && p.LegacyBranchId == branchId
                && p.ReceivedAt >= fromUtc && p.ReceivedAt < toUtc)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

    public async Task<int> GetReceiptsIssuedCountAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        // Receipt itself carries no shard/branch column — scope it through its Job.
        var jobIds = Scope(db.Jobs, shardKey, branchId).Select(j => j.Id);
        return await db.Receipts
            .Where(r => jobIds.Contains(r.JobId) && r.IssuedAt >= fromUtc && r.IssuedAt < toUtc)
            .CountAsync(ct);
    }

    public async Task<IReadOnlyList<ActivityEvent>> GetJobLifecycleEventsAsync(
        string shardKey, int branchId, CancellationToken ct)
    {
        var jobIds = Scope(db.Jobs, shardKey, branchId).Select(j => j.Id);
        return await db.ActivityEvents
            .Where(e => e.JobId != null && jobIds.Contains(e.JobId.Value)
                && (e.EventType == "job.opened" || e.EventType == "job.status.changed"))
            .OrderBy(e => e.JobId).ThenBy(e => e.OccurredAt)
            .ToListAsync(ct);
    }

    public Task<IReadOnlyList<Quotation>> GetQuotationsCreatedInRangeAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        db.Quotations
            .Include(q => q.Lines)
            .Where(q => q.Job!.LegacyShardKey == shardKey && q.Job.BranchId == branchId
                && q.CreatedAt >= fromUtc && q.CreatedAt < toUtc)
            .ToListReadOnlyAsync(ct);

    public Task<IReadOnlyList<StockLot>> GetStockLotsWithRemainingAsync(
        string shardKey, int branchId, CancellationToken ct) =>
        db.Set<StockLot>()
            .Where(l => l.LegacyShardKey == shardKey && l.LegacyBranchId == branchId && l.RemainingQuantity > 0)
            .ToListReadOnlyAsync(ct);

    public Task<IReadOnlyList<CatalogItem>> GetCatalogItemsAsync(
        string shardKey, int branchId, CancellationToken ct) =>
        db.CatalogItems
            .Where(c => c.LegacyShardKey == shardKey && c.LegacyBranchId == branchId)
            .ToListReadOnlyAsync(ct);

    public Task<IReadOnlyList<Warehouse>> GetWarehousesAsync(
        string shardKey, int branchId, CancellationToken ct) =>
        db.Warehouses
            .Where(w => w.LegacyShardKey == shardKey && w.LegacyBranchId == branchId)
            .ToListReadOnlyAsync(ct);

    private static IQueryable<Job> Scope(IQueryable<Job> jobs, string shardKey, int branchId) =>
        jobs.Where(j => j.LegacyShardKey == shardKey && j.BranchId == branchId);
}

internal static class QueryableExtensions
{
    public static async Task<IReadOnlyList<T>> ToListReadOnlyAsync<T>(this IQueryable<T> query, CancellationToken ct) =>
        await query.ToListAsync(ct);
}
