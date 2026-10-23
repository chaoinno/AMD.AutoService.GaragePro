using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// การแจ้งชำระของลูกค้า (docs/17 §2, §5.1): ลำดับการตรวจ · ส่งซ้ำได้แถวเดิม · ซ่อนตัวเงินจาก role อื่น · ยกเลิก · เปิดสลิป
/// · ลิงก์สาธารณะตรวจลายเซ็นก่อนแตะฐาน · ทั้งหมดไม่ต่อฐาน/FTP/LINE
/// </summary>
public class PackagePaymentServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 7, 41, 0, DateTimeKind.Utc);   // 14:41 เวลาไทย
    private static readonly DateTime TrialEnd = new(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);   // 13/11 10:00 เวลาไทย
    private const int Branch = 61;
    private const string Key = "0123456789abcdef0123456789abcdef-test-key";

    private sealed class Harness
    {
        public PackageTestUser User { get; } = new();
        public PackageTestWorkspaces Workspaces { get; } = new();
        public FakeWorkspacePolicy Policy { get; } = new(FakeWorkspacePolicy.Trial(1042, TrialEnd));
        public InMemoryPackagePayments Payments { get; } = new();
        public FakeSlipStorage Slips { get; } = new();
        public FakePackageNotifier Notifier { get; } = new();
        public SelfServeOptions Options { get; } = new();
        public PackageTestClock Clock { get; } = new(Now);
        public SelfServeWorkspaceInfo Workspace { get; set; }

        public Harness(WorkspacePlanKind plan = WorkspacePlanKind.Trial, WorkspaceStatus status = WorkspaceStatus.Active)
        {
            Workspace = new SelfServeWorkspaceInfo(Guid.NewGuid(), 1042, Branch, "อู่ช่างเอก", "+66812345678", plan, status,
                Now.AddDays(-7), TrialEnd, null, null, null, null, Now.AddDays(-7), Guid.NewGuid(), "owner@example.com");
            Workspaces.Workspace = Workspace;
            Payments.Workspaces[Workspace.Id] = new PackagePaymentWorkspace(Workspace.Id, 1042, Workspace.Name, Branch,
                plan, status);
            Options.Package.PaymentAccount.AccountName = "บจก. อาร์มาดิลโล่ เทค";
            Options.Package.PaymentAccount.AccountNo = "123-4-56789-0";
            // ค่าเดียวกับ production (appsettings.json): 3,999/เดือน ก่อน VAT + VAT 7%
            Options.Package.MonthlyPrice = 3999;
            Options.Package.VatRate = 0.07m;
            Options.Package.SlipLinkKey = Key;
        }

        public PackagePaymentService Service => new(User, Workspaces, Policy, Payments, Slips, Notifier, Options, Clock);

        public void SetPlan(WorkspacePlanKind plan)
        {
            Workspace = Workspace with { Plan = plan };
            Workspaces.Workspace = Workspace;
        }
    }

    private static PackagePaymentSubmission Form(string? requestId = "req-1", string? paidAt = "2026-10-21T14:30:00+07:00",
        string? amount = "12,836.79", string? months = "3", string? note = "โอนจากบัญชีบริษัท", bool slip = true,
        string? contentType = "image/png", long size = 1000) =>
        new(requestId, paidAt, amount, months, note,
            slip ? new PackageSlipUpload(new MemoryStream([1, 2, 3]), contentType, size, "slip.png") : null);

    // ---------- overview ----------

    [Fact]
    public async Task Overview_shows_plan_previews_prices_account_and_that_the_manager_can_submit()
    {
        var h = new Harness();

        var o = (await h.Service.GetOverviewAsync()).Data!;

        o.WorkspaceNo.Should().Be("SP1042");
        o.Plan.Should().Be("trial");
        o.Status.Should().Be("active");
        o.EndsAt.Should().Be(TrialEnd);
        o.LastUsableDate.Should().Be(new DateOnly(2026, 11, 13));
        o.DaysLeft.Should().Be(PlanAccess.DaysLeft(TrialEnd, Now), "ค่าเดียวกับ /auth/me");
        o.ReadOnly.Should().BeFalse();
        o.MonthOptions.Select(m => m.Months).Should().Equal(1, 2, 3, 6, 12);
        o.VatRate.Should().Be(0.07m);
        var one = o.MonthOptions[0];
        one.Subtotal.Should().Be(3999.00m);
        one.Vat.Should().Be(279.93m);
        one.Price.Should().Be(4278.93m, "ยอดที่ต้องโอนรวม VAT");
        one.PreviewEndsAt.Should().Be(new DateTime(2026, 12, 13, 17, 0, 0, DateTimeKind.Utc), "ตัวอย่างใน docs/17 §5.1");
        one.PreviewLastUsableDate.Should().Be(new DateOnly(2026, 12, 13));
        o.MonthOptions.Select(m => m.Price).Should().Equal(4278.93m, 8557.86m, 12836.79m, 25673.58m, 51347.16m);
        o.PaymentAccount!.AccountName.Should().Be("บจก. อาร์มาดิลโล่ เทค");
        o.PaymentAccount.PromptPayId.Should().BeNull();
        o.Pending.Should().BeNull();
        o.CanSubmit.Should().BeTrue();
        o.CannotSubmitCode.Should().BeNull();
        o.PaidAtMaxPastDays.Should().Be(60);
    }

    /// <summary>ตัวเลือก JSON เดียวกับ Program.cs (camelCase · ฟิลด์ null ไม่อยู่ใน JSON)</summary>
    private static readonly JsonSerializerOptions ApiJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public async Task Overview_json_has_vat_rate_and_subtotal_vat_price_per_month_option()
    {
        var h = new Harness();

        var json = JsonSerializer.Serialize((await h.Service.GetOverviewAsync()).Data!, ApiJson);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("vatRate").GetRawText().Should().Be("0.07");
        const string expected =
            """{"months":1,"subtotal":3999.00,"vat":279.93,"price":4278.93,"previewEndsAt":"2026-12-13T17:00:00Z","previewLastUsableDate":"2026-12-13"}""";
        root.GetProperty("monthOptions")[0].GetRawText().Should().Be(expected);
        root.GetProperty("monthOptions")[4].GetProperty("price").GetRawText().Should().Be("51347.16");
        root.GetProperty("paymentAccount").GetProperty("accountNo").GetString().Should().Be("123-4-56789-0");
    }

    [Fact]
    public async Task Overview_json_without_pricing_has_no_price_fields_and_no_vat_rate()
    {
        var h = new Harness();
        h.Options.Package.MonthlyPrice = null;
        h.Options.Package.VatRate = null;

        var json = JsonSerializer.Serialize((await h.Service.GetOverviewAsync()).Data!, ApiJson);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.TryGetProperty("vatRate", out _).Should().BeFalse();
        var option = root.GetProperty("monthOptions")[0];
        option.TryGetProperty("subtotal", out _).Should().BeFalse();
        option.TryGetProperty("vat", out _).Should().BeFalse();
        option.TryGetProperty("price", out _).Should().BeFalse("ไม่อยู่ทั้งสามช่อง = ยังไม่ตั้งราคา");
        option.GetProperty("previewLastUsableDate").GetString().Should().Be("2026-12-13");
    }

    [Fact]
    public async Task Overview_without_vat_sends_vat_zero_and_price_equal_to_subtotal()
    {
        var h = new Harness();
        h.Options.Package.VatRate = null;

        var json = JsonSerializer.Serialize((await h.Service.GetOverviewAsync()).Data!, ApiJson);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.TryGetProperty("vatRate", out _).Should().BeFalse("ไม่อยู่ = ราคาไม่คิด VAT แยก");
        doc.RootElement.GetProperty("monthOptions")[0].GetRawText().Should().StartWith(
            """{"months":1,"subtotal":3999.00,"vat":0.00,"price":3999.00,""");
    }

    [Fact]
    public async Task Overview_explains_why_submitting_is_closed()
    {
        var h = new Harness();
        h.Payments.Seed(h.Workspace.Id);
        var pending = (await h.Service.GetOverviewAsync()).Data!;
        pending.CanSubmit.Should().BeFalse();
        pending.CannotSubmitCode.Should().Be("pending_exists");
        pending.CannotSubmitReasonTh.Should().Contain("รอตรวจ");
        pending.Pending!.Amount.Should().Be(590m);

        h.User.Role = UserRole.Technician;
        var technician = (await h.Service.GetOverviewAsync()).Data!;
        technician.CannotSubmitCode.Should().Be("not_manager");
        technician.Pending!.Status.Should().Be("pending");
        technician.Pending.Amount.Should().BeNull("role อื่นไม่เห็นตัวเงิน");
        technician.Pending.Note.Should().BeNull();
        technician.Pending.Slip.Should().BeNull();

        h.Options.Package.PaymentAccount.AccountNo = "";
        var unavailable = (await h.Service.GetOverviewAsync()).Data!;
        unavailable.CannotSubmitCode.Should().Be("unavailable");
        unavailable.PaymentAccount.Should().BeNull();

        h.SetPlan(WorkspacePlanKind.Converted);
        (await h.Service.GetOverviewAsync()).Data!.CannotSubmitCode.Should().Be("not_renewable");
    }

    [Fact]
    public async Task Customers_of_other_shards_get_not_applicable()
    {
        var h = new Harness();
        var service = new PackagePaymentService(new PackageTestUser(shard: "db2"), h.Workspaces, h.Policy, h.Payments,
            h.Slips, h.Notifier, h.Options, h.Clock);

        (await service.GetOverviewAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
        (await service.SubmitAsync(Form())).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
    }

    // ---------- submit ----------

    [Fact]
    public async Task Manager_submits_a_payment_with_slip_and_the_team_is_told_once()
    {
        var h = new Harness();

        var result = await h.Service.SubmitAsync(Form());

        result.Success.Should().BeTrue();
        var dto = result.Data!;
        dto.Status.Should().Be("pending");
        dto.MonthCount.Should().Be(3);
        dto.Amount.Should().Be(12836.79m);
        dto.ExpectedAmount.Should().Be(12836.79m, "ราคามาตรฐานตอนแจ้ง = ยอดรวม VAT (11,997.00 + 839.79)");
        dto.PaidAt.Should().Be(new DateTime(2026, 10, 21, 7, 30, 0, DateTimeKind.Utc));
        dto.SubmittedAt.Should().Be(Now);
        dto.SubmittedBy.Should().Be("SP1042.M01", "จาก JWT ไม่ใช่จากฟอร์ม");
        dto.Slip!.ContentType.Should().Be("image/jpeg");
        dto.ShortId.Should().Be(Guid.Parse(dto.Id).ToString("N")[..8]);

        h.Slips.Saved.Should().ContainSingle().Which.Should().Be(("sp1", Branch, Guid.Parse(dto.Id)));
        var row = h.Payments.Rows.Single();
        row.SlipSha256.Should().Be("bb");
        row.SubmittedByUserId.Should().Be(501);
        row.ExpectedAmount.Should().Be(12836.79m, "สำเนาราคารวม VAT ณ ตอนแจ้ง");
        h.Payments.Audits.Should().ContainSingle(a => a.Action == PackagePaymentAuditActions.Submit && a.Audit.Actor == "SP1042.M01");

        var notice = h.Notifier.Submitted.Should().ContainSingle().Subject;
        notice.Context.WorkspaceName.Should().Be("อู่ช่างเอก");
        notice.CurrentEndsAt.Should().Be(TrialEnd);
        notice.PreviewIfApprovedNow.LastUsableDate.Should().Be(new DateOnly(2027, 2, 13));
        notice.Duplicates.Should().BeEmpty();
        notice.Price.Should().Be(new PackagePrice(3, 11997.00m, 839.79m, 12836.79m, 0.07m));
    }

    [Fact]
    public async Task Price_changes_after_submitting_do_not_touch_the_snapshot_and_no_price_means_no_expected_amount()
    {
        var h = new Harness();
        var first = (await h.Service.SubmitAsync(Form())).Data!;
        h.Options.Package.MonthlyPrice = 4500;

        h.Payments.Rows.Single(r => r.Id == Guid.Parse(first.Id)).ExpectedAmount.Should().Be(12836.79m);

        var unpriced = new Harness();
        unpriced.Options.Package.MonthlyPrice = null;
        var dto = (await unpriced.Service.SubmitAsync(Form(amount: "1500"))).Data!;
        dto.ExpectedAmount.Should().BeNull("ยังไม่ตั้งราคา = ไม่มีราคาให้เทียบ");
        unpriced.Notifier.Submitted.Single().Price.Should().BeNull();
    }

    [Fact]
    public async Task Same_request_id_returns_the_same_row_without_uploading_or_notifying_again()
    {
        var h = new Harness();
        var first = (await h.Service.SubmitAsync(Form())).Data!;

        var second = await h.Service.SubmitAsync(Form(amount: "1"));

        second.Data!.Id.Should().Be(first.Id);
        second.Data.Amount.Should().Be(12836.79m, "แถวเดิม ไม่ใช่ค่าที่ส่งมาใหม่");
        h.Slips.Saved.Should().HaveCount(1);
        h.Notifier.Submitted.Should().HaveCount(1);
    }

    [Fact]
    public async Task Only_the_manager_can_submit()
    {
        var h = new Harness();
        h.User.Role = UserRole.Office;

        (await h.Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.ForbiddenCode);
        h.Slips.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Submitting_without_a_payment_account_is_unavailable()
    {
        var h = new Harness();
        h.Options.Package.PaymentAccount.AccountName = "";

        (await h.Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.UnavailableCode);
    }

    [Fact]
    public async Task Invalid_request_id_is_a_validation_error()
    {
        var h = new Harness();
        var error = (await h.Service.SubmitAsync(Form(requestId: " "))).Error!;
        error.Code.Should().Be(PackageErrors.ValidationCode);
        error.Field.Should().Be("requestId");
    }

    [Theory]
    [InlineData(WorkspacePlanKind.Converted)]
    [InlineData(WorkspacePlanKind.Suspended)]
    public async Task Converted_and_suspended_workspaces_cannot_renew(WorkspacePlanKind plan)
    {
        var h = new Harness();
        h.SetPlan(plan);

        (await h.Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.NotRenewableCode);
        h.Slips.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Paid_without_an_end_date_cannot_renew_but_paid_with_one_can()
    {
        var h = new Harness();
        h.SetPlan(WorkspacePlanKind.Paid);
        h.Policy.Plan = FakeWorkspacePolicy.Paid(1042, expiresAt: null);
        (await h.Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.NotRenewableCode);

        h.Policy.Plan = FakeWorkspacePolicy.Paid(1042, expiresAt: TrialEnd);
        (await h.Service.SubmitAsync(Form())).Success.Should().BeTrue();
    }

    [Theory]
    [InlineData("paidAt", "2569-10-21T14:30:00+07:00", "1500", "3", null)]
    [InlineData("paidAt", "2026-10-22T14:30:00+07:00", "1500", "3", null)]
    [InlineData("amount", "2026-10-21T14:30:00+07:00", "-1", "3", null)]
    [InlineData("monthCount", "2026-10-21T14:30:00+07:00", "1500", "4", null)]
    [InlineData("note", "2026-10-21T14:30:00+07:00", "1500", "3", "long")]
    public async Task Field_errors_carry_the_field_and_nothing_is_uploaded(string field, string paidAt, string amount,
        string months, string? note)
    {
        var h = new Harness();

        var error = (await h.Service.SubmitAsync(Form(paidAt: paidAt, amount: amount, months: months,
            note: note == "long" ? new string('ก', 501) : note))).Error!;

        error.Code.Should().Be(PackageErrors.ValidationCode);
        error.Field.Should().Be(field);
        h.Slips.Saved.Should().BeEmpty();
        h.Payments.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Slip_is_required_and_checked_before_upload()
    {
        var h = new Harness();
        (await h.Service.SubmitAsync(Form(slip: false))).Error!.Field.Should().Be("slip");
        (await h.Service.SubmitAsync(Form(size: 0))).Error!.Code.Should().Be(PackageErrors.AttachmentEmptyCode);
        (await h.Service.SubmitAsync(Form(contentType: "image/gif"))).Error!.Code
            .Should().Be(PackageErrors.AttachmentTypeInvalidCode);
        var tooLarge = (await h.Service.SubmitAsync(Form(size: 16 * 1024 * 1024))).Error!;
        tooLarge.Code.Should().Be(PackageErrors.AttachmentTooLargeCode);
        PackageErrors.StatusCodeFor(tooLarge.Code).Should().Be(413);
        h.Slips.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_pending_payment_is_rejected_before_uploading()
    {
        var h = new Harness();
        h.Payments.Seed(h.Workspace.Id);

        (await h.Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.PendingExistsCode);
        h.Slips.Saved.Should().BeEmpty();
    }

    [Fact]
    public async Task Content_that_is_not_a_slip_or_a_storage_failure_saves_nothing()
    {
        var h = new Harness();
        h.Slips.SaveThrows = new InvalidDataException("not an image");
        (await h.Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.AttachmentTypeInvalidCode);

        h.Slips.SaveThrows = new IOException("ftp down");
        var failed = (await h.Service.SubmitAsync(Form())).Error!;
        failed.Code.Should().Be(PackageErrors.SlipUploadFailedCode);
        PackageErrors.StatusCodeFor(failed.Code).Should().Be(503);

        h.Payments.Rows.Should().BeEmpty();
        h.Notifier.Submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Losing_the_insert_race_deletes_the_uploaded_file()
    {
        var h = new Harness();
        var other = new PackagePayment
        {
            WorkspaceId = h.Workspace.Id, RequestId = "other-tab", Status = PackagePaymentStatus.Pending, MonthCount = 1,
            Amount = 590, SlipPath = "x", SlipSha256 = "cc", SlipContentType = "image/jpeg", SubmittedByUserName = "x"
        };
        h.Payments.RaceOnInsert = other;

        var result = await h.Service.SubmitAsync(Form());

        result.Error!.Code.Should().Be(PackageErrors.PendingExistsCode);
        h.Slips.Deleted.Should().ContainSingle().Which.Should().Contain("/package-slip/");
        h.Notifier.Submitted.Should().BeEmpty();
    }

    [Fact]
    public async Task Same_request_racing_from_two_tabs_returns_the_row_and_deletes_the_second_upload()
    {
        var h = new Harness();
        h.Payments.RaceOnInsert = new PackagePayment
        {
            WorkspaceId = h.Workspace.Id, RequestId = "req-1", Status = PackagePaymentStatus.Pending, MonthCount = 3,
            Amount = 1770, SlipPath = "x", SlipSha256 = "cc", SlipContentType = "image/jpeg", SubmittedByUserName = "x"
        };

        var result = await h.Service.SubmitAsync(Form());

        result.Success.Should().BeTrue();
        result.Data!.Id.Should().Be(h.Payments.Rows.Single().Id.ToString());
        h.Slips.Deleted.Should().ContainSingle();
    }

    [Fact]
    public async Task Duplicate_slip_hashes_are_passed_to_the_team_notice()
    {
        var h = new Harness();
        var otherWorkspace = Guid.NewGuid();
        h.Payments.Workspaces[otherWorkspace] = new PackagePaymentWorkspace(otherWorkspace, 1041, "อู่อื่น", 60,
            WorkspacePlanKind.Paid, WorkspaceStatus.Active);
        h.Payments.Seed(otherWorkspace, PackagePaymentStatus.Approved, sha: "bb");

        await h.Service.SubmitAsync(Form());

        var duplicate = h.Notifier.Submitted.Single().Duplicates.Should().ContainSingle().Subject;
        duplicate.WorkspaceNo.Should().Be("SP1041");
        duplicate.Status.Should().Be("approved");
    }

    [Fact]
    public async Task Read_only_workspace_can_still_submit()
    {
        var h = new Harness(status: WorkspaceStatus.Expired);
        h.Policy.Plan = FakeWorkspacePolicy.Trial(1042, Now.AddDays(-3), readOnly: true);

        (await h.Service.SubmitAsync(Form())).Success.Should().BeTrue("กฎข้อ 11");
    }

    // ---------- cancel / list / slip ----------

    [Fact]
    public async Task Manager_cancels_a_pending_payment()
    {
        var h = new Harness();
        var p = h.Payments.Seed(h.Workspace.Id);

        var result = await h.Service.CancelAsync(p.Id.ToString());

        result.Data!.Status.Should().Be("cancelled");
        result.Data.CancelledAt.Should().Be(Now);
        h.Notifier.Cancelled.Should().ContainSingle().Which.Payment.CancelledBy.Should().Be("SP1042.M01");
        (await h.Service.CancelAsync(p.Id.ToString())).Error!.Code.Should().Be(PackageErrors.NotPendingCode);
    }

    [Fact]
    public async Task Cancel_refuses_other_workspaces_bad_ids_and_non_managers()
    {
        var h = new Harness();
        var foreign = h.Payments.Seed(Guid.NewGuid());
        h.Payments.Workspaces[foreign.WorkspaceId] = new PackagePaymentWorkspace(foreign.WorkspaceId, 1050, "x", 70,
            WorkspacePlanKind.Trial, WorkspaceStatus.Active);

        (await h.Service.CancelAsync(foreign.Id.ToString())).Error!.Code.Should().Be(PackageErrors.NotFoundCode);
        (await h.Service.CancelAsync("not-a-guid")).Error!.Code.Should().Be(PackageErrors.NotFoundCode);

        var mine = h.Payments.Seed(h.Workspace.Id);
        h.User.Role = UserRole.FrontDesk;
        (await h.Service.CancelAsync(mine.Id.ToString())).Error!.Code.Should().Be(PackageErrors.ForbiddenCode);
    }

    [Fact]
    public async Task History_is_newest_first_and_hides_money_from_other_roles()
    {
        var h = new Harness();
        h.Payments.Seed(h.Workspace.Id, PackagePaymentStatus.Rejected, submittedAt: Now.AddDays(-2));
        var newest = h.Payments.Seed(h.Workspace.Id, PackagePaymentStatus.Approved, submittedAt: Now.AddDays(-1));
        newest.PeriodEndsAt = new DateTime(2027, 2, 13, 17, 0, 0, DateTimeKind.Utc);
        h.Payments.Rows[0].RejectReason = PackagePaymentRejectReason.AmountMismatch;
        h.Payments.Rows[0].ReviewNote = "โอนขาด 100 บาท";

        var asManager = (await h.Service.ListPaymentsAsync(null)).Data!;
        asManager.Select(p => p.Status).Should().Equal("approved", "rejected");
        asManager[0].PeriodLastUsableDate.Should().Be(new DateOnly(2027, 2, 13));
        asManager[0].RejectReason.Should().BeNull("เฉพาะ rejected");
        asManager[1].RejectReason.Should().Be("amount_mismatch");
        asManager[1].RejectReasonTh.Should().Be("ยอดเงินไม่ตรงกับแพ็กเกจ");
        asManager[1].RejectNote.Should().Be("โอนขาด 100 บาท");
        asManager[1].PeriodEndsAt.Should().BeNull();

        h.User.Role = UserRole.Technician;
        var asTechnician = (await h.Service.ListPaymentsAsync(1)).Data!;
        asTechnician.Should().ContainSingle().Which.Amount.Should().BeNull();
    }

    [Fact]
    public async Task Slip_file_opens_only_for_the_manager_of_that_workspace()
    {
        var h = new Harness();
        var stored = (await h.Service.SubmitAsync(Form())).Data!;

        var file = (await h.Service.OpenSlipAsync(stored.Id.ToString())).Data!;
        file.ContentType.Should().Be("image/jpeg");
        file.FileName.Should().Be($"slip-{stored.ShortId}.jpg");

        h.User.Role = UserRole.Cashier;
        (await h.Service.OpenSlipAsync(stored.Id.ToString())).Error!.Code.Should().Be(PackageErrors.ForbiddenCode);
    }

    [Fact]
    public async Task Signed_link_opens_the_slip_and_is_checked_before_touching_the_database()
    {
        var h = new Harness();
        var stored = (await h.Service.SubmitAsync(Form())).Data!;
        var link = PackageSlipLinks.Create(h.Options.Package, Guid.Parse(stored.Id), Now)!;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);

        (await h.Service.OpenSignedSlipAsync(Guid.Parse(stored.Id).ToString("N"), query["exp"], query["sig"])).Should().NotBeNull();

        var reads = h.Payments.Reads;
        (await h.Service.OpenSignedSlipAsync(Guid.Parse(stored.Id).ToString("N"), query["exp"], "AAAA")).Should().BeNull();
        (await h.Service.OpenSignedSlipAsync(Guid.Parse(stored.Id).ToString("N"), "abc", query["sig"])).Should().BeNull();
        (await h.Service.OpenSignedSlipAsync("nope", query["exp"], query["sig"])).Should().BeNull();
        h.Payments.Reads.Should().Be(reads, "ลายเซ็นผิด = ไม่แตะฐาน");

        h.Clock.Now = Now.AddDays(8);
        (await h.Service.OpenSignedSlipAsync(Guid.Parse(stored.Id).ToString("N"), query["exp"], query["sig"])).Should().BeNull("หมดอายุ");
    }

    [Fact]
    public async Task Signed_link_of_a_purged_workspace_or_without_a_key_is_dead()
    {
        var h = new Harness();
        var stored = (await h.Service.SubmitAsync(Form())).Data!;
        var link = PackageSlipLinks.Create(h.Options.Package, Guid.Parse(stored.Id), Now)!;
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);

        h.Payments.Workspaces[h.Workspace.Id] = h.Payments.Workspaces[h.Workspace.Id] with { Status = WorkspaceStatus.Purged };
        (await h.Service.OpenSignedSlipAsync(Guid.Parse(stored.Id).ToString("N"), query["exp"], query["sig"])).Should().BeNull();

        h.Payments.Workspaces[h.Workspace.Id] = h.Payments.Workspaces[h.Workspace.Id] with { Status = WorkspaceStatus.Active };
        h.Options.Package.SlipLinkKey = "";
        (await h.Service.OpenSignedSlipAsync(Guid.Parse(stored.Id).ToString("N"), query["exp"], query["sig"])).Should().BeNull();
    }
}
