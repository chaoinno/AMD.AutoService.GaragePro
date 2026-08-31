using AMD.AutoService.GaragePro.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

/// <summary>
/// สร้างเลขจ๊อบ JB{yyMMdd}{BranchId:D4}{ลำดับ:D3} แบบ concurrency-safe ทั้งหมดในฐาน GarageService
/// ใช้ MERGE...HOLDLOCK กันสอง request แย่งกันได้เลขซ้ำ — แทน sp_getapplock เดิมที่ผูกกับ legacy DB
/// (ไม่ต้องประสาน 2 ฐานข้อมูลอีกต่อไป เพราะจ๊อบไม่เขียนลง legacy แล้ว)
/// ลำดับรีเซ็ตทุกวันต่อ (ชาร์ด, สาขา) — เลขที่ "เสีย" เมื่อสร้างจ๊อบไม่สำเร็จถือเป็น gap ที่ยอมรับได้
/// </summary>
public sealed class JobNumberGenerator(ServiceDbContext db) : IJobNumberGenerator
{
    public async Task<string> NextAsync(
        string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default)
    {
        var counterDate = DateOnly.FromDateTime(nowLocal);

        // ห้ามต่อ .SingleAsync()/.FirstAsync() ตรงนี้ — EF จะพยายาม compose (ครอบ subquery ด้วย TOP)
        // ทับ MERGE...OUTPUT ซึ่ง SQL Server ไม่ยอมให้ compose แบบนั้น (non-composable SQL)
        // ต้อง .ToListAsync() ให้ EF รันสตริง SQL ตรงๆ ก่อน แล้วค่อยเลือกแถวเดียวฝั่ง client
        var rows = await db.Database.SqlQueryRaw<int>(
            """
            MERGE svc_JobNumberCounter WITH (HOLDLOCK) AS target
            USING (SELECT {0} AS LegacyShardKey, {1} AS BranchId, {2} AS CounterDate) AS src
              ON target.LegacyShardKey = src.LegacyShardKey
             AND target.BranchId = src.BranchId
             AND target.CounterDate = src.CounterDate
            WHEN MATCHED THEN UPDATE SET LastSequence = target.LastSequence + 1
            WHEN NOT MATCHED THEN INSERT (LegacyShardKey, BranchId, CounterDate, LastSequence)
                VALUES (src.LegacyShardKey, src.BranchId, src.CounterDate, 1)
            OUTPUT inserted.LastSequence AS Value;
            """,
            shardKey, branchId, counterDate)
            .ToListAsync(ct);

        var sequence = rows.Single();

        return $"JB{counterDate:yyMMdd}{branchId:D4}{sequence:D3}";
    }
}
