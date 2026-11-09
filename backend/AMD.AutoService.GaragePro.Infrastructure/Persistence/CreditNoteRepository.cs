using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class CreditNoteRepository(ServiceDbContext db) : ICreditNoteRepository
{
    private IQueryable<CreditNote> Notes => db.CreditNotes.Include(x => x.Lines).Include(x => x.Refunds).AsSplitQuery();

    public async Task<IReadOnlyList<CreditNote>> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        await Notes.Where(x => x.JobId == jobId).ToListAsync(ct);

    public Task<CreditNote?> GetAsync(Guid id, CancellationToken ct = default) =>
        Notes.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<CreditNote?> GetByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
        Notes.FirstOrDefaultAsync(x => x.RequestId == requestId, ct);

    public Task<bool> RefundRequestExistsAsync(Guid requestId, CancellationToken ct = default) =>
        db.CreditNoteRefunds.AnyAsync(x => x.RequestId == requestId, ct);

    public async Task<IReadOnlyList<CreditNote>> SearchAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await Notes.AsNoTracking()
            .Where(x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId
                        && x.IssuedAt >= fromUtc && x.IssuedAt < toUtc)
            .ToListAsync(ct);

    public async Task<string> NextDocumentNoAsync(
        string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default)
    {
        var year = nowLocal.Year;

        // ห้ามต่อ .SingleAsync()/.FirstAsync() — EF จะครอบ MERGE...OUTPUT เป็น subquery ซึ่ง compose ไม่ได้
        var rows = await db.Database.SqlQueryRaw<int>(
            """
            MERGE svc_CreditNoteNumberCounter WITH (HOLDLOCK) AS target
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

        return $"CN-{year % 100:D2}-{rows.Single():D4}";
    }

    public Task<BranchTaxProfile?> GetProfileAsync(string shardKey, int branchId, CancellationToken ct = default) =>
        db.BranchTaxProfiles.FirstOrDefaultAsync(x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId, ct);

    public async Task AddProfileAsync(BranchTaxProfile profile, CancellationToken ct = default) =>
        await db.BranchTaxProfiles.AddAsync(profile, ct);

    public async Task AddAsync(CreditNote note, CancellationToken ct = default) => await db.CreditNotes.AddAsync(note, ct);

    public async Task AddRefundAsync(CreditNoteRefund refund, CancellationToken ct = default)
    {
        // refund ที่เพิ่มเข้า note.Refunds ของ entity ที่ track อยู่แล้วถูก EF จับเองตอน DetectChanges — เพิ่มซ้ำได้ไม่เป็นไร
        if (db.Entry(refund).State == EntityState.Detached) await db.CreditNoteRefunds.AddAsync(refund, ct);
    }

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
