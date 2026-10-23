using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// S5 — คำสั่งทีมงานของ spadmin: Branch.EndPackageDate เป็นตัวจริงและสำเนา sp_Workspace ต้องตามเสมอ · Expired → Active เมื่อต่ออายุ ·
/// paid ไม่มีกำหนด · ระงับต้องมีเหตุผล · ปฏิเสธอู่ที่ลบ/ย้ายแล้ว และค่าตั้งที่ชี้ผิดฐาน
/// </summary>
public class SelfServeAdminServiceTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TrialEnd = new(2026, 11, 10, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Extend_moves_the_branch_end_and_its_copy_together()
    {
        var h = new Harness();

        var result = await h.Service.ExtendAsync(1042, 14, "ลูกค้าขอ", "spadmin:noi");

        result.Success.Should().BeTrue();
        var change = h.Repository.Changes.Single();
        change.SetBranchEnd.Should().BeTrue();
        change.EndsAtUtc.Should().Be(TrialEnd.AddDays(14));
        change.Plan.Should().Be(WorkspacePlanKind.Trial);
        change.Status.Should().Be(WorkspaceStatus.Active);
        change.ClearPurgeAfter.Should().BeFalse();
        change.AuditAction.Should().Be(SelfServeAdminService.ExtendAction);
        change.Actor.Should().Be("spadmin:noi");
        change.Note.Should().Be("ลูกค้าขอ");
        change.PayloadJson.Should().Contain("\"days\":14");
        result.Data!.EndsAtFrom.Should().Be(TrialEnd);
    }

    [Fact]
    public async Task Extending_an_expired_trial_into_the_future_reactivates_it()
    {
        var h = new Harness(ws => ws.Status = WorkspaceStatus.Expired, branchEnd: Now.AddDays(-3));

        await h.Service.ExtendAsync(1042, 7, null, "spadmin:noi");

        var change = h.Repository.Changes.Single();
        change.EndsAtUtc.Should().Be(Now.AddDays(4));
        change.Status.Should().Be(WorkspaceStatus.Active);
        change.ClearPurgeAfter.Should().BeTrue("วันลบเดิมคำนวณจากวันหมดอายุเก่า job รายวันตั้งใหม่ตอนหมดอายุครั้งหน้า");
    }

    [Fact]
    public async Task Negative_days_shorten_the_trial_without_touching_the_status()
    {
        var h = new Harness();

        await h.Service.ExtendAsync(1042, -31, null, "spadmin:noi");

        var change = h.Repository.Changes.Single();
        change.EndsAtUtc.Should().Be(TrialEnd.AddDays(-31));
        change.Status.Should().Be(WorkspaceStatus.Active, "job รายวันเป็นคนเปลี่ยนเป็น Expired · PlanPolicy อ่านวันใน Branch ทันที");
    }

    [Fact]
    public async Task Missing_branch_end_falls_back_to_the_trial_copy()
    {
        var h = new Harness(noBranchEnd: true);

        await h.Service.ExtendAsync(1042, 1, null, "a");

        h.Repository.Changes.Single().EndsAtUtc.Should().Be(TrialEnd.AddDays(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4000)]
    public async Task Extend_rejects_zero_or_absurd_day_counts(int days)
    {
        var h = new Harness();

        (await h.Service.ExtendAsync(1042, days, null, "a")).Error!.Code.Should().Be(SelfServeAdminErrors.ValidationCode);
        h.Repository.Changes.Should().BeEmpty();
    }

    public static TheoryData<string, Action<Workspace>> RefusedWorkspaces => new()
    {
        { "Purged", ws => ws.Status = WorkspaceStatus.Purged },
        { "Provisioning", ws => ws.Status = WorkspaceStatus.Provisioning },
        { "converted", ws => ws.Plan = WorkspacePlanKind.Converted }
    };

    [Theory]
    [MemberData(nameof(RefusedWorkspaces))]
    public async Task Package_changes_are_refused_for_purged_provisioning_or_converted_workspaces(string expected,
        Action<Workspace> change)
    {
        var h = new Harness(change);

        var extend = await h.Service.ExtendAsync(1042, 7, null, "a");
        var plan = await h.Service.SetPlanAsync(1042, WorkspacePlanKind.Paid, null, null, "a");

        extend.Error!.Code.Should().Be(SelfServeAdminErrors.RefusedCode);
        extend.Error.MessageTh.Should().Contain(expected);
        plan.Error!.Code.Should().Be(SelfServeAdminErrors.RefusedCode);
        h.Repository.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Writes_are_refused_when_the_configuration_points_at_another_database()
    {
        var h = new Harness();
        h.Repository.Mismatch = "SelfServe:ConnectionString ชี้ฐานเดียวกับ ConnectionStrings:ServiceDb (GarageService)";

        var result = await h.Service.ExtendAsync(1042, 7, null, "a");

        result.Error!.MessageTh.Should().Contain("GarageService");
        h.Repository.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task Paid_without_until_is_open_ended_and_reactivates()
    {
        var h = new Harness(ws => ws.Status = WorkspaceStatus.Expired, branchEnd: Now.AddDays(-10));

        var result = await h.Service.SetPlanAsync(1042, WorkspacePlanKind.Paid, null, "โอนแล้ว", "spadmin:noi");

        var change = h.Repository.Changes.Single();
        change.Plan.Should().Be(WorkspacePlanKind.Paid);
        change.SetBranchEnd.Should().BeTrue();
        change.EndsAtUtc.Should().BeNull("ไม่มีกำหนด = EndPackageDate NULL");
        change.Status.Should().Be(WorkspaceStatus.Active);
        change.ClearPurgeAfter.Should().BeTrue();
        result.Data!.EndsAtTo.Should().BeNull();
    }

    [Fact]
    public async Task Paid_until_a_date_ends_after_that_whole_thai_day()
    {
        var h = new Harness();

        await h.Service.SetPlanAsync(1042, WorkspacePlanKind.Paid, new DateOnly(2027, 12, 31), null, "a");

        h.Repository.Changes.Single().EndsAtUtc.Should().Be(new DateTime(2027, 12, 31, 17, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Paid_open_ended_cannot_be_extended()
    {
        var h = new Harness(ws => ws.Plan = WorkspacePlanKind.Paid, noBranchEnd: true);

        (await h.Service.ExtendAsync(1042, 30, null, "a")).Error!.MessageTh.Should().Contain("set-plan");
    }

    [Fact]
    public async Task Trial_keeps_its_end_when_until_is_omitted_and_needs_one_when_there_is_none()
    {
        var h = new Harness(ws => ws.Plan = WorkspacePlanKind.Suspended);
        await h.Service.SetPlanAsync(1042, WorkspacePlanKind.Trial, null, null, "a");
        h.Repository.Changes.Single().Should().Match<WorkspacePackageChange>(c =>
            c.Plan == WorkspacePlanKind.Trial && !c.SetBranchEnd && c.EndsAtUtc == TrialEnd);

        var none = new Harness(ws => ws.TrialEndsAt = null, noBranchEnd: true);
        (await none.Service.SetPlanAsync(1042, WorkspacePlanKind.Trial, null, null, "a"))
            .Error!.Code.Should().Be(SelfServeAdminErrors.ValidationCode);
    }

    [Fact]
    public async Task Suspend_needs_a_reason_and_keeps_the_dates()
    {
        var h = new Harness();

        (await h.Service.SuspendAsync(1042, "  ", "a")).Error!.Code.Should().Be(SelfServeAdminErrors.ValidationCode);

        await h.Service.SuspendAsync(1042, "สแปม", "spadmin:noi");
        var change = h.Repository.Changes.Single();
        change.Plan.Should().Be(WorkspacePlanKind.Suspended);
        change.SetBranchEnd.Should().BeFalse();
        change.AuditAction.Should().Be(SelfServeAdminService.SuspendAction);
        change.Note.Should().Be("สแปม");
    }

    [Fact]
    public async Task Converted_cannot_be_set_by_hand()
    {
        var h = new Harness();

        (await h.Service.SetPlanAsync(1042, WorkspacePlanKind.Converted, null, null, "a"))
            .Error!.Code.Should().Be(SelfServeAdminErrors.ValidationCode);
    }

    [Fact]
    public async Task Purge_passes_the_paid_confirmation_and_operator_to_the_purger()
    {
        var h = new Harness();

        await h.Service.PurgeAsync(1042, allowPaid: true, "spadmin:noi");

        h.Purger.Requests.Single().Should().Be(new WorkspacePurgeRequest(h.Workspace.Id, "spadmin:noi", AllowPaid: true));
        (await h.Service.PurgeAsync(9999, false, "a")).Error!.Code.Should().Be(SelfServeLifecycleErrors.WorkspaceNotFoundCode);
    }

    [Fact]
    public async Task Run_jobs_never_claims_the_daily_run()
    {
        var h = new Harness();
        var asOf = Now.AddDays(91);

        (await h.Service.RunJobsAsync(TrialLifecycleTasks.Maintenance, asOf)).Success.Should().BeTrue();
        (await h.Service.RunJobsAsync("cleanup", null)).Error!.Code.Should().Be(SelfServeAdminErrors.ValidationCode);

        h.Lifecycle.Calls.Should().Equal((TrialLifecycleTasks.Maintenance, (DateTime?)asOf, false));
    }

    [Fact]
    public async Task Show_reports_limits_for_trials_and_flags_a_stale_copy()
    {
        var h = new Harness(ws => ws.TrialEndsAt = TrialEnd.AddDays(-5));

        var detail = (await h.Service.ShowAsync(1042)).Data!;

        detail.EndsAt.Should().Be(TrialEnd, "Branch เป็นตัวจริง");
        detail.DaysLeft.Should().Be(9);
        detail.Usage.Should().ContainEquivalentOf(new AdminLimitUsage("Staff", 3, 5));
        detail.Usage.Should().ContainEquivalentOf(new AdminLimitUsage("Customers", 3, 200));
        detail.Warnings.Should().ContainSingle(w => w.Contains("TrialEndsAt"));

        var paid = new Harness(ws => ws.Plan = WorkspacePlanKind.Paid);
        (await paid.Service.ShowAsync(1042)).Data!.Usage.Should().OnlyContain(u => u.Max == null);
    }

    // ───────────────────────────────────────────── plumbing

    private sealed class Harness
    {
        public Harness(Action<Workspace>? configure = null, DateTime? branchEnd = null, bool noBranchEnd = false)
        {
            Workspace = new Workspace
            {
                WorkspaceNo = 1042, BranchId = 501, Name = "อู่ช่างเอก", Plan = WorkspacePlanKind.Trial,
                Status = WorkspaceStatus.Active, TrialStartsAt = TrialEnd.AddDays(-30), TrialEndsAt = TrialEnd,
                CreatedAt = TrialEnd.AddDays(-30)
            };
            configure?.Invoke(Workspace);
            Repository = new FakeRepository(Workspace, new BranchPackage(501, 1, TrialEnd.AddDays(-30), 30,
                noBranchEnd ? null : branchEnd ?? TrialEnd));
            Service = new SelfServeAdminService(Repository, new FakeUsage(), Purger, Lifecycle, new SelfServeOptions(),
                new FixedClock(Now));
        }

        public Workspace Workspace { get; }
        public FakeRepository Repository { get; }
        public FakePurger Purger { get; } = new();
        public FakeLifecycle Lifecycle { get; } = new();
        public SelfServeAdminService Service { get; }
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FakeRepository(Workspace workspace, BranchPackage branch) : ISelfServeAdminRepository
    {
        public List<WorkspacePackageChange> Changes { get; } = [];
        public string? Mismatch { get; set; }

        public Task<IReadOnlyList<AdminWorkspaceRow>> ListAsync(WorkspaceStatus? status, WorkspacePlanKind? plan,
            CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AdminWorkspaceSnapshot?> GetAsync(int workspaceNo, int auditTail, CancellationToken ct = default) =>
            Task.FromResult(workspaceNo == workspace.WorkspaceNo
                ? new AdminWorkspaceSnapshot(workspace, null, branch, [])
                : null);

        public string? CheckWritableDatabase() => Mismatch;

        public Task ApplyPackageChangeAsync(WorkspacePackageChange change, CancellationToken ct = default)
        {
            Changes.Add(change);
            return Task.CompletedTask;
        }

        public Task<ApprovePaymentOutcome> ApprovePaymentAsync(WorkspacePackageChange change,
            PackagePaymentApproval approval, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeUsage : IPlanUsageCounter
    {
        public Task<int> CountAsync(string shardKey, int branchId, PlanLimitKind kind, CancellationToken ct = default) =>
            Task.FromResult(3);
    }

    private sealed class FakePurger : IWorkspacePurger
    {
        public List<WorkspacePurgeRequest> Requests { get; } = [];

        public Task<Result<WorkspacePurgeReport>> PurgeAsync(WorkspacePurgeRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result<WorkspacePurgeReport>.Ok(new WorkspacePurgeReport(request.WorkspaceId, 1042, 501,
                false, new Dictionary<string, int>(), 0, 0, [])));
        }

        public Task<Result<WorkspacePurgeReport>> PurgeAbandonedServiceRowsAsync(Guid workspaceId, string actor,
            CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeLifecycle : ITrialLifecycleService
    {
        public List<(string Task, DateTime? AsOf, bool Claim)> Calls { get; } = [];

        public Task<IReadOnlyList<LifecycleRunReport>> RunDueAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<LifecycleRunReport> RunAsync(string task, DateTime? asOfUtc = null, bool claim = false,
            CancellationToken ct = default)
        {
            Calls.Add((task, asOfUtc, claim));
            return Task.FromResult(new LifecycleRunReport(task, DateOnly.FromDateTime(Now), true, "ok",
                new Dictionary<string, int>(), []));
        }
    }
}
