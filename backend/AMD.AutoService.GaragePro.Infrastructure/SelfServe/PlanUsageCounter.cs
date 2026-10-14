using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// นับการใช้งานจริงของลิมิตแพ็กเกจ — กติกาการนับอธิบายไว้ที่ <see cref="IPlanUsageCounter"/>
/// · svc_* (สินค้า/จ๊อบ) นับด้วย EF บนฐานของ shard ที่ถาม — สร้าง context ของตัวเองจาก
///   <see cref="IServiceDbConnectionResolver"/> แทนการใช้ ServiceDbContext ของ scope เพราะ scope อาจเป็นของ shard อื่น
///   (เช่น spadmin/hosted job ถามถึงอู่ sp1 จาก scope ระบบ) แล้วจะนับผิดฐานโดยไม่มีอะไรฟ้อง
/// · พนักงาน/ลูกค้า (ตารางรูปร่างเหมือน Garage) นับด้วย Dapper บน <c>LegacyShards:ConnectionStrings[shard]</c>
///   ใช้ READUNCOMMITTED แบบเดียวกับคลาส legacy ทุกตัว (ถ้าวันหนึ่งเรียกกับ shard db2 ก็ไม่ไปถือ lock ของ Garage จริง)
/// </summary>
public sealed class PlanUsageCounter(
    IServiceDbConnectionResolver connections,
    IOptions<LegacyShardOptions> legacyOptions) : IPlanUsageCounter
{
    private readonly LegacyShardOptions _legacy = legacyOptions.Value;

    internal const string ActiveStaffSql = """
        SELECT COUNT(*) FROM Staff WITH (READUNCOMMITTED)
        WHERE BranchId = @BranchId AND ISNULL(Status, 1) <> 0
        """;

    // กติกาการมองเห็นเดียวกับรายการลูกค้า (BranchDataScope) — Customer ไม่มีคอลัมน์สาขา
    // นับรวมลูกค้าที่ถูกลบแบบ soft แล้ว (ดูเหตุผลที่ IPlanUsageCounter)
    internal static readonly string VisibleCustomersSql =
        $"SELECT COUNT(DISTINCT scoped.Id) FROM ({BranchDataScope.CustomerIds}) scoped";

    public async Task<int> CountAsync(string shardKey, int branchId, PlanLimitKind kind, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardKey);

        return kind switch
        {
            PlanLimitKind.CatalogItems => await CountServiceRowsAsync(shardKey, db =>
                db.CatalogItems.CountAsync(x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId, ct)),
            PlanLimitKind.Jobs => await CountServiceRowsAsync(shardKey, db =>
                db.Jobs.CountAsync(x => x.LegacyShardKey == shardKey && x.BranchId == branchId, ct)),
            PlanLimitKind.Staff => await CountLegacyAsync(shardKey, ActiveStaffSql, branchId, ct),
            PlanLimitKind.Customers => await CountLegacyAsync(shardKey, VisibleCustomersSql, branchId, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "ไม่รู้จักชนิดลิมิต")
        };
    }

    private async Task<int> CountServiceRowsAsync(string shardKey, Func<ServiceDbContext, Task<int>> count)
    {
        var options = new DbContextOptionsBuilder<ServiceDbContext>()
            .UseSqlServer(connections.Resolve(shardKey), sql => sql.EnableRetryOnFailure(3))
            .Options;

        await using var db = new ServiceDbContext(options);
        return await count(db);
    }

    private async Task<int> CountLegacyAsync(string shardKey, string sql, int branchId, CancellationToken ct)
    {
        if (!_legacy.ConnectionStrings.TryGetValue(shardKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{shardKey}' ตั้งค่าใน {LegacyShardOptions.SectionName}:ConnectionStrings:{shardKey}");

        await using var db = new SqlConnection(cs);
        return await db.ExecuteScalarAsync<int>(
            new CommandDefinition(sql, new { BranchId = branchId }, cancellationToken: ct));
    }
}
