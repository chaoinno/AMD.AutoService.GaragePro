using AMD.AutoService.GaragePro.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

/// <summary>
/// สร้างเลขใบเสร็จ RC-{yy}-{ลำดับ:D4} แบบ concurrency-safe ด้วย MERGE...HOLDLOCK
/// เหมือน JobNumberGenerator ทุกประการ — ลำดับรีเซ็ตทุกปีต่อ (ชาร์ด, สาขา)
/// </summary>
public sealed class ReceiptNumberGenerator(ServiceDbContext db) : IReceiptNumberGenerator
{
    public async Task<string> NextAsync(
        string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default)
    {
        var year = nowLocal.Year;

        // ห้ามต่อ .SingleAsync()/.FirstAsync() ตรงนี้ — EF จะพยายาม compose (ครอบ subquery ด้วย TOP)
        // ทับ MERGE...OUTPUT ซึ่ง SQL Server ไม่ยอมให้ compose แบบนั้น (non-composable SQL)
        var rows = await db.Database.SqlQueryRaw<int>(
            """
            MERGE svc_ReceiptNumberCounter WITH (HOLDLOCK) AS target
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

        var sequence = rows.Single();

        return $"RC-{year % 100:D2}-{sequence:D4}";
    }
}
