using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// นโยบายแพ็กเกจจริง — ลงทะเบียนแทน UnlimitedWorkspacePolicy เฉพาะเมื่อตั้ง SelfServe:ConnectionString
/// · [BIZ] shard ≠ sp1 → <see cref="WorkspacePlan.Legacy"/> ทันที ไม่แตะฐานข้อมูลใดๆ (ลูกค้าเดิมต้องไม่ช้าลง/ไม่ล้มเพราะ sp1)
/// · sp1 → อ่าน Branch + sp_Workspace (cache 60 วิ ต่อ shard:สาขา) แล้วตัดสินด้วย <see cref="PlanPolicy.Evaluate"/>
///   ทุกครั้งด้วยเวลาปัจจุบัน — cache เก็บข้อมูลดิบ ไม่ใช่ผลที่ตัดสินแล้ว จึงเปลี่ยนเป็นอ่านอย่างเดียวตรงเวลาหมดอายุพอดี
///   (การแก้วันจาก spadmin มีผลภายใน ≤60 วิ เท่ากับ cache ของ SessionRevocationMiddleware)
/// </summary>
public sealed class WorkspacePolicy(
    IWorkspacePlanReader reader,
    IPlanUsageCounter usage,
    IMemoryCache cache,
    IOptions<SelfServeOptions> options,
    TimeProvider clock) : IWorkspacePolicy
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    private readonly SelfServeOptions _options = options.Value;

    public async Task<WorkspacePlan> GetAsync(string shardKey, int branchId, CancellationToken ct = default)
    {
        if (!ServiceDbConnectionResolver.IsSelfServeShard(shardKey, _options))
            return WorkspacePlan.Legacy;

        var inputs = await cache.GetOrCreateAsync(CacheKey(_options, branchId), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            return reader.ReadAsync(_options.ShardKey, branchId, ct);
        });

        return PlanPolicy.Evaluate(clock.GetUtcNow().UtcDateTime, inputs?.Branch, inputs?.Workspace, _options);
    }

    public async Task<ApiError?> CheckLimitAsync(string shardKey, int branchId, PlanLimitKind kind,
        CancellationToken ct = default)
    {
        // legacy ไม่จำกัดและต้องไม่ถูกนับ (ไม่แตะฐาน) — ตัดก่อนเรียก GetAsync ให้ชัดว่าไม่มี I/O
        if (!ServiceDbConnectionResolver.IsSelfServeShard(shardKey, _options))
            return null;

        var plan = await GetAsync(shardKey, branchId, ct);
        if (plan.Limits.For(kind) is not { } max)
            return null;

        var used = await usage.CountAsync(_options.ShardKey, branchId, kind, ct);
        return used >= max ? PlanErrors.LimitReached(kind, max) : null;
    }

    /// <summary>ใช้ shard ตามที่ตั้งค่า (ไม่ใช่ค่าที่ส่งมา) เป็น key — "SP1"/"sp1" ต้องได้ cache เดียวกัน</summary>
    private static string CacheKey(SelfServeOptions options, int branchId) =>
        $"workspace-plan:{options.ShardKey}:{branchId}";

    /// <summary>ล้าง cache ของสาขาหลังแก้แพ็กเกจ (spadmin/S4) — ไม่งั้นผลเก่าค้างได้ถึง 60 วิใน instance เดียวกัน</summary>
    public static void Invalidate(IMemoryCache cache, SelfServeOptions options, int branchId) =>
        cache.Remove(CacheKey(options, branchId));
}
