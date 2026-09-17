using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class QuotationTemplateRepository(ServiceDbContext db) : IQuotationTemplateRepository
{
    public async Task<IReadOnlyList<QuotationTemplate>> SearchAsync(
        string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default)
    {
        var q = db.QuotationTemplates
            .Include(t => t.Lines)
            .Where(t => t.LegacyShardKey == shardKey && t.LegacyBranchId == branchId);

        if (!includeInactive)
            q = q.Where(t => t.IsActive);

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(t => EF.Functions.Like(t.Code, $"%{k}%") || EF.Functions.Like(t.Name, $"%{k}%"));
        }

        return await q.OrderBy(t => t.Name).Take(500).ToListAsync(ct);
    }

    public Task<QuotationTemplate?> GetWithLinesAsync(
        string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
        db.QuotationTemplates
            .Include(t => t.Lines)
            .FirstOrDefaultAsync(t => t.LegacyShardKey == shardKey && t.LegacyBranchId == branchId && t.Id == id, ct);

    public Task<bool> CodeExistsAsync(
        string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default) =>
        db.QuotationTemplates.AnyAsync(t =>
            t.LegacyShardKey == shardKey && t.LegacyBranchId == branchId &&
            t.Code.ToUpper() == code.ToUpper() && (excludingId == null || t.Id != excludingId), ct);

    public async Task AddAsync(QuotationTemplate template, CancellationToken ct = default) =>
        await db.QuotationTemplates.AddAsync(template, ct);

    public void RemoveLines(IEnumerable<QuotationTemplateLine> lines) =>
        db.QuotationTemplateLines.RemoveRange(lines);

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

    private static readonly string[] KnownConstraints = ["UX_svc_QuotationTemplate_Code"];
}
