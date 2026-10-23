using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// งานของทีมงานกับการแจ้งชำระ (docs/17 §4, §7): อนุมัติต่อวันถูกสูตร · --until · ยอดไม่ตรงต้องมีหมายเหตุ · ปฏิเสธ ·
/// การอ้างรายการด้วย shortId · ค่าที่ส่งให้ repository (วันหมดดิบไว้กันชน) · ผลการแจ้งเจ้าของอู่
/// </summary>
public class PackagePaymentAdminServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 2, 0, 0, DateTimeKind.Utc);   // 09:00 เวลาไทย 21/10
    private const int Branch = 61;
    private const string Actor = "spadmin:noi";

    private static DateTime ThaiMidnight(int y, int m, int d) => new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);

    private sealed class Harness
    {
        public InMemoryPackagePayments Payments { get; } = new();
        public FakePackageAdminRepository Admin { get; }
        public FakePackageNotifier Notifier { get; } = new();
        public SelfServeOptions Options { get; } = new();
        public PackageTestClock Clock { get; } = new(Now);
        public Workspace Workspace { get; }

        public Harness(DateTime? branchEnd, WorkspacePlanKind plan = WorkspacePlanKind.Trial,
            WorkspaceStatus status = WorkspaceStatus.Active, DateTime? trialCopy = null)
        {
            Admin = new FakePackageAdminRepository(Payments);
            Workspace = new Workspace
            {
                WorkspaceNo = 1042, BranchId = Branch, Name = "อู่ช่างเอก", Phone = "+66812345678", Plan = plan, Status = status,
                TrialEndsAt = trialCopy ?? branchEnd, CreatedAt = Now.AddDays(-20), PurgeAfter = Now.AddDays(80)
            };
            Admin.Snapshots[1042] = new AdminWorkspaceSnapshot(Workspace,
                new AdminAccountInfo(Guid.NewGuid(), "owner@example.com", "owner@example.com",
                    SelfServeAccountProvider.Email, SelfServeAccountStatus.Active, "SP1042.M01", 501, Now, Now),
                new BranchPackage(Branch, 1, Now.AddDays(-20), 30, branchEnd), []);
            Payments.Workspaces[Workspace.Id] = new PackagePaymentWorkspace(Workspace.Id, 1042, Workspace.Name, Branch,
                plan, status);
        }

        public PackagePaymentAdminService Service => new(Payments, Admin, Notifier, Options, Clock);

        public PackagePayment Seed(decimal amount = 1770, decimal? expected = 1770, int months = 3,
            PackagePaymentStatus status = PackagePaymentStatus.Pending) =>
            Payments.Seed(Workspace.Id, status, amount: amount, expected: expected, months: months);
    }

    private static string Ref(PackagePayment p) => p.Id.ToString("N")[..8];

    // ---------- อนุมัติ ----------

    [Fact]
    public async Task Approving_before_expiry_extends_from_the_current_end_and_converts_the_trial_to_paid()
    {
        var trialEnd = new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);   // 13/11 10:00
        var h = new Harness(trialEnd);
        var p = h.Seed();

        var result = await h.Service.ApproveAsync(Ref(p), null, null, Actor);

        result.Success.Should().BeTrue();
        var (change, approval) = h.Admin.Approvals.Single();
        change.EndsAtUtc.Should().Be(ThaiMidnight(2027, 2, 14), "ปัดขึ้นเป็น 14/11 00:00 แล้ว + 3 เดือน");
        change.SetBranchEnd.Should().BeTrue();
        change.Plan.Should().Be(WorkspacePlanKind.Paid);
        change.Status.Should().Be(WorkspaceStatus.Active);
        change.ClearPurgeAfter.Should().BeTrue();
        change.AuditAction.Should().Be(PackagePaymentAuditActions.Approve);
        change.Actor.Should().Be(Actor);
        change.PayloadJson.Should().Contain(p.Id.ToString());
        approval.ExpectedBranchEndUtc.Should().Be(trialEnd, "ค่าดิบของ Branch ไว้กันชนกับ extend ที่รันพร้อมกัน");
        approval.PrevEndsAt.Should().Be(trialEnd);
        approval.PeriodStartsAt.Should().Be(ThaiMidnight(2026, 11, 14));
        approval.PeriodEndsAt.Should().Be(change.EndsAtUtc!.Value);
        approval.ReviewedBy.Should().Be(Actor);
        approval.ReviewedAt.Should().Be(Now);

        var r = result.Data!;
        r.EndsFrom.Should().Be(trialEnd);
        r.EndsTo.Should().Be(ThaiMidnight(2027, 2, 14));
        r.Payment.Payment.Status.Should().Be("approved");
        r.Payment.Payment.PeriodLastUsableDate.Should().Be(new DateOnly(2027, 2, 13));
        r.EmailSent.Should().BeTrue();
        r.InAppSaved.Should().BeTrue();

        var notice = h.Notifier.Reviewed.Single();
        notice.EndsTo.Should().Be(ThaiMidnight(2027, 2, 14));
        notice.Context.OwnerEmail.Should().Be("owner@example.com");
        notice.Context.BranchId.Should().Be(Branch);
    }

    [Fact]
    public async Task Approving_after_expiry_counts_from_today_and_reactivates()
    {
        var h = new Harness(ThaiMidnight(2026, 9, 1), status: WorkspaceStatus.Expired);
        var p = h.Seed(months: 1, amount: 590, expected: 590);

        await h.Service.ApproveAsync(Ref(p), null, null, Actor);

        var (change, approval) = h.Admin.Approvals.Single();
        approval.PeriodStartsAt.Should().Be(ThaiMidnight(2026, 10, 21));
        change.EndsAtUtc.Should().Be(ThaiMidnight(2026, 11, 21));
        change.Status.Should().Be(WorkspaceStatus.Active);
    }

    [Fact]
    public async Task Renewing_a_paid_workspace_keeps_it_paid()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 1), WorkspacePlanKind.Paid);
        var p = h.Seed(months: 12, amount: 5900, expected: 5900);

        await h.Service.ApproveAsync(Ref(p), null, null, Actor);

        h.Admin.Approvals.Single().Change.EndsAtUtc.Should().Be(ThaiMidnight(2027, 11, 1));
    }

    [Fact]
    public async Task Until_overrides_the_end_but_must_not_start_before_the_base_or_exceed_the_cap()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var p = h.Seed();

        var early = await h.Service.ApproveAsync(Ref(p), new DateOnly(2026, 11, 12), null, Actor);
        early.Error!.Field.Should().Be("until");
        early.Error.MessageTh.Should().Contain("2026-11-14");

        var far = await h.Service.ApproveAsync(Ref(p), new DateOnly(2040, 1, 1), null, Actor);
        far.Error!.Field.Should().Be("until");
        h.Admin.Approvals.Should().BeEmpty();

        var ok = await h.Service.ApproveAsync(Ref(p), new DateOnly(2027, 3, 31), null, Actor);
        ok.Success.Should().BeTrue();
        h.Admin.Approvals.Single().Change.EndsAtUtc.Should().Be(ThaiMidnight(2027, 4, 1), "ใช้ได้ถึงสิ้นวัน 31/3");
        h.Admin.Approvals.Single().Approval.PeriodStartsAt.Should().Be(ThaiMidnight(2026, 11, 14));
    }

    [Fact]
    public async Task Amount_that_does_not_match_the_price_needs_a_note()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var p = h.Seed(amount: 1500, expected: 1770);

        var refused = await h.Service.ApproveAsync(Ref(p), null, "  ", Actor);
        refused.Error!.Code.Should().Be(PackageErrors.ValidationCode);
        refused.Error.Field.Should().Be("note");

        (await h.Service.ApproveAsync(Ref(p), null, "ส่วนลดเปิดตัว", Actor)).Success.Should().BeTrue();
        h.Admin.Approvals.Single().Approval.ReviewNote.Should().Be("ส่วนลดเปิดตัว");
    }

    [Fact]
    public async Task No_standard_price_means_no_mismatch_check()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var p = h.Seed(amount: 1234, expected: null);

        (await h.Service.ApproveAsync(Ref(p), null, null, Actor)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Payment_that_is_no_longer_pending_is_refused_both_before_and_inside_the_transaction()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var done = h.Seed(status: PackagePaymentStatus.Approved);
        (await h.Service.ApproveAsync(Ref(done), null, null, Actor)).Error!.Code.Should().Be(PackageErrors.NotPendingCode);
        h.Admin.Approvals.Should().BeEmpty();

        var raced = h.Seed();
        h.Admin.NextOutcome = ApprovePaymentOutcome.PaymentNotPending;
        (await h.Service.ApproveAsync(Ref(raced), null, null, Actor)).Error!.Code.Should().Be(PackageErrors.NotPendingCode);
        h.Notifier.Reviewed.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_change_of_the_end_date_is_a_conflict()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var p = h.Seed();
        h.Admin.NextOutcome = ApprovePaymentOutcome.BranchEndChanged;

        var result = await h.Service.ApproveAsync(Ref(p), null, null, Actor);

        result.Error!.Code.Should().Be(PackageErrors.ApprovalConflictCode);
        PackageErrors.StatusCodeFor(result.Error.Code).Should().Be(409);
        h.Notifier.Reviewed.Should().BeEmpty();
    }

    [Theory]
    [InlineData(WorkspacePlanKind.Converted, WorkspaceStatus.Active, PackageErrors.NotRenewableCode)]
    [InlineData(WorkspacePlanKind.Suspended, WorkspaceStatus.Active, PackageErrors.NotRenewableCode)]
    [InlineData(WorkspacePlanKind.Trial, WorkspaceStatus.Provisioning, SelfServeAdminErrors.RefusedCode)]
    [InlineData(WorkspacePlanKind.Trial, WorkspaceStatus.Purged, SelfServeAdminErrors.RefusedCode)]
    public async Task Workspaces_that_cannot_change_package_are_refused(WorkspacePlanKind plan, WorkspaceStatus status,
        string code)
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14), plan, status);
        var p = h.Seed();

        (await h.Service.ApproveAsync(Ref(p), null, null, Actor)).Error!.Code.Should().Be(code);
        h.Admin.Approvals.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_ended_paid_and_wrong_database_settings_are_refused()
    {
        var openEnded = new Harness(branchEnd: null, WorkspacePlanKind.Paid);
        var p = openEnded.Seed();
        (await openEnded.Service.ApproveAsync(Ref(p), null, null, Actor)).Error!.Code
            .Should().Be(PackageErrors.NotRenewableCode);

        var mismatch = new Harness(ThaiMidnight(2026, 11, 14));
        mismatch.Admin.Mismatch = "SelfServe:ConnectionString ชี้ฐานเดียวกับ GarageService";
        var q = mismatch.Seed();
        var refused = await mismatch.Service.ApproveAsync(Ref(q), null, null, Actor);
        refused.Error!.Code.Should().Be(SelfServeAdminErrors.RefusedCode);
        refused.Error.MessageTh.Should().Contain("ไม่เขียนฐาน");
        mismatch.Admin.Approvals.Should().BeEmpty();
    }

    [Fact]
    public async Task Missing_branch_end_uses_the_trial_copy_but_guards_on_the_raw_null()
    {
        var copy = new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);
        var h = new Harness(branchEnd: null, trialCopy: copy);
        var p = h.Seed(months: 1, amount: 590, expected: 590);

        await h.Service.ApproveAsync(Ref(p), null, null, Actor);

        var (change, approval) = h.Admin.Approvals.Single();
        approval.ExpectedBranchEndUtc.Should().BeNull("เทียบกับค่าดิบใน Branch ไม่ใช่สำเนา");
        approval.PrevEndsAt.Should().Be(copy);
        change.EndsAtUtc.Should().Be(ThaiMidnight(2026, 12, 14));
    }

    [Fact]
    public async Task Notification_failures_are_reported_not_thrown()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var p = h.Seed();
        h.Notifier.Outcome = new PackageReviewNotifyOutcome(false, false);

        var result = await h.Service.ApproveAsync(Ref(p), null, null, Actor);

        result.Success.Should().BeTrue("อนุมัติบันทึกแล้วก่อนแจ้ง");
        result.Data!.EmailSent.Should().BeFalse();
        result.Data.InAppSaved.Should().BeFalse();
    }

    // ---------- ปฏิเสธ ----------

    [Fact]
    public async Task Rejecting_records_the_reason_and_tells_the_owner()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var p = h.Seed();

        var result = await h.Service.RejectAsync(Ref(p), "NOT_RECEIVED", "ยังไม่เห็นยอดเข้า", Actor);

        result.Success.Should().BeTrue();
        result.Data!.Payment.Payment.Status.Should().Be("rejected");
        result.Data.Payment.Payment.RejectReason.Should().Be("not_received");
        result.Data.Payment.Payment.RejectNote.Should().Be("ยังไม่เห็นยอดเข้า");
        result.Data.EndsTo.Should().BeNull();
        h.Payments.Audits.Should().ContainSingle(a => a.Action == PackagePaymentAuditActions.Reject && a.Audit.Actor == Actor);
        h.Notifier.Reviewed.Single().Context.Payment.Status.Should().Be(PackagePaymentStatus.Rejected);
        h.Admin.Approvals.Should().BeEmpty("ปฏิเสธไม่แตะวันหมด");
    }

    [Fact]
    public async Task Reject_needs_a_known_reason_and_other_needs_a_note()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var p = h.Seed();

        (await h.Service.RejectAsync(Ref(p), "fraud", null, Actor)).Error!.Field.Should().Be("reason");
        (await h.Service.RejectAsync(Ref(p), "other", " ", Actor)).Error!.Field.Should().Be("note");
        (await h.Service.RejectAsync(Ref(p), "other", "ชื่อบัญชีไม่ตรง", Actor)).Success.Should().BeTrue();
        (await h.Service.RejectAsync(Ref(p), "duplicate", null, Actor)).Error!.Code
            .Should().Be(PackageErrors.NotPendingCode, "ปฏิเสธซ้ำไม่ได้");
    }

    [Fact]
    public async Task Reject_that_loses_the_race_is_not_pending()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var p = h.Seed();
        h.Payments.RejectFails = true;

        (await h.Service.RejectAsync(Ref(p), "duplicate", null, Actor)).Error!.Code.Should().Be(PackageErrors.NotPendingCode);
        h.Notifier.Reviewed.Should().BeEmpty();
    }

    // ---------- อ้างรายการ · รายการ ----------

    [Fact]
    public async Task Reference_must_be_long_enough_unique_and_existing()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        var a = h.Payments.Seed(h.Workspace.Id, id: Guid.Parse("3f2a9c1b-0000-4000-8000-000000000001"));
        h.Payments.Seed(h.Workspace.Id, PackagePaymentStatus.Rejected, id: Guid.Parse("3f2a9c1b-0000-4000-8000-000000000002"));

        (await h.Service.ShowAsync("3f2a9c1")).Error!.Field.Should().Be("id");
        var ambiguous = (await h.Service.ShowAsync("3f2a9c1b")).Error!;
        ambiguous.Field.Should().Be("id");
        ambiguous.MessageTh.Should().Contain("ตรงหลายรายการ").And.Contain("3f2a9c1b-0000-4000-8000-000000000001");
        (await h.Service.ShowAsync("deadbeef")).Error!.Code.Should().Be(PackageErrors.NotFoundCode);

        var exact = await h.Service.ShowAsync(a.Id.ToString());
        exact.Data!.Item.Payment.Id.Should().Be(a.Id.ToString());
    }

    [Fact]
    public async Task Show_includes_duplicates_preview_and_a_fresh_slip_link()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        h.Options.Package.SlipLinkKey = "0123456789abcdef0123456789abcdef-test-key";
        var p = h.Payments.Seed(h.Workspace.Id, sha: "same");
        h.Payments.Seed(h.Workspace.Id, PackagePaymentStatus.Rejected, sha: "same");

        var d = (await h.Service.ShowAsync(Ref(p))).Data!;

        d.Item.Duplicates.Should().ContainSingle().Which.Status.Should().Be("rejected");
        d.Item.Preview!.BaseAt.Should().Be(ThaiMidnight(2026, 11, 14));
        d.Item.Preview.LastUsableDate.Should().Be(new DateOnly(2026, 12, 13));
        d.Item.Payment.Amount.Should().Be(590m, "ทีมงานเห็นตัวเงินครบ");
        d.Item.CurrentEndsAt.Should().Be(ThaiMidnight(2026, 11, 14));
        d.SlipLink.Should().Contain("/api/v1/public/package-slips/");
    }

    [Fact]
    public async Task List_defaults_to_the_requested_status_and_previews_only_pending_items()
    {
        var h = new Harness(ThaiMidnight(2026, 11, 14));
        h.Payments.Seed(h.Workspace.Id, PackagePaymentStatus.Pending);
        h.Payments.Seed(h.Workspace.Id, PackagePaymentStatus.Approved);

        var pending = (await h.Service.ListAsync(PackagePaymentStatus.Pending, null)).Data!;
        pending.Should().ContainSingle().Which.Preview.Should().NotBeNull();

        var all = (await h.Service.ListAsync(null, 1042)).Data!;
        all.Should().HaveCount(2);
        all.Single(i => i.Payment.Status == "approved").Preview.Should().BeNull();
        (await h.Service.ListAsync(null, 9999)).Data!.Should().BeEmpty();
    }
}
