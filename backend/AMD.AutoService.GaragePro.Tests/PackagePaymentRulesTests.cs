using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Notifications;
using FluentAssertions;
using System.Text.Json;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>ค่าของฟอร์มแจ้งชำระ · ลิงก์สลิปแบบมีลายเซ็น · id สั้น · ค่าตั้ง · body ของ LINE push (docs/17 §2, §5)</summary>
public class PackagePaymentRulesTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 7, 41, 0, DateTimeKind.Utc);   // 14:41 เวลาไทย

    // ---------- ยอดเงิน ----------

    [Theory]
    [InlineData("1500.00", 1500)]
    [InlineData("1,500", 1500)]
    [InlineData(" 590.5 ", 590.5)]
    [InlineData("999,999.99", 999999.99)]
    [InlineData("0.01", 0.01)]
    public void Amount_accepts_plain_numbers_with_optional_commas(string text, decimal expected)
    {
        var parsed = PackagePaymentRules.ParseAmount(text);
        parsed.Ok.Should().BeTrue();
        parsed.Value.Should().Be(expected);
        parsed.Value.Scale.Should().Be(2, "เงินเป็นทศนิยม 2 ตำแหน่งเสมอ");
    }

    [Theory]
    [InlineData(null, "กรุณากรอก")]
    [InlineData("", "กรุณากรอก")]
    [InlineData("abc", "ตัวเลข")]
    [InlineData("-5", "ตัวเลข")]
    [InlineData("0", "มากกว่า 0")]
    [InlineData("10.001", "ทศนิยม")]
    [InlineData("1000000", "ไม่เกิน")]
    [InlineData("1e3", "ตัวเลข")]
    public void Amount_rejects_bad_values_with_the_amount_field(string? text, string message)
    {
        var parsed = PackagePaymentRules.ParseAmount(text);
        parsed.Ok.Should().BeFalse();
        parsed.Error!.Code.Should().Be(PackageErrors.ValidationCode);
        parsed.Error.Field.Should().Be("amount");
        parsed.Error.MessageTh.Should().Contain(message);
    }

    // ---------- วันเวลาที่โอน ----------

    [Fact]
    public void Paid_at_with_an_offset_is_converted_to_utc()
    {
        var parsed = PackagePaymentRules.ParsePaidAt("2026-10-21T14:30:00+07:00", Now, 60);
        parsed.Value.Should().Be(new DateTime(2026, 10, 21, 7, 30, 0, DateTimeKind.Utc));
        parsed.Value.Kind.Should().Be(DateTimeKind.Utc);

        PackagePaymentRules.ParsePaidAt("2026-10-21T07:30:00Z", Now, 60).Value
            .Should().Be(new DateTime(2026, 10, 21, 7, 30, 0, DateTimeKind.Utc));
        PackagePaymentRules.ParsePaidAt("2026-10-21T07:30:00.000Z", Now, 60).Ok.Should().BeTrue("JS toISOString");
    }

    [Fact]
    public void Paid_at_without_an_offset_is_thai_time()
    {
        PackagePaymentRules.ParsePaidAt("2026-10-21T14:30", Now, 60).Value
            .Should().Be(new DateTime(2026, 10, 21, 7, 30, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void Paid_at_allows_five_minutes_of_clock_skew_but_not_the_future()
    {
        PackagePaymentRules.ParsePaidAt("2026-10-21T14:45:00+07:00", Now, 60).Ok.Should().BeTrue("ภายใน 5 นาที");
        var future = PackagePaymentRules.ParsePaidAt("2026-10-21T14:47:00+07:00", Now, 60);
        future.Ok.Should().BeFalse();
        future.Error!.Field.Should().Be("paidAt");
        future.Error.MessageTh.Should().Contain("อนาคต");
    }

    [Fact]
    public void Paid_at_can_go_back_only_the_configured_days()
    {
        PackagePaymentRules.ParsePaidAt("2026-08-23T15:00:00+07:00", Now, 60).Ok.Should().BeTrue();
        PackagePaymentRules.ParsePaidAt("2026-08-21T00:00:00+07:00", Now, 60).Error!.MessageTh.Should().Contain("60 วัน");
    }

    [Theory]
    [InlineData("2569-10-21T14:30:00+07:00")]
    [InlineData("2569-10-21T14:30")]
    public void Buddhist_year_gets_its_own_message(string text)
    {
        var parsed = PackagePaymentRules.ParsePaidAt(text, Now, 60);
        parsed.Error!.MessageTh.Should().Contain("ค.ศ.");
        parsed.Error.Field.Should().Be("paidAt");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    [InlineData("2026-13-40T10:00")]
    public void Paid_at_rejects_unreadable_values(string? text) =>
        PackagePaymentRules.ParsePaidAt(text, Now, 60).Error!.Field.Should().Be("paidAt");

    // ---------- อื่นๆ ----------

    [Theory]
    [InlineData("1", true)]
    [InlineData("12", true)]
    [InlineData("4", false)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("x", false)]
    [InlineData(null, false)]
    public void Month_count_must_be_one_of_the_options(string? text, bool ok)
    {
        var parsed = PackagePaymentRules.ParseMonthCount(text, [1, 2, 3, 6, 12]);
        parsed.Ok.Should().Be(ok);
        if (!ok) parsed.Error!.Field.Should().Be("monthCount");
    }

    [Fact]
    public void Note_is_optional_single_line_and_limited()
    {
        PackagePaymentRules.ParseNote("  ").Value.Should().BeNull();
        PackagePaymentRules.ParseNote("โอนจาก\nบัญชีบริษัท").Value.Should().Be("โอนจาก บัญชีบริษัท",
            "ข้อความไปลง LINE ห้ามปลอมบรรทัดคำสั่งอนุมัติ");
        PackagePaymentRules.ParseNote(new string('ก', 501)).Error!.Field.Should().Be("note");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("  ", false)]
    [InlineData("a3f2c1d0-0000-4000-8000-000000000001", true)]
    public void Request_id_is_required_and_limited(string? text, bool ok)
    {
        PackagePaymentRules.ParseRequestId(text).Ok.Should().Be(ok);
        PackagePaymentRules.ParseRequestId(new string('x', 101)).Error!.Field.Should().Be("requestId");
    }

    [Fact]
    public void Status_and_reject_reason_codes_are_snake_case_and_round_trip()
    {
        Enum.GetValues<PackagePaymentStatus>().Select(PackagePaymentRules.StatusCode)
            .Should().Equal("pending", "approved", "rejected", "cancelled");
        PackagePaymentRules.RejectReasonCodes.Should().Equal(
            "slip_unreadable", "amount_mismatch", "not_received", "duplicate", "other");

        foreach (var reason in Enum.GetValues<PackagePaymentRejectReason>())
        {
            PackagePaymentRules.TryParseRejectReason(PackagePaymentRules.RejectReasonCode(reason).ToUpperInvariant(),
                out var parsed).Should().BeTrue();
            parsed.Should().Be(reason);
        }

        PackagePaymentRules.RejectReasonTh(PackagePaymentRejectReason.SlipUnreadable).Should().Be("สลิปไม่ชัด อ่านไม่ออก");
        PackagePaymentRules.RejectReasonTh(PackagePaymentRejectReason.NotReceived).Should().Be("ไม่พบยอดเงินเข้าบัญชี");
        PackagePaymentRules.TryParseRejectReason("fraud", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("3f2a9c1b", "3f2a9c1b")]
    [InlineData("3F2A9C1B-12", "3f2a9c1b12")]
    [InlineData("{3f2a9c1b-1234-4abc-8def-0123456789ab}", "3f2a9c1b12344abc8def0123456789ab")]
    [InlineData("3f2a9c1", null)]
    [InlineData("3f2a9c1z", null)]
    [InlineData("", null)]
    public void Payment_reference_needs_at_least_eight_hex_digits(string text, string? expected) =>
        PackagePaymentReference.Normalize(text).Should().Be(expected);

    [Fact]
    public void Reference_prefix_is_laid_out_like_a_guid_string()
    {
        PackagePaymentReference.ToDisplayPrefix("3f2a9c1b").Should().Be("3f2a9c1b");
        PackagePaymentReference.ToDisplayPrefix("3f2a9c1b1234").Should().Be("3f2a9c1b-1234");
        PackagePaymentReference.ToDisplayPrefix("3f2a9c1b12344abc8d").Should().Be("3f2a9c1b-1234-4abc-8d");

        var id = Guid.NewGuid();
        id.ToString("D").Should().StartWith(PackagePaymentReference.ToDisplayPrefix(id.ToString("N")[..20]));
        PackagePaymentRules.ShortId(id).Should().Be(id.ToString("N")[..8]);
    }

    // ---------- ค่าตั้ง ----------

    [Fact]
    public void Month_options_default_dedupe_sort_and_clamp()
    {
        new SelfServeOptions.PackageOptions().EffectiveMonthOptions.Should().Equal(1, 2, 3, 6, 12);
        new SelfServeOptions.PackageOptions { MonthOptions = [12, 1, 1, 0, 99, 3] }.EffectiveMonthOptions
            .Should().Equal(1, 3, 12);
        new SelfServeOptions.PackageOptions { MonthOptions = [0, 40] }.EffectiveMonthOptions
            .Should().Equal(1, 2, 3, 6, 12);   // ผิดทั้งหมด = ค่าเริ่มต้น
        new SelfServeOptions.PackageOptions().EffectivePaidReminderDays.Should().Equal(14, 3);
    }

    [Fact]
    public void Prices_are_keyed_by_month_and_unset_means_no_standard_price()
    {
        var options = new SelfServeOptions.PackageOptions();
        options.Prices["1"] = 590;
        options.Prices["3"] = 0;

        options.PriceFor(1).Should().Be(590m);
        options.PriceFor(3).Should().BeNull("≤ 0 ถือว่าไม่ได้ตั้ง");
        options.PriceFor(6).Should().BeNull();
    }

    [Theory]
    [InlineData("", "", "", false)]
    [InlineData("บจก. อาร์มาดิลโล่", "", "", false)]
    [InlineData("บจก. อาร์มาดิลโล่", "123-4-56789-0", "", true)]
    [InlineData("บจก. อาร์มาดิลโล่", "", "0105563009156", true)]
    [InlineData("", "123-4-56789-0", "0105563009156", false)]
    public void Payment_account_needs_a_name_and_an_account_or_promptpay(string name, string no, string promptPay,
        bool configured) =>
        new SelfServeOptions.PaymentAccountOptions { AccountName = name, AccountNo = no, PromptPayId = promptPay }
            .IsConfigured.Should().Be(configured);

    // ---------- ลิงก์สลิป ----------

    private const string Key = "0123456789abcdef0123456789abcdef-test-key";

    [Fact]
    public void Signed_slip_link_verifies_until_it_expires()
    {
        var options = new SelfServeOptions.PackageOptions { SlipLinkKey = Key, PublicApiBaseUrl = "https://api.example.test/" };
        var id = Guid.NewGuid();

        var link = PackageSlipLinks.Create(options, id, Now)!;
        link.Should().StartWith($"https://api.example.test/api/v1/public/package-slips/{id:N}?exp=");

        var query = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);
        var exp = long.Parse(query["exp"]!);
        var sig = query["sig"]!;
        exp.Should().Be(new DateTimeOffset(Now).AddDays(7).ToUnixTimeSeconds());
        sig.Should().NotContainAny("+", "/", "=");

        PackageSlipLinks.Verify(Key, id, exp, sig, Now).Should().BeTrue();
        PackageSlipLinks.Verify(Key, id, exp, sig, Now.AddDays(7)).Should().BeFalse("หมดอายุแล้ว");
    }

    [Fact]
    public void Tampered_slip_links_fail()
    {
        var id = Guid.NewGuid();
        var exp = new DateTimeOffset(Now).AddDays(1).ToUnixTimeSeconds();
        var sig = PackageSlipLinks.Sign(Key, id, exp);

        PackageSlipLinks.Verify(Key, Guid.NewGuid(), exp, sig, Now).Should().BeFalse("id อื่น");
        PackageSlipLinks.Verify(Key, id, exp + 1, sig, Now).Should().BeFalse("ยืดเวลาเอง");
        PackageSlipLinks.Verify(Key, id, exp, sig[..^2] + "AA", Now).Should().BeFalse("ลายเซ็นผิด");
        PackageSlipLinks.Verify(Key, id, exp, "not base64!!", Now).Should().BeFalse();
        PackageSlipLinks.Verify(Key + "x", id, exp, sig, Now).Should().BeFalse("คีย์เปลี่ยน");
        PackageSlipLinks.Verify(null, id, exp, sig, Now).Should().BeFalse();
        PackageSlipLinks.Verify(Key, id, exp, null, Now).Should().BeFalse();
    }

    [Fact]
    public void No_or_short_key_means_no_link()
    {
        PackageSlipLinks.Create(new SelfServeOptions.PackageOptions(), Guid.NewGuid(), Now).Should().BeNull();
        PackageSlipLinks.Create(new SelfServeOptions.PackageOptions { SlipLinkKey = "too-short" }, Guid.NewGuid(), Now)
            .Should().BeNull("คีย์สั้นกว่า 32 ตัว");
        PackageSlipLinks.Create(new SelfServeOptions.PackageOptions { SlipLinkKey = Key, PublicApiBaseUrl = " " },
            Guid.NewGuid(), Now).Should().BeNull();
    }

    // ---------- LINE ----------

    [Fact]
    public void Line_push_body_carries_text_and_image_messages()
    {
        var body = LineContactNotifier.BuildPushBody("Cgroup",
        [
            LineOutboundMessage.FromText("แจ้งชำระ"),
            LineOutboundMessage.FromImage("https://api.example.test/api/v1/public/package-slips/x?exp=1&sig=a")
        ]);

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("to").GetString().Should().Be("Cgroup");
        var messages = doc.RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(2);
        messages[0].GetProperty("type").GetString().Should().Be("text");
        messages[0].GetProperty("text").GetString().Should().Be("แจ้งชำระ");
        messages[1].GetProperty("type").GetString().Should().Be("image");
        messages[1].GetProperty("originalContentUrl").GetString()
            .Should().Be(messages[1].GetProperty("previewImageUrl").GetString());
    }

    [Fact]
    public void Line_push_body_rejects_http_images_and_wrong_message_counts()
    {
        var http = () => LineContactNotifier.BuildPushBody("C", [LineOutboundMessage.FromImage("http://x.test/a.jpg")]);
        http.Should().Throw<ArgumentException>();

        var none = () => LineContactNotifier.BuildPushBody("C", []);
        none.Should().Throw<ArgumentException>();

        var six = () => LineContactNotifier.BuildPushBody("C",
            Enumerable.Range(0, 6).Select(i => LineOutboundMessage.FromText($"{i}")).ToList());
        six.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Text_only_notifiers_get_the_messages_joined()
    {
        LineOutboundMessage.Join([LineOutboundMessage.FromText("a"), LineOutboundMessage.FromImage("https://x/y.jpg")])
            .Should().Be("a\nhttps://x/y.jpg");
    }
}
