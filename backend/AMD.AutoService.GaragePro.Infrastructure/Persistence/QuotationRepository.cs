using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class QuotationRepository(ServiceDbContext db) : IQuotationRepository
{
    public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Quotations.FirstOrDefaultAsync(q => q.Id == id, ct);

    public Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default) =>
        db.Quotations
          .Include(q => q.Lines)
          .Include(q => q.Approval)
          .FirstOrDefaultAsync(q => q.Id == id, ct);

    public Task<Quotation?> GetLatestForJobAsync(string shardKey, long jobId, CancellationToken ct = default) =>
        db.Quotations
          .Include(q => q.Lines)
          .Where(q => q.LegacyShardKey == shardKey && q.LegacyJobId == jobId)
          .OrderByDescending(q => q.Version)
          .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<Quotation>> GetQueueAsync(
        string shardKey, int branchId, string? statusFilter, long? jobId = null, CancellationToken ct = default)
    {
        var query = db.Quotations
            .Where(q => q.LegacyShardKey == shardKey && q.LegacyBranchId == branchId);

        if (jobId is not null)
            query = query.Where(q => q.LegacyJobId == jobId);

        // ตัวกรองตรงกับ chip บนหน้าคิวใน design: ทั้งหมด / รอเสนอราคา / รออนุมัติ / ขอแก้ไข
        query = statusFilter switch
        {
            "todo" => query.Where(q => q.Status == QuotationStatus.Draft),
            "wait" => query.Where(q => q.Status == QuotationStatus.Sent),
            "rev"  => query.Where(q => q.Status == QuotationStatus.Partial
                                    || q.Status == QuotationStatus.Rejected),
            "done" => query.Where(q => q.Status == QuotationStatus.Approved),
            _ => query.Where(q => q.Status != QuotationStatus.Superseded)
        };

        return await query
            .OrderBy(q => q.Status == QuotationStatus.Draft ? 0 : 1)
            .ThenBy(q => q.SentAt ?? q.CreatedAt)
            .Take(500)
            .ToListAsync(ct);
    }

    public async Task<int> GetNextVersionAsync(string shardKey, long jobId, CancellationToken ct = default)
    {
        var max = await db.Quotations
            .Where(q => q.LegacyShardKey == shardKey && q.LegacyJobId == jobId)
            .Select(q => (int?)q.Version)
            .MaxAsync(ct);

        return (max ?? 0) + 1;
    }

    public async Task AddAsync(Quotation quotation, CancellationToken ct = default) =>
        await db.Quotations.AddAsync(quotation, ct);

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public sealed class CatalogRepository(ServiceDbContext db) : ICatalogRepository
{
    public async Task<IReadOnlyList<CatalogItem>> SearchAsync(
        string shardKey, int branchId, string? keyword, CancellationToken ct = default)
    {
        var query = db.CatalogItems
            .Where(c => c.LegacyShardKey == shardKey && c.LegacyBranchId == branchId && c.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            query = query.Where(c =>
                EF.Functions.Like(c.Code, $"%{k}%") ||
                EF.Functions.Like(c.Name, $"%{k}%") ||
                (c.Compatibility != null && EF.Functions.Like(c.Compatibility, $"%{k}%")));
        }

        return await query.OrderBy(c => c.Type).ThenBy(c => c.Name).Take(100).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<CatalogItem>> GetByCodesAsync(
        string shardKey, int branchId, IEnumerable<string> codes, CancellationToken ct = default)
    {
        var list = codes.ToArray();

        return await db.CatalogItems
            .Where(c => c.LegacyShardKey == shardKey
                     && c.LegacyBranchId == branchId
                     && list.Contains(c.Code))
            .ToListAsync(ct);
    }
}
