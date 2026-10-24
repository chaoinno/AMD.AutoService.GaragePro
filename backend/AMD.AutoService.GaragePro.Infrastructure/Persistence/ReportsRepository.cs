using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class ReportsRepository(ServiceDbContext db) : IReportsRepository
{
    public Task<IReadOnlyList<Job>> GetJobsAsync(string shardKey, int branchId, CancellationToken ct) =>
        Scope(db.Jobs, shardKey, branchId).ToListReadOnlyAsync(ct);

    public async Task<decimal> GetCollectedAmountAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var jobPayments = await db.Payments
            .Where(p => p.LegacyShardKey == shardKey && p.LegacyBranchId == branchId
                && p.ReceivedAt >= fromUtc && p.ReceivedAt < toUtc)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        var retailPayments = await db.SalePayments
            .Where(p => p.Sale!.LegacyShardKey == shardKey && p.Sale.LegacyBranchId == branchId
                && p.Sale.Status == Domain.Enums.SaleStatus.Completed
                && p.ReceivedAt >= fromUtc && p.ReceivedAt < toUtc)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
        return jobPayments + retailPayments;
    }

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

    public Task<IReadOnlyList<Sale>> GetRetailSalesCompletedInRangeAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct) =>
        db.Sales.AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .Where(s => s.LegacyShardKey == shardKey && s.LegacyBranchId == branchId
                && (s.Status == Domain.Enums.SaleStatus.Completed || s.Status == Domain.Enums.SaleStatus.Voided)
                && s.CompletedAt >= fromUtc && s.CompletedAt < toUtc)
            .AsSplitQuery()
            .ToListReadOnlyAsync(ct);

    private const int StockUsageMaxRows = 50_000;
    private static readonly string[] StockUsageTypes = ["issue", "issue-return", "sale", "sale-void"];

    public async Task<StockUsageData> GetStockUsageAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, StockUsageFilter filter, CancellationToken ct)
    {
        IQueryable<StockMovement> Scoped() => db.Set<StockMovement>().AsNoTracking()
            .Where(x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId && StockUsageTypes.Contains(x.Type)
                && (filter.WarehouseId == null || x.WarehouseId == filter.WarehouseId)
                && (filter.CatalogItemId == null || x.CatalogItemId == filter.CatalogItemId)
                && (filter.RequesterStaffId == null || x.RequesterStaffId == filter.RequesterStaffId));

        var movements = await Scoped().Where(x => x.OccurredAt >= fromUtc && x.OccurredAt < toUtc)
            .OrderBy(x => x.OccurredAt).ThenBy(x => x.Id).Take(StockUsageMaxRows + 1).ToListAsync(ct);
        var truncated = movements.Count > StockUsageMaxRows;
        if (truncated) movements.RemoveAt(movements.Count - 1);

        // ใบคืน/ยกเลิกบิลที่เกิดนอกช่วงแต่เป็นของใบที่อยู่ในช่วง — ไว้คิดจำนวนสุทธิของใบนั้น
        var issueIds = movements.Where(x => x.Type == "issue").Select(x => x.Id).ToList();
        var saleIds = movements.Where(x => x.Type == "sale" && x.SaleId != null).Select(x => x.SaleId!.Value).Distinct().ToList();
        if (issueIds.Count > 0 || saleIds.Count > 0)
        {
            var late = await Scoped().Where(x => (x.OccurredAt < fromUtc || x.OccurredAt >= toUtc)
                    && ((x.Type == "issue-return" && x.SourceMovementId != null && issueIds.Contains(x.SourceMovementId.Value))
                        || (x.Type == "sale-void" && x.SaleId != null && saleIds.Contains(x.SaleId.Value))))
                .ToListAsync(ct);
            movements.AddRange(late);
        }

        var itemIds = movements.Select(x => x.CatalogItemId).Distinct().ToList();
        var jobIds = movements.Where(x => x.JobId != null).Select(x => x.JobId!.Value).Distinct().ToList();
        var allSaleIds = movements.Where(x => x.SaleId != null).Select(x => x.SaleId!.Value).Distinct().ToList();
        var items = await db.CatalogItems.AsNoTracking().Where(x => itemIds.Contains(x.Id))
            .Select(x => new { x.Id, x.Code, x.Name, x.Unit }).ToListAsync(ct);
        var jobs = await db.Jobs.AsNoTracking().Where(x => jobIds.Contains(x.Id))
            .Select(x => new { x.Id, x.JobNo, x.VehicleRegistration }).ToListAsync(ct);
        var sales = await db.Sales.AsNoTracking().Where(x => allSaleIds.Contains(x.Id))
            .Select(x => new { x.Id, x.ReceiptNo }).ToListAsync(ct);
        return new StockUsageData(
            movements,
            items.ToDictionary(x => x.Id, x => (x.Code, x.Name, x.Unit)),
            jobs.ToDictionary(x => x.Id, x => (x.JobNo, (string?)x.VehicleRegistration)),
            sales.ToDictionary(x => x.Id, x => x.ReceiptNo),
            truncated);
    }

    public Task<int> CountRetailDraftsAsync(string shardKey, int branchId, CancellationToken ct) =>
        db.Sales.CountAsync(s => s.LegacyShardKey == shardKey && s.LegacyBranchId == branchId
            && s.Status == Domain.Enums.SaleStatus.Draft, ct);

    public Task<IReadOnlyList<Job>> SearchJobsByVehicleOrPhoneAsync(
        string shardKey, int branchId, string normalizedTerm, int take, CancellationToken ct) =>
        // REPLACE ทั้งคอลัมน์ทำให้ใช้ index ไม่ได้ — ยอมรับได้เพราะกรองสาขาก่อน (index shard/branch) และจ๊อบต่อสาขาหลักพัน
        Scope(db.Jobs.AsNoTracking(), shardKey, branchId)
            .Where(j => j.VehicleRegistration.Replace(" ", "").Replace("-", "").Contains(normalizedTerm)
                || (j.CustomerPhone != null
                    && j.CustomerPhone.Replace(" ", "").Replace("-", "").Contains(normalizedTerm)))
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .ToListReadOnlyAsync(ct);

    public async Task<VehicleHistoryData> GetVehicleHistoryAsync(
        string shardKey, int branchId, long vehicleId, CancellationToken ct)
    {
        var jobs = await Scope(db.Jobs.AsNoTracking(), shardKey, branchId)
            .Where(j => j.VehicleId == vehicleId)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(ct);
        var jobIds = jobs.Select(j => j.Id).ToList();
        if (jobIds.Count == 0)
            return new VehicleHistoryData([], [], [], []);

        var quotations = await db.Quotations.AsNoTracking()
            .Include(q => q.Lines)
            .Where(q => jobIds.Contains(q.JobId) && q.Status != Domain.Enums.QuotationStatus.Superseded)
            .AsSplitQuery()
            .ToListAsync(ct);
        var receipts = await db.Receipts.AsNoTracking().Where(r => jobIds.Contains(r.JobId)).ToListAsync(ct);
        var handovers = await db.HandoverRecords.AsNoTracking().Where(h => jobIds.Contains(h.JobId)).ToListAsync(ct);

        return new VehicleHistoryData(jobs, quotations, receipts, handovers);
    }

    public async Task<ServiceDueData> GetServiceDueAsync(
        string shardKey, int branchId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var candidates = await db.HandoverRecords.AsNoTracking()
            .Include(h => h.Job)
            .Where(h => h.SubmittedAt != null && h.NextServiceDueOn != null
                && h.NextServiceDueOn >= from && h.NextServiceDueOn <= to
                && h.Job!.LegacyShardKey == shardKey && h.Job.BranchId == branchId)
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return new ServiceDueData([], [], []);

        var vehicleIds = candidates.Select(h => h.Job!.VehicleId).Distinct().ToList();
        var vehicleJobs = Scope(db.Jobs, shardKey, branchId).Where(j => vehicleIds.Contains(j.VehicleId));

        var visits = await vehicleJobs
            .Select(j => new VehicleVisitRef(j.VehicleId, j.Id, j.CreatedAt, j.Status))
            .ToListAsync(ct);
        var handovers = await db.HandoverRecords
            .Where(h => h.SubmittedAt != null && vehicleJobs.Select(j => j.Id).Contains(h.JobId))
            .Select(h => new VehicleHandoverRef(h.Job!.VehicleId, h.JobId, h.SubmittedAt!.Value))
            .ToListAsync(ct);

        return new ServiceDueData(candidates, visits, handovers);
    }

    private static IQueryable<Job> Scope(IQueryable<Job> jobs, string shardKey, int branchId) =>
        jobs.Where(j => j.LegacyShardKey == shardKey && j.BranchId == branchId);
}

internal static class QueryableExtensions
{
    public static async Task<IReadOnlyList<T>> ToListReadOnlyAsync<T>(this IQueryable<T> query, CancellationToken ct) =>
        await query.ToListAsync(ct);
}
