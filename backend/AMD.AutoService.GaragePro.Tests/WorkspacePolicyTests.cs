using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// WorkspacePolicy = ตัวเชื่อม I/O รอบ PlanPolicy · สิ่งที่ต้องไม่พัง:
/// (1) shard ลูกค้าเดิมไม่แตะฐานเลย (2) อ่านซ้ำภายใน 60 วิได้จาก cache (3) ลิมิตนับเฉพาะ sp1 และตอบ PLAN_LIMIT_REACHED
/// </summary>
public class WorkspacePolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeReader : IWorkspacePlanReader
    {
        public int Calls { get; private set; }
        public List<(string Shard, int Branch)> Requests { get; } = [];

        public WorkspacePlanInputs Inputs { get; set; } = new(
            new BranchPackage(7, 1, Now.AddDays(-5), 30, Now.AddDays(25)),
            new WorkspaceSnapshot(WorkspacePlanKind.Trial, WorkspaceStatus.Active, 1007, Now.AddDays(25)));

        public Task<WorkspacePlanInputs> ReadAsync(string shardKey, int branchId, CancellationToken ct = default)
        {
            Calls++;
            Requests.Add((shardKey, branchId));
            return Task.FromResult(Inputs);
        }
    }

    private sealed class FakeUsage : IPlanUsageCounter
    {
        public Dictionary<PlanLimitKind, int> Used { get; } = [];
        public int Calls { get; private set; }

        public Task<int> CountAsync(string shardKey, int branchId, PlanLimitKind kind, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Used.GetValueOrDefault(kind));
        }
    }

    private static (WorkspacePolicy Policy, FakeReader Reader, FakeUsage Usage, FixedClock Clock, MemoryCache Cache)
        Create(SelfServeOptions? options = null)
    {
        var reader = new FakeReader();
        var usage = new FakeUsage();
        var clock = new FixedClock(Now);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var policy = new WorkspacePolicy(reader, usage, cache, Options.Create(options ?? new SelfServeOptions()), clock);
        return (policy, reader, usage, clock, cache);
    }

    [Theory]
    [InlineData("db2")]
    [InlineData("db1")]
    public async Task Legacy_shard_is_legacy_without_touching_any_database(string shard)
    {
        var (policy, reader, usage, _, cache) = Create();
        using var _ = cache;

        var plan = await policy.GetAsync(shard, 227);
        var limit = await policy.CheckLimitAsync(shard, 227, PlanLimitKind.Staff);

        plan.Should().BeSameAs(WorkspacePlan.Legacy);
        limit.Should().BeNull();
        reader.Calls.Should().Be(0, "ลูกค้าเดิมต้องไม่ช้าลง/ไม่ล้มเพราะฐาน self-serve");
        usage.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Self_serve_shard_evaluates_branch_and_workspace_data()
    {
        var (policy, reader, _, _, cache) = Create();
        using var _ = cache;

        var plan = await policy.GetAsync("sp1", 7);

        plan.Kind.Should().Be(PlanKind.Trial);
        plan.WorkspaceNo.Should().Be(1007);
        plan.ExpiresAt.Should().Be(Now.AddDays(25));
        plan.IsReadOnly.Should().BeFalse();
        reader.Requests.Should().Equal([("sp1", 7)]);
    }

    [Fact]
    public async Task Second_read_within_a_minute_is_served_from_cache_and_re_evaluated_with_the_current_time()
    {
        var (policy, reader, _, clock, cache) = Create();
        using var _ = cache;
        reader.Inputs = reader.Inputs with
        {
            Branch = new BranchPackage(7, 1, Now.AddDays(-30), 30, Now.AddSeconds(20))
        };

        var before = await policy.GetAsync("sp1", 7);
        clock.Now = Now.AddSeconds(30);
        var after = await policy.GetAsync("SP1", 7);

        reader.Calls.Should().Be(1, "ภายใน 60 วิต้องไม่อ่านฐานซ้ำ — รวมกรณีส่ง shard ตัวพิมพ์ต่างกัน");
        before.IsReadOnly.Should().BeFalse();
        after.IsReadOnly.Should().BeTrue("cache เก็บข้อมูลดิบ ไม่ใช่ผลที่ตัดสินแล้ว — หมดอายุตรงเวลาแม้ยังอยู่ใน cache");
    }

    [Fact]
    public async Task Cache_is_per_branch_and_can_be_invalidated()
    {
        var options = new SelfServeOptions();
        var (policy, reader, _, _, cache) = Create(options);
        using var _ = cache;

        await policy.GetAsync("sp1", 7);
        await policy.GetAsync("sp1", 8);
        reader.Calls.Should().Be(2);

        WorkspacePolicy.Invalidate(cache, options, 7);
        await policy.GetAsync("sp1", 7);

        reader.Calls.Should().Be(3);
    }

    [Fact]
    public async Task Limit_is_reached_when_used_equals_the_maximum()
    {
        var (policy, _, usage, _, cache) = Create();
        using var _ = cache;
        usage.Used[PlanLimitKind.Staff] = 5;
        usage.Used[PlanLimitKind.Jobs] = 99;

        var staff = await policy.CheckLimitAsync("sp1", 7, PlanLimitKind.Staff);
        var jobs = await policy.CheckLimitAsync("sp1", 7, PlanLimitKind.Jobs);

        staff.Should().NotBeNull();
        staff!.Code.Should().Be(PlanErrors.LimitReachedCode);
        PlanErrors.StatusCodeFor(staff.Code).Should().Be(422);
        jobs.Should().BeNull("ใช้ไป 99 จาก 100 ยังเพิ่มได้อีกหนึ่ง");
    }

    [Fact]
    public async Task Paid_workspace_is_never_counted()
    {
        var (policy, reader, usage, _, cache) = Create();
        using var _ = cache;
        reader.Inputs = reader.Inputs with
        {
            Workspace = new WorkspaceSnapshot(WorkspacePlanKind.Paid, WorkspaceStatus.Active, 1007, null)
        };
        usage.Used[PlanLimitKind.CatalogItems] = 10_000;

        var result = await policy.CheckLimitAsync("sp1", 7, PlanLimitKind.CatalogItems);

        result.Should().BeNull();
        usage.Calls.Should().Be(0, "ไม่จำกัด = ไม่ต้องนับ");
    }

    [Fact]
    public async Task Limits_follow_configuration()
    {
        var (policy, _, usage, _, cache) = Create(new SelfServeOptions { Limits = { Customers = 3 } });
        using var _ = cache;
        usage.Used[PlanLimitKind.Customers] = 3;

        var result = await policy.CheckLimitAsync("sp1", 7, PlanLimitKind.Customers);

        result.Should().NotBeNull();
        result!.MessageTh.Should().Contain("ลูกค้า").And.Contain("3");
    }
}
