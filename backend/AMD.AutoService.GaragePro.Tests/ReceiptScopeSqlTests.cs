using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class ReceiptScopeSqlFactAttribute : FactAttribute
{
    public ReceiptScopeSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GARAGEPRO_RECEIPT_SQL_CONNECTION")))
            Skip = "Set GARAGEPRO_RECEIPT_SQL_CONNECTION to a ServiceDb migrated past AddReceiptScope to run the isolated SQL integration test.";
    }
}

/// <summary>
/// สิ่งที่พิสูจน์ได้เฉพาะกับฐานข้อมูลจริง — ขอบเขต unique ของเลขใบเสร็จอยู่ที่ index
/// `UX_svc_Receipt_Scope_DocumentNo` (ชาร์ด, สาขา, DocumentNo) ไม่ใช่ที่ service: สองสาขาต้องออก RC-26-0001 ได้ทั้งคู่
/// (เดิม IX_svc_Receipt_DocumentNo เป็น unique ทั้งตาราง ทำให้สาขาที่สองออกใบแรกไม่ได้) แต่สาขาเดียวกันต้องไม่ได้เลขซ้ำ
///
/// ใช้ shard ปลอมเฉพาะการรันครั้งนี้แล้วลบทิ้งใน finally — ไม่แตะข้อมูลจริงของสาขาไหน (pattern เดียวกับ WorkIntervalSqlTests)
/// </summary>
public class ReceiptScopeSqlTests
{
    [ReceiptScopeSqlFact]
    public async Task Index_allows_the_same_receipt_number_in_two_branches_but_not_twice_in_one_branch()
    {
        var connection = Environment.GetEnvironmentVariable("GARAGEPRO_RECEIPT_SQL_CONNECTION")!;
        var shardKey = $"rc-{Guid.NewGuid():N}"[..20];
        const int firstBranch = 1;
        const int secondBranch = 2;
        const string documentNo = "RC-26-0001";

        ServiceDbContext NewDb() => new(new DbContextOptionsBuilder<ServiceDbContext>()
            .UseSqlServer(connection, sql => sql.EnableRetryOnFailure(3)).Options);

        var firstBranchJob = NewJob(shardKey, firstBranch);
        var secondBranchJob = NewJob(shardKey, secondBranch);
        var anotherFirstBranchJob = NewJob(shardKey, firstBranch);

        try
        {
            await using (var db = NewDb())
            {
                db.AddRange(firstBranchJob, secondBranchJob, anotherFirstBranchJob);
                await db.SaveChangesAsync();
            }

            // สองสาขาออกใบแรกของปีได้เลขเดียวกัน — ต้องผ่านทั้งคู่ (นี่คือเคสที่เคยล้มด้วย SqlException 2601)
            await using (var db = NewDb())
            {
                db.Receipts.AddRange(
                    NewReceipt(shardKey, firstBranch, firstBranchJob.Id, documentNo),
                    NewReceipt(shardKey, secondBranch, secondBranchJob.Id, documentNo));
                await db.SaveChangesAsync();
            }

            // สาขาเดิมออกเลขเดิมซ้ำบนจ๊อบอื่น — ฐานข้อมูลต้องปฏิเสธเองด้วย index ขอบเขตใหม่ (ไม่ใช่ IX_svc_Receipt_JobId
            // เพราะใช้จ๊อบคนละตัว)
            await using (var db = NewDb())
            {
                db.Receipts.Add(NewReceipt(shardKey, firstBranch, anotherFirstBranchJob.Id, documentNo));
                var conflict = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
                Assert.Contains("UX_svc_Receipt_Scope_DocumentNo", conflict.InnerException?.Message ?? "");
            }

            await using (var verify = NewDb())
            {
                var issued = await verify.Receipts.AsNoTracking()
                    .Where(r => r.LegacyShardKey == shardKey && r.DocumentNo == documentNo)
                    .OrderBy(r => r.LegacyBranchId)
                    .ToListAsync();
                Assert.Equal([firstBranch, secondBranch], issued.Select(r => r.LegacyBranchId).ToArray());
            }
        }
        finally
        {
            await using var cleanup = NewDb();
            await cleanup.Receipts.Where(x => x.LegacyShardKey == shardKey).ExecuteDeleteAsync();
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
        Status = JobStatus.Ready,
        BranchName = "สาขาทดสอบชั่วคราว",
        CustomerName = "ลูกค้าทดสอบชั่วคราว",
        VehicleRegistration = "ทส-0001",
        JobTypeId = 9,
        JobTypeName = "รถในอู่"
    };

    private static Receipt NewReceipt(string shardKey, int branchId, Guid jobId, string documentNo) => new()
    {
        JobId = jobId,
        LegacyShardKey = shardKey,
        LegacyBranchId = branchId,
        DocumentNo = documentNo,
        NetAmount = 900m,
        VatAmount = 63m,
        TotalAmount = 963m,
        IssuedByUserId = 0,
        IssuedByName = "เทสต์",
        IssuedAt = DateTime.UtcNow
    };
}
