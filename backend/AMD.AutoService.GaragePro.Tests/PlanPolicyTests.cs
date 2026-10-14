using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ล็อกกฎแพ็กเกจของ shard self-serve (docs/12 §6): ใช้ได้ 30 วัน → อ่านอย่างเดียว 90 วัน → ล็อก
/// · ถ้าเทสต์นี้แดง อู่ทดลองใช้จะเขียนข้อมูลได้หลังหมดอายุ หรือถูกล็อกก่อนเวลา
/// </summary>
public class PlanPolicyTests
{
    private static readonly DateTime Start = new(2026, 10, 1, 3, 0, 0, DateTimeKind.Utc);
    private static readonly SelfServeOptions Options = new();

    private static BranchPackage Branch(DateTime? end, int status = 1) =>
        new(BranchId: 1, Status: status, StartsAtUtc: Start, PackageDays: 30, EndsAtUtc: end);

    private static WorkspaceSnapshot Workspace(WorkspacePlanKind plan = WorkspacePlanKind.Trial,
        WorkspaceStatus status = WorkspaceStatus.Active) =>
        new(plan, status, WorkspaceNo: 1042, TrialEndsAtUtc: Start.AddDays(30));

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(29, false, false)]
    [InlineData(30, true, false)]
    [InlineData(119, true, false)]
    [InlineData(120, true, true)]
    public void Trial_is_writable_for_30_days_then_read_only_for_90_then_locked(int day, bool readOnly, bool locked)
    {
        var plan = PlanPolicy.Evaluate(Start.AddDays(day), Branch(Start.AddDays(30)), Workspace(), Options);

        plan.Kind.Should().Be(PlanKind.Trial);
        plan.IsReadOnly.Should().Be(readOnly);
        plan.IsLocked.Should().Be(locked);
        plan.Features.Should().Be(PlanFeature.None, "แพ็กเกจทดลองใช้ล็อกฟีเจอร์ขั้นสูงทั้งหมด");
        plan.Limits.Should().Be(new PlanLimits(5, 200, 200, 100));
        plan.ExpiresAt.Should().Be(Start.AddDays(30));
        plan.WorkspaceNo.Should().Be(1042);
    }

    [Fact]
    public void Read_only_starts_exactly_at_the_end_instant()
    {
        var end = Start.AddDays(30);

        PlanPolicy.Evaluate(end.AddTicks(-1), Branch(end), Workspace(), Options).IsReadOnly.Should().BeFalse();
        PlanPolicy.Evaluate(end, Branch(end), Workspace(), Options).IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Trial_limits_and_read_only_window_come_from_options()
    {
        var options = new SelfServeOptions
        {
            Trial = { ReadOnlyDays = 10 },
            Limits = { Staff = 2, CatalogItems = 20, Customers = 30, Jobs = 40 }
        };

        var plan = PlanPolicy.Evaluate(Start.AddDays(40), Branch(Start.AddDays(30)), Workspace(), options);

        plan.IsLocked.Should().BeTrue("30 วัน + อ่านอย่างเดียว 10 วัน = ล็อกวันที่ 40");
        plan.Limits.Should().Be(new PlanLimits(2, 20, 30, 40));
    }

    [Fact]
    public void Trial_without_an_end_date_in_Branch_falls_back_to_the_copy_in_the_workspace()
    {
        var branch = new BranchPackage(1, 1, Start, PackageDays: 30, EndsAtUtc: null);

        var plan = PlanPolicy.Evaluate(Start.AddDays(31), branch, Workspace(), Options);

        plan.ExpiresAt.Should().Be(Start.AddDays(30), "Workspace().TrialEndsAtUtc = วันที่ 30");
        plan.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Paid_without_an_end_date_ignores_leftover_trial_dates()
    {
        // เปลี่ยนเป็น paid แบบไม่มีกำหนด: EndPackageDate = NULL แต่ Start/Days และ TrialEndsAt ของช่วงทดลองยังค้าง
        var branch = new BranchPackage(1, 1, Start, PackageDays: 30, EndsAtUtc: null);

        var plan = PlanPolicy.Evaluate(Start.AddDays(200), branch, Workspace(WorkspacePlanKind.Paid), Options);

        plan.ExpiresAt.Should().BeNull();
        plan.IsReadOnly.Should().BeFalse();
        plan.IsLocked.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(400)]
    public void Paid_has_every_feature_and_no_limit_while_not_expired(int? daysUntilEnd)
    {
        DateTime? end = daysUntilEnd is { } d ? Start.AddDays(d) : null;

        var plan = PlanPolicy.Evaluate(Start.AddDays(200), Branch(end), Workspace(WorkspacePlanKind.Paid), Options);

        plan.Kind.Should().Be(PlanKind.Paid);
        plan.Features.Should().Be(PlanFeature.All);
        plan.Limits.Should().Be(PlanLimits.Unlimited);
        plan.IsReadOnly.Should().BeFalse();
        plan.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void Paid_that_lapses_follows_the_same_read_only_then_locked_rule()
    {
        var end = Start.AddDays(365);
        var paid = Workspace(WorkspacePlanKind.Paid);

        var lapsed = PlanPolicy.Evaluate(end.AddDays(1), Branch(end), paid, Options);
        lapsed.IsReadOnly.Should().BeTrue();
        lapsed.IsLocked.Should().BeFalse();
        lapsed.Features.Should().Be(PlanFeature.All, "ยังจ่ายเงินแล้ว อ่าน/พิมพ์ทุกส่วนได้");

        PlanPolicy.Evaluate(end.AddDays(90), Branch(end), paid, Options).IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Suspended_is_locked_immediately_even_before_the_end_date()
    {
        var plan = PlanPolicy.Evaluate(Start.AddDays(1), Branch(Start.AddDays(30)),
            Workspace(WorkspacePlanKind.Suspended), Options);

        plan.Kind.Should().Be(PlanKind.Suspended);
        plan.IsLocked.Should().BeTrue();
        plan.IsReadOnly.Should().BeTrue();
        plan.Features.Should().Be(PlanFeature.None);
    }

    [Theory]
    [InlineData(WorkspaceStatus.Provisioning)]
    [InlineData(WorkspaceStatus.Purged)]
    public void Workspace_that_is_not_ready_or_already_purged_is_locked(WorkspaceStatus status)
    {
        var plan = PlanPolicy.Evaluate(Start.AddDays(1), Branch(Start.AddDays(30)),
            Workspace(status: status), Options);

        plan.IsLocked.Should().BeTrue();
        plan.IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Expired_status_alone_does_not_override_the_dates_in_Branch()
    {
        // spadmin extend เลื่อน EndPackageDate แล้วแต่ job ยังไม่ทันเปลี่ยน Status — วันที่ใน Branch เป็นตัวตัดสิน
        var plan = PlanPolicy.Evaluate(Start.AddDays(31), Branch(Start.AddDays(60)),
            Workspace(status: WorkspaceStatus.Expired), Options);

        plan.IsReadOnly.Should().BeFalse();
        plan.IsLocked.Should().BeFalse();
    }

    [Fact]
    public void Disabled_branch_is_locked()
    {
        var plan = PlanPolicy.Evaluate(Start.AddDays(1), Branch(Start.AddDays(30), status: 0), Workspace(), Options);

        plan.IsLocked.Should().BeTrue();
    }

    [Fact]
    public void Missing_branch_fails_closed()
    {
        var plan = PlanPolicy.Evaluate(Start, branch: null, Workspace(), Options);

        plan.IsLocked.Should().BeTrue();
        plan.IsReadOnly.Should().BeTrue();
        plan.Features.Should().Be(PlanFeature.None);
    }

    [Fact]
    public void Branch_without_a_workspace_row_is_treated_as_a_trial()
    {
        var plan = PlanPolicy.Evaluate(Start.AddDays(1), Branch(Start.AddDays(30)), workspace: null, Options);

        plan.Kind.Should().Be(PlanKind.Trial);
        plan.WorkspaceNo.Should().BeNull();
        plan.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public void Converted_workspace_stays_readable_but_not_writable()
    {
        var plan = PlanPolicy.Evaluate(Start.AddDays(10), Branch(Start.AddDays(30)),
            Workspace(WorkspacePlanKind.Converted), Options);

        plan.IsReadOnly.Should().BeTrue();
        plan.IsLocked.Should().BeFalse();
        plan.Features.Should().Be(PlanFeature.All);
    }

    [Fact]
    public void Branch_package_dates_are_thai_local_time_and_round_trip_through_utc()
    {
        // ข้อมูลจริงของ db2: EndPackageDate 2028-01-01 00:00 = เที่ยงคืนเวลาไทย = 2027-12-31 17:00 UTC
        var local = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var utc = BranchPackageDates.ToUtc(local);

        utc.Should().Be(new DateTime(2027, 12, 31, 17, 0, 0, DateTimeKind.Utc));
        utc.Kind.Should().Be(DateTimeKind.Utc);
        BranchPackageDates.ToBranchLocal(utc).Should().Be(local);
        BranchPackageDates.ToBranchLocal(utc).Kind.Should().Be(DateTimeKind.Unspecified);
        BranchPackageDates.ToUtc((DateTime?)null).Should().BeNull();
    }
}
