using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;

namespace AMD.AutoService.GaragePro.Application.Internal;

/// <summary>
/// แหล่งข้อมูลอู่ sp1 · มีเฉพาะ deployment ที่ตั้งฐาน GarageSelfServe (ไม่ตั้ง = ส่งแค่สาขา GaragePro)
/// · <see cref="Admin"/>.ListAsync ตัวเดียวกับ <c>spadmin list</c>
/// </summary>
public sealed record SelfServeSiteSources(
    SelfServeAdminService Admin,
    IPackagePaymentRepository Payments,
    SelfServeOptions Options);

/// <summary>
/// <c>GET /api/v1/internal/sites</c> (docs/17 §8.6) · รวมทุก site ของ ServicePro ไว้ในรายการเดียวให้หน้า /branches ของ GaragePro Admin
/// · selfserve: sp_Workspace + Branch ของ sp1 (ListAsync อ่าน Branch ทั้ง shard ครั้งเดียว) · สรุปรายการแจ้งชำระ (GROUP BY ครั้งเดียว) ·
///   จ๊อบของ sp1 (GROUP BY ครั้งเดียว)
/// · garagepro: Branch กลุ่ม Service ของ db2 (Dapper · READUNCOMMITTED) · จ๊อบของ db2 (GROUP BY ครั้งเดียว)
/// · รวม 6 query ไม่ว่าจะมีกี่อู่ (ไม่ยิงทีละสาขา) · อ่านอย่างเดียวทั้งหมด
/// </summary>
public sealed class InternalSitesService(
    IInternalSiteReader reader,
    TimeProvider clock,
    string garageProShardKey,
    SelfServeSiteSources? selfServe)
{
    public async Task<InternalSitesDto> GetAsync(CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(garageProShardKey);

        var now = clock.GetUtcNow().UtcDateTime;
        var since = now.AddDays(-InternalSiteMapper.JobWindowDays);
        var sites = new List<InternalSiteDto>();

        if (selfServe is not null)
        {
            var shard = selfServe.Options.ShardKey;
            var rows = await selfServe.Admin.ListAsync(null, null, ct);
            if (rows.Count > 0)
            {
                var payments = await selfServe.Payments.SummarizeByWorkspaceAsync(ct);
                var jobs = await reader.ReadJobActivityAsync(shard, since, ct);
                foreach (var row in rows.OrderBy(r => r.WorkspaceNo))
                {
                    var site = InternalSiteMapper.SelfServe(row, shard, payments.GetValueOrDefault(row.Id),
                        row.BranchId is { } b ? jobs.GetValueOrDefault(b) : null, now, selfServe.Options);
                    if (site is not null) sites.Add(site);
                }
            }
        }

        var branches = await reader.ReadGarageProBranchesAsync(garageProShardKey, ct);
        if (branches.Count > 0)
        {
            var jobs = await reader.ReadJobActivityAsync(garageProShardKey, since, ct);
            sites.AddRange(branches.OrderBy(b => b.Id)
                .Select(b => InternalSiteMapper.GaragePro(b, garageProShardKey, jobs.GetValueOrDefault(b.Id), now)));
        }

        return new InternalSitesDto(now, sites);
    }
}
