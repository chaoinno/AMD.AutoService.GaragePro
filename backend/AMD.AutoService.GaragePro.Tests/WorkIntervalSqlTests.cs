using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class WorkIntervalSqlFactAttribute : FactAttribute
{
    public WorkIntervalSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GARAGEPRO_WORKINTERVAL_SQL_CONNECTION")))
            Skip = "Set GARAGEPRO_WORKINTERVAL_SQL_CONNECTION to a migrated ServiceDb to run the isolated SQL integration test.";
    }
}

/// <summary>
/// สิ่งที่พิสูจน์ได้เฉพาะกับฐานข้อมูลจริงเท่านั้น — invariant "หนึ่งช่วงเปิดต่อช่าง" อยู่ที่
/// filtered unique index `UX_WorkInterval_OneOpenPerTech` ไม่ใช่ที่ service ดังนั้นเทสต์ที่ใช้ fake
/// repository พิสูจน์ข้อนี้ไม่ได้เลย (docs/09-technician-time-tracking.md §4)
///
/// ใช้ shard ปลอมเฉพาะการรันครั้งนี้แล้วลบทิ้งใน finally — ไม่แตะข้อมูลจริงของสาขาไหน
/// </summary>
public class WorkIntervalSqlTests
{
    [WorkIntervalSqlFact]
    public async Task Index_allows_a_whole_crew_on_one_car_but_never_two_open_intervals_for_one_technician()
    {
        var connection = Environment.GetEnvironmentVariable("GARAGEPRO_WORKINTERVAL_SQL_CONNECTION")!;
        var shardKey = $"wt-{Guid.NewGuid():N}"[..19];
        const int branchId = 1;

        ServiceDbContext NewDb() => new(new DbContextOptionsBuilder<ServiceDbContext>()
            .UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3)).Options);

        var job = NewJob(shardKey, branchId);
        var otherJob = NewJob(shardKey, branchId);

        try
        {
            await using (var db = NewDb())
            {
                db.AddRange(job, otherJob);
                await db.SaveChangesAsync();
            }

            // ช่าง 3 คนรุมคันเดียวกันพร้อมกันได้ — key ของ index เป็น "ช่าง" ไม่ใช่ "จ๊อบ" (docs/09 §5.3)
            await using (var db = NewDb())
            {
                db.WorkIntervals.AddRange(
                    OpenInterval(shardKey, branchId, job.Id, staffId: 101),
                    OpenInterval(shardKey, branchId, job.Id, staffId: 102),
                    OpenInterval(shardKey, branchId, job.Id, staffId: 103));
                await db.SaveChangesAsync();
            }

            // ช่างคนเดิมเปิดคาบที่สองไม่ได้ ไม่ว่าจะคันเดิมหรือคันใหม่ — ฐานข้อมูลต้องปฏิเสธเอง
            await using (var db = NewDb())
            {
                db.WorkIntervals.Add(OpenInterval(shardKey, branchId, otherJob.Id, staffId: 101));
                var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.Contains("UX_WorkInterval_OneOpenPerTech", conflict.InnerException?.Message ?? "");
            }

            // ปิดคาบเดิมแล้วเปิดใหม่ในคำขอเดียว — index ปล่อยผ่านเพราะเหลือช่วงเปิดเดียวตอน commit
            await using (var db = NewDb())
            {
                var open = await db.WorkIntervals.SingleAsync(x =>
                    x.LegacyShardKey == shardKey && x.TechnicianStaffId == 101 && x.EndedAt == null);
                open.EndedAt = DateTime.UtcNow;
                open.EndReason = WorkEndReason.SwitchedJob;
                db.WorkIntervals.Add(OpenInterval(shardKey, branchId, otherJob.Id, staffId: 101));
                await db.SaveChangesAsync();
            }

            await using (var verify = NewDb())
            {
                var mine = await verify.WorkIntervals
                    .Where(x => x.LegacyShardKey == shardKey && x.TechnicianStaffId == 101)
                    .ToListAsync();
                Assert.Equal(2, mine.Count);
                Assert.Single(mine.Where(x => x.EndedAt == null));
                Assert.Equal(otherJob.Id, mine.Single(x => x.EndedAt == null).JobId);

                // RequestId ซ้ำเป็น unique ระดับ global — กดรัวๆ ต้องไม่ได้หลายช่วง
                var duplicate = OpenInterval(shardKey, branchId, job.Id, staffId: 104);
                duplicate.RequestId = mine[0].RequestId;
                verify.WorkIntervals.Add(duplicate);
                await Assert.ThrowsAsync<DbUpdateException>(() => verify.SaveChangesAsync());
            }
        }
        finally
        {
            await using var cleanup = NewDb();
            await cleanup.WorkIntervals.Where(x => x.LegacyShardKey == shardKey).ExecuteDeleteAsync();
            await cleanup.Jobs.Where(x => x.LegacyShardKey == shardKey).ExecuteDeleteAsync();
        }
    }

    private static Job NewJob(string shardKey, int branchId) => new()
    {
        LegacyShardKey = shardKey,
        BranchId = branchId,
        CustomerId = 1,
        VehicleId = 1,
        JobNo = $"JB{Guid.NewGuid():N}"[..18],
        Status = JobStatus.InProgress,
        BranchName = "สาขาทดสอบชั่วคราว",
        CustomerName = "ลูกค้าทดสอบชั่วคราว",
        VehicleRegistration = "ทส-0001",
        JobTypeId = 9,
        JobTypeName = "รถในอู่"
    };

    private static WorkInterval OpenInterval(string shardKey, int branchId, Guid jobId, long staffId) => new()
    {
        LegacyShardKey = shardKey,
        LegacyBranchId = branchId,
        JobId = jobId,
        TechnicianStaffId = staffId,
        TechnicianName = $"ช่างทดสอบ {staffId}",
        Kind = WorkIntervalKind.Work,
        StartedAt = DateTime.UtcNow,
        RequestId = Guid.NewGuid(),
        CreatedByUserId = 0
    };
}
