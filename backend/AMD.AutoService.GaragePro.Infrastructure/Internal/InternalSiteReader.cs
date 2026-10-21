using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Internal;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Internal;

/// <summary>
/// ข้อมูลของ API ภายใน (docs/17 §8.6) ที่อยู่นอก SelfServeDbContext · อ่านอย่างเดียวทั้งหมด
/// · Branch กลุ่ม Service อ่านจาก Garage เดิมด้วย Dapper ตามกฎ legacy: เลือกคอลัมน์ + <c>WITH (READUNCOMMITTED)</c> ·
///   ไม่มี LOB (Name/PhoneNumber1 เป็น nvarchar สั้น) · วันที่ในตารางเป็นเวลาไทย → UTC ที่นี่ที่เดียว
/// · svc_Job: ServiceDbContext เลือกฐานตาม ICurrentUser.ShardKey จึงเปิด scope ใหม่ในนาม shard ที่ต้องการ
///   (<see cref="IShardScopeFactory"/>) ไม่ใช้ context ของคำขอ เพราะคำขอภายในไม่มี JWT (CurrentUser ตกเป็น db2 เสมอ)
///   และถ้ามีคนส่ง token ของ sp1 มาด้วย context ของคำขอจะชี้ผิดฐาน
/// </summary>
public sealed class InternalSiteReader(IOptions<LegacyShardOptions> legacyOptions, IShardScopeFactory scopes)
    : IInternalSiteReader
{
    private readonly LegacyShardOptions _legacy = legacyOptions.Value;

    /// <summary>[BIZ] กลุ่ม Service = BranchGroupId 7 (ค่าเดียวกับ LegacyUserReader ที่ใช้กั้น login)</summary>
    internal static readonly string GarageProBranchesSql = $"""
        SELECT b.Id, b.Name, b.PhoneNumber1 AS Phone, ISNULL(b.Status, 0) AS Status,
               b.StartPackageDate, b.EndPackageDate, b.CreatedDate
        FROM Branch b WITH (READUNCOMMITTED)
        WHERE b.BranchGroupId = {LegacyUserReader.ServiceBranchGroupId}
        ORDER BY b.Id
        """;

    public async Task<IReadOnlyList<GarageProBranchFacts>> ReadGarageProBranchesAsync(string shardKey,
        CancellationToken ct = default)
    {
        if (!_legacy.ConnectionStrings.TryGetValue(shardKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{shardKey}' ตั้งค่า {LegacyShardOptions.SectionName}:ConnectionStrings:{shardKey}");

        await using var connection = new SqlConnection(cs);
        var rows = await connection.QueryAsync<BranchRow>(new CommandDefinition(GarageProBranchesSql, cancellationToken: ct));
        return rows.Select(r => new GarageProBranchFacts(r.Id, r.Name ?? string.Empty, r.Phone, r.Status,
                BranchPackageDates.ToUtc(r.StartPackageDate), BranchPackageDates.ToUtc(r.EndPackageDate),
                BranchPackageDates.ToUtc(r.CreatedDate)))
            .ToList();
    }

    public Task<IReadOnlyDictionary<int, SiteJobActivity>> ReadJobActivityAsync(string shardKey, DateTime sinceUtc,
        CancellationToken ct = default) =>
        scopes.RunAsAsync<IReadOnlyDictionary<int, SiteJobActivity>>(
            new ScopedCurrentUser(shardKey, 0, 0, "internal-sites"),
            async (sp, token) =>
            {
                var db = sp.GetRequiredService<ServiceDbContext>();
                // ผลของ MAX อาจไม่ผ่าน converter ของคอลัมน์ (Kind=Unspecified แล้ว JSON ไม่มี Z) · ระบุ Utc เองให้แน่
                return (await JobActivityQuery(db, shardKey, sinceUtc).ToListAsync(token))
                    .Select(a => a with { LastJobAt = AsUtc(a.LastJobAt) })
                    .ToDictionary(a => a.BranchId);
            }, ct);

    /// <summary>
    /// GROUP BY BranchId ครั้งเดียวต่อ shard (index (LegacyShardKey, BranchId, …) ของ svc_Job) · นับช่วงด้วยเงื่อนไขใน COUNT
    /// แทน WHERE เพื่อให้ <see cref="SiteJobActivity.LastJobAt"/> เป็นจ๊อบล่าสุดตลอดกาล (สาขาที่เงียบเกิน 30 วันยังเห็นวันสุดท้าย)
    /// </summary>
    internal static IQueryable<SiteJobActivity> JobActivityQuery(ServiceDbContext db, string shardKey, DateTime sinceUtc) =>
        db.Jobs.AsNoTracking()
            .Where(j => j.LegacyShardKey == shardKey)
            .GroupBy(j => j.BranchId)
            .Select(g => new SiteJobActivity(
                g.Key,
                g.Count(j => j.CreatedAt >= sinceUtc),
                g.Max(j => (DateTime?)j.CreatedAt)));

    private static DateTime? AsUtc(DateTime? value) =>
        value is { } v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private sealed class BranchRow
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public string? Phone { get; set; }
        public int Status { get; set; }
        public DateTime? StartPackageDate { get; set; }
        public DateTime? EndPackageDate { get; set; }
        public DateTime? CreatedDate { get; set; }
    }
}
