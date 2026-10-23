using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// flow garagepro (docs/17 §8.7.1): เลือก flow ตาม shard/กลุ่ม · overview/รายการแปลงจากข้อมูลเดิม (สถานะ · เวลาไทย · สูตรพรีวิว)
/// · แจ้งชำระส่งต่อให้ API เดิม · กันกดซ้ำ · รายการรอตรวจกันซ้อน · ไม่ทราบผลแล้วลองใหม่ · ไม่ได้ตั้ง BaseUrl
/// · ไม่ต่อฐาน/ไม่ยิง API จริง
/// </summary>
public class GarageProPackageServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 7, 41, 0, DateTimeKind.Utc);   // 14:41 เวลาไทย 21/10
    private const int Branch = 227;

    /// <summary>เวลาไทย (ไม่มีโซน) → UTC</summary>
    private static DateTime Thai(int y, int m, int d, int h = 0, int min = 0) => new DateTime(y, m, d, h, min, 0, DateTimeKind.Utc).AddHours(-7);

    private sealed class FakeReader : IGarageProPackageReader
    {
        public GarageProBranchPackage? Branch { get; set; } =
            new(GarageProPackageServiceTests.Branch, "Service Center Demo", 1, 7, Thai(2026, 11, 3));

        public List<GarageProPaymentRow> Rows { get; } = [];
        public int Reads { get; private set; }

        public Task<GarageProBranchPackage?> GetBranchAsync(string shardKey, int branchId, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(Branch?.BranchId == branchId ? Branch : null);
        }

        public Task<IReadOnlyList<GarageProPaymentRow>> ListPaymentsAsync(string shardKey, int branchId, int take,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GarageProPaymentRow>>(Rows.OrderByDescending(r => r.CreatedAtUtc).Take(take).ToList());

        public Task<GarageProPaymentRow?> FindPendingAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult(Rows.Where(r => r.Status == 1).OrderByDescending(r => r.CreatedAtUtc).FirstOrDefault());

        public Task<GarageProPaymentRow?> GetPaymentAsync(string shardKey, int branchId, long id,
            CancellationToken ct = default) =>
            Task.FromResult(Rows.FirstOrDefault(r => r.Id == id));
    }

    private sealed class FakeGateway(FakeReader reader) : IGarageProPaymentGateway
    {
        public bool IsConfigured { get; set; } = true;
        public List<GarageProAddPaymentRequest> Requests { get; } = [];
        public GarageProAddPaymentResult Next { get; set; } = new(GarageProAddPaymentOutcome.Accepted, "", 146);

        /// <summary>จำลองระบบเดิมบันทึกแถวจริงเมื่อรับ (หรือบันทึกแล้วแต่ตอบไม่ทัน)</summary>
        public bool CreatesRow { get; set; } = true;

        public Task<GarageProAddPaymentResult> AddPaymentAsync(GarageProAddPaymentRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            if (CreatesRow && Next.Outcome != GarageProAddPaymentOutcome.Rejected)
                reader.Rows.Add(new GarageProPaymentRow(Next.PaymentId ?? 146, 1, BranchPackageDates.ToUtc(request.PaidAtThai),
                    Now, "service", Thai(2027, 2, 3), "Images/PaySlip/x.jpg"));
            return Task.FromResult(Next);
        }
    }

    private sealed class FakeNormalizer : IPackageSlipNormalizer
    {
        public long MaxSizeBytes => 15 * 1024 * 1024;
        public bool Invalid { get; set; }

        public AttachmentValidation Validate(string? contentType, long sizeBytes) =>
            sizeBytes <= 0 ? new(false, PackageErrors.AttachmentEmptyCode, "ว่าง")
            : contentType == "image/gif" ? new(false, PackageErrors.AttachmentTypeInvalidCode, "ชนิด")
            : new(true, null, null);

        public Task<PreparedSlip> NormalizeAsync(Stream content, CancellationToken ct = default) =>
            Invalid
                ? throw new InvalidDataException("ไม่ใช่รูป")
                : Task.FromResult(new PreparedSlip([9, 9, 9], "image/jpeg", ".jpg"));
    }

    private sealed class Harness
    {
        public FakeReader Reader { get; } = new();
        public FakeGateway Gateway { get; }
        public FakeNormalizer Slips { get; } = new();
        public GarageProSubmissionMemo Memo { get; } = new(new MemoryCache(new MemoryCacheOptions()));
        public SelfServeOptions Options { get; } = new();
        public PackageTestUser User { get; }
        public PackageTestClock Clock { get; } = new(Now);

        public Harness(UserRole role = UserRole.Manager, string shard = "db2")
        {
            Gateway = new FakeGateway(Reader);
            User = new PackageTestUser(role, shard, Branch);
            Options.Package.MonthlyPrice = 3999m;
            Options.Package.VatRate = 0.07m;
            Options.Package.PaymentAccount = new SelfServeOptions.PaymentAccountOptions
            {
                BankName = "ธนาคารตัวอย่าง", AccountNo = "000-0-00000-0", AccountName = "บริษัทตัวอย่าง"
            };
            Options.Package.GarageProApi.BaseUrl = "https://legacy-api.example.test";
            Options.Package.GarageProApi.SlipBaseUrl = "https://legacy-files.example.test/";
        }

        public GarageProPackageService Service =>
            new(User, Reader, Gateway, Slips, Memo, Options, Clock);
    }

    private static PackagePaymentSubmission Form(string requestId = "req-1", string paidAt = "2026-10-21T14:30:00+07:00",
        string months = "3", PackageSlipUpload? slip = null, string? amount = null) =>
        new(requestId, paidAt, amount, months, "ไม่ส่งไป", slip ?? new PackageSlipUpload(new MemoryStream([1, 2, 3]), "image/png", 3, "a.png"));

    // ---------- เลือก flow ----------

    [Theory]
    [InlineData("sp1", "selfserve")]
    [InlineData("SP1", "selfserve")]
    [InlineData("db2", "garagepro")]
    [InlineData("db1", "garagepro")]
    [InlineData("db9", null)]
    public void Router_picks_the_flow_by_shard(string shard, string? expected)
    {
        var options = new SelfServeOptions();
        var selfServe = new MarkerService("selfserve");
        var garagePro = new MarkerService("garagepro");
        var router = new PackagePaymentRouter(new PackageTestUser(shard: shard), options, selfServe, garagePro);

        (router.Target as MarkerService)?.Name.Should().Be(expected);
        if (expected is null) router.Target.Should().BeNull();
    }

    [Fact]
    public async Task Router_without_a_target_is_not_applicable_and_public_slip_links_belong_to_sp1_only()
    {
        var router = new PackagePaymentRouter(new PackageTestUser(shard: "db2"), new SelfServeOptions(), null, null);
        (await router.GetOverviewAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
        (await router.SubmitAsync(Form())).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);

        var garagePro = new MarkerService("garagepro");
        var withGaragePro = new PackagePaymentRouter(new PackageTestUser(shard: "db2"), new SelfServeOptions(), null, garagePro);
        (await withGaragePro.OpenSignedSlipAsync("x", "1", "s")).Should().BeNull("ลิงก์สาธารณะไม่มีผู้ใช้ ใช้ได้แค่สลิปของ sp1");
        garagePro.SignedCalls.Should().Be(0);
    }

    [Fact]
    public async Task Configured_zones_replace_the_default_shard_map()
    {
        var options = new SelfServeOptions();
        options.Package.GarageProApi.Zones["db2"] = 2;
        options.Package.GarageProApi.ZoneFor("db1").Should().BeNull("ตั้ง Zones แล้ว = ใช้เฉพาะที่ตั้ง");
        options.Package.GarageProApi.ZoneFor("DB2").Should().Be(2);
        new SelfServeOptions().Package.GarageProApi.ZoneFor("db1").Should().Be(1);

        var h = new Harness(shard: "db1");
        h.Options.Package.GarageProApi.Zones["db2"] = 2;
        (await h.Service.GetOverviewAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
    }

    [Fact]
    public async Task Branches_outside_the_service_group_are_not_applicable()
    {
        var h = new Harness();
        h.Reader.Branch = h.Reader.Branch! with { BranchGroupId = 1 };

        (await h.Service.GetOverviewAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
        (await h.Service.ListPaymentsAsync(null)).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
        (await h.Service.SubmitAsync(Form())).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
        h.Gateway.Requests.Should().BeEmpty();

        h.Reader.Branch = null;
        (await h.Service.GetOverviewAsync()).Error!.Code.Should().Be(SelfServeErrors.NotApplicableCode);
    }

    // ---------- overview ----------

    [Fact]
    public async Task Overview_uses_the_legacy_branch_dates_and_the_legacy_preview_formula()
    {
        var h = new Harness();

        var o = (await h.Service.GetOverviewAsync()).Data!;

        o.Flow.Should().Be("garagepro");
        o.Plan.Should().Be("legacy");
        o.Status.Should().Be("active");
        o.CanCancel.Should().BeFalse();
        o.AmountRequired.Should().BeFalse();
        o.WorkspaceNo.Should().Be("227");
        o.ReadOnly.Should().BeFalse("ServicePro ไม่ล็อกสาขา db2 ตามวันหมด");
        o.EndsAt.Should().Be(Thai(2026, 11, 3));
        o.LastUsableDate.Should().Be(new DateOnly(2026, 11, 2), "เที่ยงคืนเริ่มวันที่หมดสิทธิ์ · ใช้ได้ถึงวันก่อนหน้า");
        o.DaysLeft.Should().Be(12);
        o.CanSubmit.Should().BeTrue();
        o.VatRate.Should().Be(0.07m);
        o.PaymentAccount!.AccountName.Should().Be("บริษัทตัวอย่าง");

        var one = o.MonthOptions.Single(m => m.Months == 1);
        one.PreviewEndsAt.Should().Be(Thai(2026, 12, 3), "สูตรเดิม = EndPackageDate + N เดือน");
        one.PreviewLastUsableDate.Should().Be(new DateOnly(2026, 12, 2));
        one.Price.Should().Be(4278.93m);
        o.MonthOptions.Single(m => m.Months == 3).PreviewEndsAt.Should().Be(Thai(2027, 2, 3));
    }

    [Fact]
    public void Legacy_preview_keeps_the_time_of_day_clamps_month_ends_and_continues_from_an_old_end()
    {
        GarageProPackageService.PreviewEnd(Thai(2027, 1, 31, 10), Now, 1).Should().Be(Thai(2027, 2, 28, 10));
        GarageProPackageService.PreviewEnd(Thai(2026, 9, 1), Now, 1)
            .Should().Be(Thai(2026, 10, 1), "ระบบเดิมต่อจากวันหมดเดิมแม้หมดไปแล้ว (ต่างจาก sp1)");
        GarageProPackageService.PreviewEnd(null, Now, 2).Should().Be(Thai(2026, 12, 21), "ไม่มีวันหมด = เที่ยงคืนวันนี้ + N");
    }

    [Fact]
    public async Task Overview_explains_why_submitting_is_not_possible()
    {
        var off = new Harness();
        off.Options.Package.GarageProApi.BaseUrl = "";
        off.Gateway.IsConfigured = false;
        var unavailable = (await off.Service.GetOverviewAsync()).Data!;
        unavailable.CanSubmit.Should().BeFalse();
        unavailable.CannotSubmitCode.Should().Be("unavailable");
        unavailable.CannotSubmitReasonTh.Should().Contain("ยังไม่ได้เชื่อมต่อระบบแจ้งชำระของ GaragePro");

        var tech = new Harness(UserRole.Technician);
        (await tech.Service.GetOverviewAsync()).Data!.CannotSubmitCode.Should().Be("not_manager");

        var pending = new Harness();
        pending.Reader.Rows.Add(new GarageProPaymentRow(140, 1, Thai(2026, 10, 20, 9), Thai(2026, 10, 20, 9, 5), "service",
            Thai(2026, 12, 3), "Images/PaySlip/p.jpg"));
        var withPending = (await pending.Service.GetOverviewAsync()).Data!;
        withPending.CannotSubmitCode.Should().Be("pending_exists");
        withPending.Pending!.Id.Should().Be("140");
        withPending.Pending.Status.Should().Be("pending");

        var expired = new Harness();
        expired.Reader.Branch = expired.Reader.Branch! with { EndsAtUtc = Thai(2026, 10, 1) };
        var o = (await expired.Service.GetOverviewAsync()).Data!;
        o.Status.Should().Be("expired");
        o.ReadOnly.Should().BeFalse();
        o.DaysLeft.Should().Be(-21);
    }

    // ---------- รายการ ----------

    [Fact]
    public async Task List_maps_legacy_rows_statuses_thai_times_and_slip_links()
    {
        var h = new Harness();
        h.Reader.Rows.AddRange(
        [
            new GarageProPaymentRow(145, 2, Thai(2026, 10, 18, 22, 16), Thai(2026, 10, 18, 22, 17), "service",
                Thai(2026, 11, 3), "Images/PaySlip/สลิป 1.jpg"),
            new GarageProPaymentRow(141, -1, Thai(2026, 10, 16, 15, 12), Thai(2026, 10, 17, 8, 17), null,
                Thai(2026, 11, 4), "Images/PaySlip/b.jpg"),
            new GarageProPaymentRow(140, 0, Thai(2026, 10, 13, 14, 2), Thai(2026, 10, 13, 14, 30), "service", null, null),
            new GarageProPaymentRow(139, null, null, Thai(2026, 10, 10, 14, 1), " ", null, "../etc/passwd"),
            new GarageProPaymentRow(138, 1, null, null, "service", null, null)
        ]);

        var list = (await h.Service.ListPaymentsAsync(null)).Data!;

        list.Select(p => p.Id).Should().Equal("145", "141", "140", "139");
        var approved = list[0];
        approved.ShortId.Should().Be("145");
        approved.Status.Should().Be("approved");
        approved.PaidAt.Should().Be(new DateTime(2026, 10, 18, 15, 16, 0, DateTimeKind.Utc), "เวลาไทยในตาราง → UTC");
        approved.SubmittedBy.Should().Be("service");
        approved.MonthCount.Should().BeNull("ตารางเดิมไม่เก็บจำนวนเดือน");
        approved.Amount.Should().BeNull();
        approved.PeriodEndsAt.Should().Be(Thai(2026, 11, 3));
        approved.PeriodLastUsableDate.Should().Be(new DateOnly(2026, 11, 2));
        approved.Slip!.Url.Should().Be("https://legacy-files.example.test/Images/PaySlip/%E0%B8%AA%E0%B8%A5%E0%B8%B4%E0%B8%9B%201.jpg");
        approved.Slip.ContentType.Should().BeNull();

        list[1].Status.Should().Be("cancelled", "-1 ยกเลิกอัตโนมัติ");
        list[1].PeriodEndsAt.Should().BeNull("ช่วงที่ต่อแสดงเฉพาะที่ยืนยันแล้ว");
        list[1].SubmittedBy.Should().BeNull();
        list[2].Status.Should().Be("cancelled");
        list[2].Slip.Should().BeNull();
        list[3].Status.Should().Be("cancelled", "สถานะว่าง = ข้อมูลเสีย ไม่นับว่ารอตรวจ");
        list[3].PaidAt.Should().Be(Thai(2026, 10, 10, 14, 1), "ไม่มีวันโอนใช้วันที่บันทึก");
        list[3].Slip.Should().BeNull("path ที่มี .. ไม่สร้างลิงก์");

        var tech = new Harness(UserRole.Technician);
        tech.Reader.Rows.AddRange(h.Reader.Rows);
        (await tech.Service.ListPaymentsAsync(null)).Data!.Should().OnlyContain(p => p.Slip == null, "ไม่ใช่ผู้จัดการไม่เห็นสลิป");

        var noBase = new Harness();
        noBase.Options.Package.GarageProApi.SlipBaseUrl = "";
        noBase.Reader.Rows.AddRange(h.Reader.Rows);
        (await noBase.Service.ListPaymentsAsync(null)).Data!.Should().OnlyContain(p => p.Slip == null);
    }

    [Theory]
    [InlineData("Images/PaySlip/a.jpg", "https://files.example.test/base/Images/PaySlip/a.jpg")]
    [InlineData("/Images//PaySlip/a b.jpg", "https://files.example.test/base/Images/PaySlip/a%20b.jpg")]
    [InlineData("Images\\PaySlip\\a.jpg", "https://files.example.test/base/Images/PaySlip/a.jpg")]
    [InlineData("https://evil.example/a.jpg", null)]
    [InlineData("//evil.example/a.jpg", null)]
    [InlineData("Images/../secret.jpg", null)]
    [InlineData("", null)]
    public void Slip_links_are_built_from_the_configured_base_only(string path, string? expected) =>
        GarageProPackageService.SlipUrl(new Uri("https://files.example.test/base/"), path).Should().Be(expected);

    // ---------- แจ้งชำระ ----------

    [Fact]
    public async Task Submit_forwards_to_the_legacy_api_and_returns_the_row_it_created()
    {
        var h = new Harness();

        var result = await h.Service.SubmitAsync(Form(amount: "ไม่ใช่ตัวเลข"));

        result.Success.Should().BeTrue("ยอดเงิน/หมายเหตุไม่ใช้ใน flow นี้");
        var request = h.Gateway.Requests.Single();
        request.Zone.Should().Be(2);
        request.BranchId.Should().Be(Branch);
        request.PaidAtThai.Should().Be(new DateTime(2026, 10, 21, 14, 30, 0));
        request.MonthCount.Should().Be(3);
        request.CreatedByUserId.Should().Be(501);
        request.Slip.Should().Equal(9, 9, 9);
        request.SlipContentType.Should().Be("image/jpeg");
        request.SlipFileName.Should().EndWith(".jpg");

        var dto = result.Data!;
        dto.Id.Should().Be("146");
        dto.Status.Should().Be("pending");
        dto.MonthCount.Should().Be(3);
        dto.Slip!.Url.Should().StartWith("https://legacy-files.example.test/");
    }

    [Fact]
    public async Task Zone_comes_from_the_shard_map()
    {
        var h = new Harness(shard: "db1");
        await h.Service.SubmitAsync(Form());
        h.Gateway.Requests.Single().Zone.Should().Be(1);
    }

    [Fact]
    public async Task Same_request_id_returns_the_same_row_without_calling_the_legacy_api_again()
    {
        var h = new Harness();
        var first = (await h.Service.SubmitAsync(Form("dup"))).Data!;

        var second = await h.Service.SubmitAsync(Form("dup"));

        second.Data.Should().BeEquivalentTo(first);
        h.Gateway.Requests.Should().ContainSingle();

        var other = await h.Service.SubmitAsync(Form("another"));
        other.Error!.Code.Should().Be(PackageErrors.PendingExistsCode, "รอตรวจได้ครั้งละ 1 รายการ (กันที่ฝั่ง ServicePro)");
        h.Gateway.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task An_existing_pending_row_blocks_a_new_submission()
    {
        var h = new Harness();
        h.Reader.Rows.Add(new GarageProPaymentRow(144, 1, Thai(2026, 10, 20, 10), Thai(2026, 10, 20, 10), "service",
            Thai(2026, 12, 3), null));

        (await h.Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.PendingExistsCode);
        h.Gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_outcome_then_retry_with_the_same_request_id_recovers_the_row_instead_of_sending_again()
    {
        var h = new Harness();
        h.Gateway.Next = new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Unavailable);

        var first = await h.Service.SubmitAsync(Form("net"));
        first.Error!.Code.Should().Be(PackageErrors.UnavailableCode);
        SelfServePackageControllerStatus(first.Error.Code).Should().Be(503);

        var retry = await h.Service.SubmitAsync(Form("net"));
        retry.Success.Should().BeTrue("แถวที่ระบบเดิมบันทึกไว้แล้วคือคำขอเดิม");
        retry.Data!.Status.Should().Be("pending");
        h.Gateway.Requests.Should().ContainSingle("ไม่ส่งซ้ำ");

        var nothingCreated = new Harness();
        nothingCreated.Gateway.Next = new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Unavailable);
        nothingCreated.Gateway.CreatesRow = false;
        await nothingCreated.Service.SubmitAsync(Form("net"));
        nothingCreated.Gateway.Next = new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Accepted, null, 150);
        nothingCreated.Gateway.CreatesRow = true;
        (await nothingCreated.Service.SubmitAsync(Form("net"))).Data!.Id.Should().Be("150");
        nothingCreated.Gateway.Requests.Should().HaveCount(2, "ครั้งแรกระบบเดิมไม่ได้บันทึก จึงส่งใหม่ได้");
    }

    [Fact]
    public async Task Legacy_rejection_with_a_thai_message_is_a_validation_error_and_can_be_retried()
    {
        var h = new Harness();
        h.Gateway.Next = new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Rejected, "กรุณาแนบไฟล์สลิป\nให้ถูกต้อง");

        var result = await h.Service.SubmitAsync(Form("r1"));

        result.Error!.Code.Should().Be(PackageErrors.ValidationCode);
        result.Error.MessageTh.Should().Be("กรุณาแนบไฟล์สลิป ให้ถูกต้อง");
        result.Error.Field.Should().Be("slip");
        SelfServePackageControllerStatus(result.Error.Code).Should().Be(422);

        h.Gateway.Next = new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Accepted, "", 147);
        (await h.Service.SubmitAsync(Form("r1"))).Data!.Id.Should().Be("147", "ไม่ได้บันทึก = ลองใหม่ด้วย requestId เดิมได้");
    }

    [Theory]
    [InlineData("Object reference not set to an instance of an object.")]
    [InlineData("เกิดข้อผิดพลาด System.Data.SqlClient.SqlException")]
    [InlineData("")]
    [InlineData(null)]
    public void Technical_or_empty_legacy_messages_become_unavailable(string? message)
    {
        var error = GarageProPackageService.MapRejection(message);
        error.Code.Should().Be(PackageErrors.UnavailableCode);
        error.MessageTh.Should().Contain("ส่งการแจ้งชำระไปยังระบบ GaragePro ไม่สำเร็จ");
    }

    [Theory]
    [InlineData("จำนวนเดือนไม่ถูกต้อง", "monthCount")]
    [InlineData("วันที่โอนไม่ถูกต้อง", "paidAt")]
    [InlineData("ไม่พบสาขา", null)]
    public void Thai_legacy_messages_keep_their_text_and_guess_the_field(string message, string? field)
    {
        var error = GarageProPackageService.MapRejection(message);
        error.Code.Should().Be(PackageErrors.ValidationCode);
        error.MessageTh.Should().Be(message);
        error.Field.Should().Be(field);
    }

    [Fact]
    public async Task Base_url_unset_is_unavailable_without_calling_anything()
    {
        var h = new Harness();
        h.Gateway.IsConfigured = false;

        var result = await h.Service.SubmitAsync(Form());

        result.Error!.Code.Should().Be(PackageErrors.UnavailableCode);
        result.Error.MessageTh.Should().Contain("ยังไม่ได้เชื่อมต่อ");
        h.Gateway.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Submit_checks_role_account_and_form_before_sending()
    {
        (await new Harness(UserRole.FrontDesk).Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.ForbiddenCode);

        var noAccount = new Harness();
        noAccount.Options.Package.PaymentAccount = new SelfServeOptions.PaymentAccountOptions();
        (await noAccount.Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.UnavailableCode);

        var h = new Harness();
        (await h.Service.SubmitAsync(Form(months: "5"))).Error!.Field.Should().Be("monthCount");
        (await h.Service.SubmitAsync(Form(paidAt: "2026-10-22T14:30:00+07:00"))).Error!.Field.Should().Be("paidAt");
        (await h.Service.SubmitAsync(Form(paidAt: "2569-10-21T14:30:00"))).Error!.MessageTh.Should().Contain("ค.ศ.");
        (await h.Service.SubmitAsync(Form(requestId: ""))).Error!.Field.Should().Be("requestId");
        (await h.Service.SubmitAsync(Form() with { Slip = null })).Error!.Field.Should().Be("slip");
        (await h.Service.SubmitAsync(Form(slip: new PackageSlipUpload(new MemoryStream([1]), "image/gif", 1, "a.gif"))))
            .Error!.Code.Should().Be(PackageErrors.AttachmentTypeInvalidCode);
        h.Slips.Invalid = true;
        (await h.Service.SubmitAsync(Form())).Error!.Code.Should().Be(PackageErrors.AttachmentTypeInvalidCode);
        h.Gateway.Requests.Should().BeEmpty();
    }

    // ---------- ยกเลิก / สลิป ----------

    [Fact]
    public async Task Cancel_and_slip_download_are_not_offered_for_legacy_rows()
    {
        var h = new Harness();
        h.Reader.Rows.Add(new GarageProPaymentRow(144, 1, Thai(2026, 10, 20, 10), Thai(2026, 10, 20, 10), "service", null, null));
        h.Reader.Rows.Add(new GarageProPaymentRow(143, 2, Thai(2026, 10, 1, 10), Thai(2026, 10, 1, 10), "service", null, null));

        (await h.Service.CancelAsync("144")).Error!.Code.Should().Be(PackageErrors.NotCancellableCode);
        SelfServePackageControllerStatus(PackageErrors.NotCancellableCode).Should().Be(409);
        (await h.Service.CancelAsync("143")).Error!.Code.Should().Be(PackageErrors.NotPendingCode);
        (await h.Service.CancelAsync("999")).Error!.Code.Should().Be(PackageErrors.NotFoundCode);
        (await h.Service.CancelAsync("abc")).Error!.Code.Should().Be(PackageErrors.NotFoundCode);
        (await h.Service.OpenSlipAsync("144")).Error!.Code.Should().Be(PackageErrors.NotFoundCode);
        (await new Harness(UserRole.Technician).Service.CancelAsync("144")).Error!.Code.Should().Be(PackageErrors.ForbiddenCode);
    }

    private static int SelfServePackageControllerStatus(string code) =>
        API.Controllers.SelfServePackageController.StatusFor(code);

    /// <summary>ตัวแทน service สำหรับเทสต์ router (นับการเรียกลิงก์สาธารณะ)</summary>
    private sealed class MarkerService(string name) : IPackagePaymentService
    {
        public string Name { get; } = name;
        public int SignedCalls { get; private set; }

        public Task<Result<PackageOverviewDto>> GetOverviewAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<PackagePaymentDto>>> ListPaymentsAsync(int? take, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<PackagePaymentDto>> SubmitAsync(PackagePaymentSubmission submission, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<PackagePaymentDto>> CancelAsync(string? paymentId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<PackageSlipFile>> OpenSlipAsync(string? paymentId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PackageSlipFile?> OpenSignedSlipAsync(string? paymentId, string? exp, string? sig,
            CancellationToken ct = default)
        {
            SignedCalls++;
            return Task.FromResult<PackageSlipFile?>(null);
        }
    }
}
