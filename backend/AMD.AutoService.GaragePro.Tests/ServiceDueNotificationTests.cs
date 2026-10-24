using AMD.AutoService.GaragePro.API.ServiceDue;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Notifications;
using AMD.AutoService.GaragePro.Application.ServiceDue;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using static AMD.AutoService.GaragePro.Tests.ReportsServiceTests;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>แจ้งเตือนรถใกล้ครบรอบบริการ (2026-10-25): แจ้งครั้งเดียวต่อรอบ · ไม่แจ้งย้อนหลัง · ไม่แจ้งรถที่มีจ๊อบเปิด · แยก shard/สาขา · งานรายวัน</summary>
public sealed class ServiceDueNotificationTests
{
    private static readonly DateTime Now = new(2026, 10, 25, 3, 0, 0, DateTimeKind.Utc); // 10:00 น. 25 ต.ค. เวลาไทย
    private static readonly DateOnly Today = new(2026, 10, 25);

    private sealed class FixedClock(DateTime utc) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utc);
    }

    private sealed class SystemUser(string shard = "db2", int branch = 105) : ICurrentUser
    {
        public long UserId => 0;
        public string UserName => "ระบบ";
        public UserRole Role => UserRole.Manager;
        public string ShardKey => shard;
        public int BranchId => branch;
        public EventSource Source => EventSource.System;
        public Guid? SessionId => null;
        public bool IsAdministrator => false;
    }

    /// <summary>เก็บเฉพาะสิ่งที่ notifier ใช้: กุญแจที่เคยมีแล้ว + จำนวนครั้งที่ save</summary>
    private sealed class FakeNotificationRepository : INotificationRepository
    {
        public HashSet<string> Existing { get; } = [];
        public int Saves { get; private set; }
        public void Add(Notification notification) { }
        public Task<IReadOnlyList<Notification>> GetOpenBySubjectAsync(string s, int b, string key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Notification>> GetOpenBySubjectPrefixAsync(string s, int b, string prefix, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlySet<string>> GetExistingSubjectKeysAsync(string s, int b, IReadOnlyCollection<string> keys, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(keys.Where(Existing.Contains).ToHashSet());
        public Task<IReadOnlyList<NotificationRow>> PageAsync(NotificationViewer v, DateTime since, bool unreadOnly, DateTime? beforeAt, Guid? beforeId, int take, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountUnreadAsync(NotificationViewer v, DateTime since, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetUnreadIdsAsync(NotificationViewer v, DateTime since, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Notification?> GetVisibleAsync(NotificationViewer v, DateTime since, Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NotificationRead?> GetReadAsync(Guid id, long staffId, CancellationToken ct = default) => throw new NotSupportedException();
        public void AddRead(NotificationRead read) { }
        public void RemoveRead(NotificationRead read) { }
        public Task SaveChangesAsync(CancellationToken ct = default) { Saves++; return Task.CompletedTask; }
    }

    private static (ServiceDueNotifier Notifier, FakeNotificationPublisher Publisher, FakeNotificationRepository Repo) Build(
        FakeReportsRepository reports, string shard = "db2", int branch = 105)
    {
        var publisher = new FakeNotificationPublisher();
        var repo = new FakeNotificationRepository();
        return (new ServiceDueNotifier(reports, repo, publisher, new SystemUser(shard, branch), new FixedClock(Now)), publisher, repo);
    }

    /// <summary>กุญแจเรื่องของรถ + วันนัดที่อีก N วัน (วันที่ตามรูปแบบคงที่ ไม่ขึ้นกับ culture ของเครื่อง)</summary>
    private static string Key(long vehicleId, int dueInDays) =>
        $"service-due:{vehicleId}:{Today.AddDays(dueInDays).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)}";

    private static FakeReportsRepository Vehicle(
        long vehicleId, int dueInDays, string plate = "1กก-1234", string shard = "db2", int branch = 105,
        JobStatus? otherJob = null, FakeReportsRepository? into = null, params (string, int)[] extraItems)
    {
        var repo = into ?? new FakeReportsRepository();
        var job = VehicleJob(vehicleId, $"JB-{shard}-{branch}-{vehicleId}", Now.AddDays(-30), plate: plate, shard: shard, branch: branch);
        repo.Jobs.Add(job);
        var items = new List<(string, int, DateOnly)> { ("น้ำมันเครื่อง", 15_000, Today.AddDays(dueInDays)) };
        items.AddRange(extraItems.Select(e => (e.Item1, 30_000, Today.AddDays(e.Item2))));
        repo.Handovers.Add(HandedOver(job.Id, Now.AddDays(-29), 10_000, items.ToArray()));
        if (otherJob is { } status)
            repo.Jobs.Add(VehicleJob(vehicleId, $"JB-{vehicleId}-X", Now.AddDays(-1), plate: plate, status: status, shard: shard, branch: branch));
        return repo;
    }

    [Fact]
    public async Task Notifies_front_desk_and_manager_with_plate_items_and_a_dedupe_key()
    {
        var reports = Vehicle(7, 5, "2ขข-2222", extraItems: [("กรองอากาศ", 6), ("น้ำมันเกียร์", 200)]);
        var (notifier, publisher, repo) = Build(reports);

        var created = await notifier.NotifyBranchAsync();

        created.Should().Be(1);
        repo.Saves.Should().Be(1);
        var (draft, roles) = publisher.ToRoles.Single();
        roles.Should().BeEquivalentTo([UserRole.FrontDesk, UserRole.Manager]);
        draft.Kind.Should().Be(NotificationKinds.ServiceDue).And.Be("service.due");
        draft.SubjectKey.Should().Be(Key(7, 5));
        draft.TitleTh.Should().Contain("2ขข-2222").And.Contain("อีก 5 วัน");
        // รายการที่ครบในหน้าต่าง 7 วันเท่านั้น — น้ำมันเกียร์ที่นัดไกลกว่านั้นไม่ขึ้น
        draft.BodyTh.Should().Contain("น้ำมันเครื่อง").And.Contain("กรองอากาศ").And.NotContain("น้ำมันเกียร์");
        draft.LinkHint.Should().Be("service-due");
        draft.EntityType.Should().Be("ServiceDue");
    }

    [Fact]
    public async Task Due_today_is_worded_as_due_today()
    {
        var (notifier, publisher, _) = Build(Vehicle(7, 0));

        await notifier.NotifyBranchAsync();

        publisher.ToRoles.Single().Draft.TitleTh.Should().Contain("ครบกำหนดวันนี้");
    }

    [Fact]
    public async Task Notifies_once_per_round_even_when_the_job_runs_again()
    {
        var (notifier, publisher, repo) = Build(Vehicle(7, 5));
        await notifier.NotifyBranchAsync();
        // จำลองการบันทึกลงฐาน — รอบถัดไป repository เห็นกุญแจแล้ว
        foreach (var (draft, _) in publisher.ToRoles) repo.Existing.Add(draft.SubjectKey!);

        var second = await notifier.NotifyBranchAsync();

        second.Should().Be(0);
        publisher.ToRoles.Should().HaveCount(1);
        repo.Saves.Should().Be(1, "ไม่มีอะไรใหม่ก็ไม่ save");
    }

    [Fact]
    public async Task A_resolved_notification_still_blocks_a_repeat_for_the_same_round()
    {
        var reports = Vehicle(7, 5);
        var (notifier, publisher, repo) = Build(reports);
        repo.Existing.Add(Key(7, 5)); // เคยแจ้งแล้วและปิดไปแล้ว (resolve) — ยังนับว่าเคยแจ้ง

        (await notifier.NotifyBranchAsync()).Should().Be(0);
        publisher.ToRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_due_date_for_the_same_vehicle_is_a_new_round_and_notifies_again()
    {
        var (notifier, publisher, repo) = Build(Vehicle(7, 5));
        repo.Existing.Add(Key(7, -175)); // รอบเก่าเมื่อครึ่งปีก่อน

        (await notifier.NotifyBranchAsync()).Should().Be(1);
        publisher.ToRoles.Should().ContainSingle();
    }

    [Fact]
    public async Task Does_not_backfill_vehicles_already_overdue_or_not_yet_in_the_seven_day_window()
    {
        var reports = Vehicle(1, -1, "ค้าง-1");
        Vehicle(2, 8, "ไกล-2", into: reports);
        Vehicle(3, 7, "พอดี-3", into: reports);
        var (notifier, publisher, _) = Build(reports);

        (await notifier.NotifyBranchAsync()).Should().Be(1);
        publisher.ToRoles.Single().Draft.TitleTh.Should().Contain("พอดี-3");
    }

    [Theory]
    [InlineData(JobStatus.WaitInspect)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Completed)] // กลับมาเข้าอู่แล้วหลังส่งมอบครั้งนั้น (แม้ปิดงานแล้ว) → รอบนัดเดิมจบ
    public async Task Does_not_notify_a_vehicle_that_already_has_a_newer_job(JobStatus status)
    {
        var (notifier, publisher, _) = Build(Vehicle(7, 3, otherJob: status));

        (await notifier.NotifyBranchAsync()).Should().Be(0);
        publisher.ToRoles.Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_return_visit_does_not_stop_the_reminder()
    {
        var (notifier, _, _) = Build(Vehicle(7, 3, otherJob: JobStatus.Cancelled));

        (await notifier.NotifyBranchAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Only_the_branch_in_scope_is_notified_and_other_branches_and_shards_are_untouched()
    {
        var reports = Vehicle(1, 3, "สาขา105");
        Vehicle(1, 3, "สาขา106", branch: 106, into: reports);
        Vehicle(1, 3, "ฐานอื่น", shard: "sp1", branch: 105, into: reports);
        var (notifier, publisher, _) = Build(reports, shard: "db2", branch: 105);

        (await notifier.NotifyBranchAsync()).Should().Be(1);
        publisher.ToRoles.Single().Draft.TitleTh.Should().Contain("สาขา105");
    }

    // ---- ไล่ทุกสาขา/ทุกฐาน ------------------------------------------------------------------------------

    private sealed class RecordingNotifier(string shard, int branch, List<(string, int)> log, bool fail) : IServiceDueNotifier
    {
        public Task<int> NotifyBranchAsync(CancellationToken ct = default)
        {
            log.Add((shard, branch));
            if (fail) throw new InvalidOperationException("boom");
            return Task.FromResult(1);
        }
    }

    /// <summary>จำลองฐานแต่ละฐาน: การไล่หาสาขา (ใน scope ของ shard ตัวแทนฐาน) คืนเฉพาะสาขาของฐานนั้น</summary>
    private sealed class FakeScopes(
        Dictionary<string, List<ServiceDueBranch>> branchesByDatabase, List<(string, int)> notified, int failBranch = -1)
        : IShardScopeFactory
    {
        public List<(string Shard, int Branch)> Scopes { get; } = [];

        public async Task<T> RunAsAsync<T>(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task<T>> work, CancellationToken ct = default)
        {
            Scopes.Add((user.ShardKey, user.BranchId));
            var services = new ServiceCollection();
            services.AddSingleton<IReportsRepository>(
                new FakeReportsRepository { BranchesOverride = branchesByDatabase.GetValueOrDefault(user.ShardKey) ?? [] });
            services.AddSingleton<IServiceDueNotifier>(new RecordingNotifier(user.ShardKey, user.BranchId, notified, user.BranchId == failBranch));
            await using var provider = services.BuildServiceProvider();
            return await work(provider, ct);
        }

        public Task RunAsAsync(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct = default) =>
            RunAsAsync<object?>(user, async (sp, t) => { await work(sp, t); return null; }, ct);
    }

    [Fact]
    public async Task Sweep_walks_every_branch_of_every_database_in_its_own_shard_scope()
    {
        var notified = new List<(string, int)>();
        var scopes = new FakeScopes(new()
        {
            ["db2"] = [new("db2", 105), new("db2", 106)],
            ["sp1"] = [new("sp1", 3)]
        }, notified);
        var sweep = new ServiceDueSweepService(scopes, new ServiceDueSweepOptions(["db2", "sp1"]), new FixedClock(Now));

        var report = await sweep.RunAsync();

        report.BranchesChecked.Should().Be(3);
        report.NotificationsCreated.Should().Be(3);
        report.Errors.Should().BeEmpty();
        // แต่ละสาขารันใน scope ของ shard ตัวเอง — สาขาของ db2 ไม่ถูกรันใน scope ของ sp1 และกลับกัน
        notified.Should().BeEquivalentTo([("db2", 105), ("db2", 106), ("sp1", 3)]);
        scopes.Scopes.Should().Contain([("db2", 0), ("sp1", 0), ("db2", 105), ("db2", 106), ("sp1", 3)]);
    }

    [Fact]
    public async Task Sweep_without_the_self_serve_database_only_looks_at_the_main_database()
    {
        var notified = new List<(string, int)>();
        var scopes = new FakeScopes(new() { ["db2"] = [new("db2", 105)], ["sp1"] = [new("sp1", 3)] }, notified);
        var sweep = new ServiceDueSweepService(scopes, new ServiceDueSweepOptions(["db2"]), new FixedClock(Now));

        await sweep.RunAsync();

        notified.Should().Equal(("db2", 105));
        scopes.Scopes.Select(s => s.Shard).Should().NotContain("sp1");
    }

    [Fact]
    public async Task One_failing_branch_does_not_stop_the_others_and_is_reported()
    {
        var notified = new List<(string, int)>();
        var scopes = new FakeScopes(new() { ["db2"] = [new("db2", 105), new("db2", 106), new("db2", 107)] }, notified, failBranch: 106);
        var sweep = new ServiceDueSweepService(scopes, new ServiceDueSweepOptions(["db2"]), new FixedClock(Now));

        var report = await sweep.RunAsync();

        notified.Select(n => n.Item2).Should().Equal(105, 106, 107);
        report.NotificationsCreated.Should().Be(2);
        report.Errors.Should().ContainSingle().Which.Should().Contain("106");
    }

    // ---- งานรายวัน --------------------------------------------------------------------------------------

    private sealed class CountingSweep : IServiceDueSweepService
    {
        public int Runs { get; private set; }
        public Task<ServiceDueSweepReport> RunAsync(CancellationToken ct = default)
        {
            Runs++;
            return Task.FromResult(new ServiceDueSweepReport(0, 0, []));
        }
    }

    private static ServiceDueDailyJob Job(CountingSweep sweep, string? enabled) => new(
        sweep,
        new ConfigurationBuilder().AddInMemoryCollection(
            enabled is null ? [] : new Dictionary<string, string?> { [ServiceDueDailyJob.EnabledKey] = enabled }).Build(),
        TimeProvider.System, NullLogger<ServiceDueDailyJob>.Instance);

    [Fact]
    public async Task The_daily_job_does_nothing_when_disabled_by_config()
    {
        var sweep = new CountingSweep();
        var job = Job(sweep, "false");

        await job.StartAsync(CancellationToken.None);
        await (job.ExecuteTask ?? Task.CompletedTask);
        await job.StopAsync(CancellationToken.None);

        sweep.Runs.Should().Be(0);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("true", true)]
    [InlineData("garbage", true)]
    [InlineData("false", false)]
    [InlineData(" FALSE ", false)]
    public void Job_is_enabled_unless_explicitly_switched_off(string? value, bool expected)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            value is null ? [] : new Dictionary<string, string?> { [ServiceDueDailyJob.EnabledKey] = value }).Build();

        ServiceDueDailyJob.IsEnabled(config).Should().Be(expected);
    }

    [Fact]
    public async Task Tick_runs_the_sweep_and_swallows_failures_so_the_api_keeps_running()
    {
        var sweep = new CountingSweep();
        await Job(sweep, null).TickAsync(CancellationToken.None);
        sweep.Runs.Should().Be(1);

        var failing = new ServiceDueDailyJob(new ThrowingSweep(), new ConfigurationBuilder().Build(),
            TimeProvider.System, NullLogger<ServiceDueDailyJob>.Instance);
        await failing.TickAsync(CancellationToken.None); // ต้องไม่โยน
    }

    private sealed class ThrowingSweep : IServiceDueSweepService
    {
        public Task<ServiceDueSweepReport> RunAsync(CancellationToken ct = default) => throw new InvalidOperationException("db down");
    }

    [Theory]
    [InlineData("2026-10-24T20:00:00Z", "2026-10-25T01:00:00Z")] // 03:00 ไทย → วันนี้ 08:00
    [InlineData("2026-10-25T00:59:59Z", "2026-10-25T01:00:00Z")] // 07:59:59 ไทย
    [InlineData("2026-10-25T01:00:00Z", "2026-10-26T01:00:00Z")] // 08:00 พอดี → พรุ่งนี้
    [InlineData("2026-10-25T03:00:00Z", "2026-10-26T01:00:00Z")] // 10:00 ไทย
    [InlineData("2026-10-25T18:00:00Z", "2026-10-26T01:00:00Z")] // 01:00 ไทยของวันที่ 26 → 08:00 วันที่ 26
    public void Next_run_is_08_00_thai_time(string nowUtc, string expectedUtc)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var now = DateTime.Parse(nowUtc, inv, System.Globalization.DateTimeStyles.AdjustToUniversal);
        var expected = DateTime.Parse(expectedUtc, inv, System.Globalization.DateTimeStyles.AdjustToUniversal);

        ServiceDueDailyJob.NextRunUtc(now).Should().Be(expected);
    }
}
