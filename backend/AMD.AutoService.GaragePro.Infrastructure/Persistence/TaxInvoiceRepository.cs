using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class TaxInvoiceRepository(ServiceDbContext db) : ITaxInvoiceRepository
{
    public Task<TaxInvoice?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        db.TaxInvoices.Include(x => x.Lines).FirstOrDefaultAsync(x => x.JobId == jobId, ct);

    public async Task<string> NextDocumentNoAsync(
        string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default)
    {
        var year = nowLocal.Year;

        // ห้ามต่อ .SingleAsync()/.FirstAsync() — EF จะครอบ MERGE...OUTPUT เป็น subquery ซึ่ง compose ไม่ได้
        var rows = await db.Database.SqlQueryRaw<int>(
            """
            MERGE svc_TaxInvoiceNumberCounter WITH (HOLDLOCK) AS target
            USING (SELECT {0} AS LegacyShardKey, {1} AS LegacyBranchId, {2} AS Year) AS src
              ON target.LegacyShardKey = src.LegacyShardKey
             AND target.LegacyBranchId = src.LegacyBranchId
             AND target.Year = src.Year
            WHEN MATCHED THEN UPDATE SET LastSequence = target.LastSequence + 1
            WHEN NOT MATCHED THEN INSERT (LegacyShardKey, LegacyBranchId, Year, LastSequence)
                VALUES (src.LegacyShardKey, src.LegacyBranchId, src.Year, 1)
            OUTPUT inserted.LastSequence AS Value;
            """,
            shardKey, branchId, year)
            .ToListAsync(ct);

        return $"IV-{year % 100:D2}-{rows.Single():D4}";
    }

    public async Task AddAsync(TaxInvoice invoice, CancellationToken ct = default) =>
        await db.TaxInvoices.AddAsync(invoice, ct);

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
