using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// รันคำสั่งลบของ S4 กับฐาน GarageSelfServe(_Dev) ที่ provision แล้ว — จับคอลัมน์/ตาราง shadow ที่หายหรือ FK ที่ลำดับผิด
/// ซึ่ง unit test จับไม่ได้ · [SAFE] ใช้สาขาที่ไม่มีอยู่จริง (int.MinValue) และ <b>rollback ทุกครั้ง</b> — ไม่มีแถวใดถูกลบ/แก้
/// · ตั้ง <c>GARAGEPRO_SELFSERVE_SQL_CONNECTION</c> แบบเดียวกับ SelfServeShardSqlTests (ห้ามชี้ฐาน Garage/GarageService จริง)
/// </summary>
public class WorkspacePurgeSqlTests
{
    private const int NoSuchBranch = int.MinValue;

    private static string Connection => Environment.GetEnvironmentVariable(SelfServeShardSqlFactAttribute.Variable)!;

    [SelfServeShardSqlFact]
    public async Task Shadow_purge_sql_runs_against_the_provisioned_schema()
    {
        await using var db = new SqlConnection(Connection);
        await db.OpenAsync();
        (await db.ExecuteScalarAsync<string>("SELECT DB_NAME()")).Should().StartWith("GarageSelfServe",
            "เทสต์นี้รันได้เฉพาะกับฐาน self-serve");

        await using var tx = (SqlTransaction)await db.BeginTransactionAsync();
        try
        {
            var args = new { BranchId = NoSuchBranch };
            await db.ExecuteAsync(WorkspacePurgeData.CreateShadowTempTablesSql, transaction: tx);
            await db.ExecuteAsync(WorkspacePurgeData.CollectShadowRowsSql, args, tx);
            (await db.QueryAsync<string>(WorkspacePurgeData.StaffPicturesSql, transaction: tx)).Should().BeEmpty();
            (await db.QueryAsync<string>(WorkspacePurgeData.CarImagesSql, transaction: tx)).Should().BeEmpty();
            foreach (var (table, sql) in WorkspacePurgeData.ShadowDeletes)
                (await db.ExecuteAsync(sql, args, tx)).Should().Be(0, $"{table}: สาขาที่ไม่มีอยู่ต้องไม่โดนแถวใด");
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    [SelfServeShardSqlFact]
    public async Task Service_purge_plan_runs_against_the_migrated_svc_tables()
    {
        // ไม่เปิด retry เพื่อใช้ transaction ตรงๆ ได้ (เฉพาะเทสต์ — โค้ดจริงไม่ห่อ transaction)
        await using var db = new ServiceDbContext(new DbContextOptionsBuilder<ServiceDbContext>()
            .UseSqlServer(Connection).Options);
        await using var tx = await db.Database.BeginTransactionAsync();
        try
        {
            var scope = new ServicePurgeScope("sp1", NoSuchBranch, [long.MinValue]);
            (await ServicePurgePlan.AttachmentPathsAsync(db, scope, default)).Should().BeEmpty();
            foreach (var step in ServicePurgePlan.Steps)
                (await step.Run(db, scope, default)).Should().Be(0, $"{step.Table}: สาขาที่ไม่มีอยู่ต้องไม่โดนแถวใด");
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }
}
