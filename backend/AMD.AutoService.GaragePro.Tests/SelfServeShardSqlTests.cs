using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class SelfServeShardSqlFactAttribute : FactAttribute
{
    public const string Variable = "GARAGEPRO_SELFSERVE_SQL_CONNECTION";

    public SelfServeShardSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Set {Variable} to a provisioned GarageSelfServe(_Dev) database (deploy/selfserve) to run the sp1 shadow-schema tests. Read-only.";
    }
}

/// <summary>
/// รัน SQL จริงของคลาส legacy กับฐาน sp1 ที่ provision แล้ว — จับ "ตาราง/คอลัมน์ shadow หาย" หรือ synonym พัง
/// ซึ่ง unit test จับไม่ได้ (คิวรีเหล่านี้ไม่เคยรันกับฐานอื่นนอกจาก Garage จริงมาก่อน)
/// · อ่านอย่างเดียว ไม่เขียนแถวใดๆ · branch 0 = ไม่มีแถวจริง แต่ SQL ยังต้อง compile/bind ทุกคอลัมน์ผ่าน
/// </summary>
public class SelfServeShardSqlTests
{
    private const string Shard = "sp1";

    private static string Connection => Environment.GetEnvironmentVariable(SelfServeShardSqlFactAttribute.Variable)!;

    private static IOptions<LegacyShardOptions> Options(string defaultShard = "db2") => Microsoft.Extensions.Options.Options.Create(
        new LegacyShardOptions { DefaultShard = defaultShard, ConnectionStrings = { [Shard] = Connection } });

    [SelfServeShardSqlFact]
    public async Task Accessible_branch_queries_run_against_the_shadow_Branch_table()
    {
        await using var db = new SqlConnection(Connection);

        foreach (var isAdministrator in new[] { false, true })
        {
            var rows = await db.QueryAsync<LegacyBranchSummaryDto>(
                LegacyUserReader.BuildAccessibleBranchesSql(isAdministrator), new { branchId = 0 });
            rows.Should().NotBeNull();
        }
    }

    [SelfServeShardSqlFact]
    public async Task Staff_reference_data_reads_the_copied_reference_rows()
    {
        // GetReferenceDataAsync ไม่แตะรูปพนักงาน — ไม่ต้องมี image storage
        var repository = new StaffRepository(Options(), imageStorage: null!);

        var data = await repository.GetReferenceDataAsync(new LegacyRequestScope(Shard, 0, 0), isAdministrator: false);

        data.Genders.Select(g => g.Id).Should().Contain(3, "Gender(3, 'ไม่ระบุ') ต้องถูก seed ใน sp1 (05-copy-reference-rows.sql)");
        data.Positions.Should().NotBeEmpty();
        data.Departments.Should().NotBeEmpty();
        data.SkillLevels.Should().NotBeEmpty();
    }

    [SelfServeShardSqlFact]
    public async Task Vehicle_reference_data_reads_Garage_through_the_synonyms()
    {
        // ปกติคิวรีนี้อ่านจาก DefaultShard (db2) — ที่นี่บังคับให้วิ่งบน sp1 เพื่อพิสูจน์ synonym 15 ตัว
        var repository = new CustomerVehicleRepository(Options(defaultShard: Shard), imageStorage: null!);

        var data = await repository.GetVehicleReferenceDataAsync();

        data.Brands.Should().NotBeEmpty();
        data.Years.Should().NotBeEmpty();
        data.PrimaryColors.Should().NotBeEmpty();
    }

    [SelfServeShardSqlFact]
    public async Task Customer_scope_plan_counters_and_package_reader_bind_every_shadow_column()
    {
        await using var db = new SqlConnection(Connection);

        // BranchDataScope แตะ Customer/Car/CarCustomer/[User]/Staff/PJCarPickUp (คอลัมน์ที่ตัดมาแบบย่อ)
        (await db.ExecuteScalarAsync<int>(PlanUsageCounter.VisibleCustomersSql, new { BranchId = 0 })).Should().Be(0);
        (await db.ExecuteScalarAsync<int>(
            $"SELECT COUNT(DISTINCT scoped.Id) FROM ({BranchDataScope.VehicleIds}) scoped", new { BranchId = 0 })).Should().Be(0);
        (await db.ExecuteScalarAsync<int>(PlanUsageCounter.ActiveStaffSql, new { BranchId = 0 })).Should().Be(0);
        (await db.QueryAsync(WorkspacePlanReader.BranchPackageSql, new { BranchId = 0 })).Should().BeEmpty();
    }
}
