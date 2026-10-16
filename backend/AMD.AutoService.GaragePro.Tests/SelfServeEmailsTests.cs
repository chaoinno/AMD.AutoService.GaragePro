using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// อีเมลภาษาไทยของ self-serve + ตัวช่วยจัดรูปข้อความ — ทุกฉบับต้องไม่มี placeholder ค้าง ({…}) ระบุข้อเท็จจริงครบ
/// (รหัส · วันที่ไทย พ.ศ. · ช่องทางติดต่อ) และ escape ค่าที่ผู้ใช้กรอกใน HTML
/// </summary>
public class SelfServeEmailsTests
{
    private static readonly DateTime EndsAt = new(2026, 10, 31, 3, 0, 0, DateTimeKind.Utc);   // 10:00 น. 31/10/2569
    private const string BaseUrl = "https://service.garage-pro.net/";

    private static IEnumerable<EmailContent> All() =>
    [
        SelfServeEmails.Otp("482913", 10),
        SelfServeEmails.AccountExists(BaseUrl),
        SelfServeEmails.Welcome(new WelcomeEmail("อู่ช่างเอก", 1042, "owner@example.com", false, EndsAt, BaseUrl,
            [new SampleCredential("SP1042.T01", "Kx7-pq2Mz9")])),
        SelfServeEmails.Reminder(7, "อู่ช่างเอก", 1042, EndsAt, 90, BaseUrl),
        SelfServeEmails.Reminder(1, "อู่ช่างเอก", 1042, EndsAt, 90, BaseUrl),
        SelfServeEmails.Expired("อู่ช่างเอก", 1042, EndsAt, 90, EndsAt.AddDays(90), BaseUrl),
        SelfServeEmails.Comeback("อู่ช่างเอก", 1042, EndsAt, EndsAt.AddDays(90), BaseUrl)
    ];

    [Fact]
    public void Every_template_is_complete_and_signed()
    {
        foreach (var email in All())
        {
            foreach (var text in new[] { email.Subject, email.TextBody, email.HtmlBody })
            {
                text.Should().NotContain("{").And.NotContain("}", $"{email.Tag}: ห้ามมี placeholder ค้าง");
                text.Should().NotContainAny("TODO", "lorem", "XXX");
            }

            email.Subject.Should().Contain("ServicePro");
            email.TextBody.Should().EndWith("ทีมงาน ServicePro");
            email.HtmlBody.Should().StartWith("<div").And.EndWith("</div>");
            email.Tag.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Otp_states_the_code_and_its_lifetime()
    {
        var email = SelfServeEmails.Otp("482913", 10);

        email.Tag.Should().Be(SelfServeEmails.OtpTag);
        email.Subject.Should().Contain("482913");
        email.TextBody.Should().Contain("482913").And.Contain("10 นาที").And.Contain("ใช้ได้ครั้งเดียว");
        email.To("a@example.com").Should().Be(new Application.Abstractions.EmailMessage(
            "a@example.com", email.Subject, email.TextBody, email.HtmlBody, "otp"));
    }

    [Fact]
    public void Account_exists_points_to_login_without_creating_anything()
    {
        var email = SelfServeEmails.AccountExists(BaseUrl);

        email.TextBody.Should().Contain("https://service.garage-pro.net/login")
            .And.Contain("ไม่ได้สร้างบัญชีใหม่").And.Contain("LINE @garagepro").And.Contain("090-996-6446");
    }

    [Fact]
    public void Welcome_lists_workspace_login_trial_end_app_and_sample_credentials()
    {
        var email = SelfServeEmails.Welcome(new WelcomeEmail("อู่ช่างเอก", 1042, "owner@example.com", false, EndsAt, BaseUrl,
            [new SampleCredential("SP1042.T01", "Kx7-pq2Mz9"), new SampleCredential("SP1042.T02", "Lm3-zz8Qa1")]));

        email.Subject.Should().Contain("อู่ช่างเอก").And.Contain("SP1042");
        email.TextBody.Should()
            .Contain("รหัสอู่: SP1042")
            .And.Contain("31/10/2569 10:00 น.")
            .And.Contain("https://service.garage-pro.net/login")
            .And.Contain("ชื่อผู้ใช้: owner@example.com")
            .And.Contain("SP1042.T01 รหัสผ่าน Kx7-pq2Mz9")
            .And.Contain("SP1042.T02 รหัสผ่าน Lm3-zz8Qa1")
            .And.Contain(SelfServeText.PlayStoreUrl)
            .And.Contain(SelfServeText.AppStoreUrl)
            .And.Contain("จัดซื้อ");
    }

    [Fact]
    public void Welcome_for_google_accounts_mentions_google_sign_in_and_no_sample_block_when_empty()
    {
        var email = SelfServeEmails.Welcome(new WelcomeEmail("อู่ช่างเอก", 1042, "owner@gmail.com", true, EndsAt, BaseUrl));

        email.TextBody.Should().Contain("เข้าสู่ระบบด้วย Google").And.NotContain("รหัสที่คุณตั้งไว้")
            .And.NotContain("บัญชีช่างตัวอย่าง");
    }

    [Theory]
    [InlineData(7, "เหลืออีก 7 วัน")]
    [InlineData(1, "พรุ่งนี้")]
    [InlineData(0, "วันนี้")]
    public void Reminder_subject_matches_the_days_left(int days, string expected)
    {
        var email = SelfServeEmails.Reminder(days, "อู่ช่างเอก", 1042, EndsAt, 90, BaseUrl);

        email.Subject.Should().Contain(expected);
        email.TextBody.Should().Contain("31/10/2569 10:00 น.").And.Contain("อีก 90 วัน").And.Contain("ขอต่ออายุ");
    }

    [Fact]
    public void Expired_and_comeback_state_the_read_only_window_and_purge_date()
    {
        var expired = SelfServeEmails.Expired("อู่ช่างเอก", 1042, EndsAt, 90, EndsAt.AddDays(90), BaseUrl);
        expired.TextBody.Should().Contain("อ่านอย่างเดียว").And.Contain("29/01/2570").And.Contain("90 วันหลังหมดอายุ")
            .And.Contain("LINE @garagepro").And.Contain("090-996-6446");

        var comeback = SelfServeEmails.Comeback("อู่ช่างเอก", 1042, EndsAt, EndsAt.AddDays(90), BaseUrl);
        comeback.Tag.Should().Be(SelfServeEmails.ComebackTag);
        comeback.TextBody.Should().Contain("31/10/2569").And.Contain("29/01/2570").And.Contain("/login");
    }

    [Fact]
    public void Garage_name_is_html_escaped_and_kept_on_one_line()
    {
        var email = SelfServeEmails.Reminder(7, "<script>x</script>\nโทร: 0999", 1042, EndsAt, 90, BaseUrl);

        email.HtmlBody.Should().NotContain("<script>").And.Contain("&lt;script&gt;");
        email.Subject.Should().NotContain("\n");
    }

    // ---------- SelfServeText ----------

    [Theory]
    [InlineData("+66812345678", "081-234-5678")]
    [InlineData("+6621234567", "02-123-4567")]
    [InlineData("0812345678", "0812345678")]
    [InlineData(null, "-")]
    [InlineData("", "-")]
    public void DisplayPhone_formats_thai_e164_numbers(string? input, string expected) =>
        SelfServeText.DisplayPhone(input).Should().Be(expected);

    [Fact]
    public void Days_left_rounds_up_for_display_but_reminders_use_thai_calendar_days()
    {
        // job รัน 03:30 เวลาไทยของวันก่อนหมดอายุ (หมดอายุ 10:00): เหลือ 30.5 ชม. → แสดง 2 วัน แต่ตามปฏิทินคือ "พรุ่งนี้"
        var now = new DateTime(2026, 10, 29, 20, 30, 0, DateTimeKind.Utc);
        SelfServeText.DaysLeft(EndsAt, now).Should().Be(2);
        SelfServeText.CalendarDaysLeft(EndsAt, now).Should().Be(1);
        SelfServeText.DaysLeft(EndsAt, EndsAt.AddHours(1)).Should().Be(0);
    }

    [Fact]
    public void Thai_dates_use_the_buddhist_year_and_thai_time()
    {
        SelfServeText.FormatThaiDate(new DateTime(2026, 12, 31, 17, 30, 0, DateTimeKind.Utc)).Should().Be("01/01/2570");
        SelfServeText.FormatThaiDateTime(EndsAt).Should().Be("31/10/2569 10:00 น.");
        SelfServeText.WorkspaceCode(1042).Should().Be("SP1042");
    }

    [Fact]
    public void Deterministic_guid_is_stable_and_distinct()
    {
        SelfServeText.DeterministicGuid("summary:2026-10-31").Should().Be(SelfServeText.DeterministicGuid("summary:2026-10-31"));
        SelfServeText.DeterministicGuid("summary:2026-10-31").Should().NotBe(SelfServeText.DeterministicGuid("summary:2026-11-01"));
    }

    [Fact]
    public void SingleLine_strips_line_breaks_and_limits_length()
    {
        SelfServeText.SingleLine("a\r\nb\tc", 10).Should().Be("a b c");
        SelfServeText.SingleLine(new string('x', 20), 5).Should().Be("xxxxx…");
    }

    // ---------- ส่วนประกอบเล็กๆ ของ S4 ----------

    [Theory]
    [InlineData(SelfServeErrors.NotApplicableCode, 404)]
    [InlineData(SelfServeErrors.ExtensionAlreadyRequestedCode, 409)]
    [InlineData(SelfServeErrors.SampleForbiddenCode, 403)]
    [InlineData(SelfServeLifecycleErrors.ValidationCode, 422)]
    [InlineData(SelfServeLifecycleErrors.NotifyFailedCode, 502)]
    [InlineData(SelfServeLifecycleErrors.SampleUnavailableCode, 503)]
    [InlineData(Application.Common.PlanErrors.TrialExpiredCode, 403)]
    [InlineData("SELFSERVE_SAMPLE_WORKSPACE_NOT_READY", 409)]
    [InlineData("SELFSERVE_SAMPLE_CLEAR_INCOMPLETE", 500)]
    [InlineData("SOMETHING_ELSE", 422)]
    public void Controller_maps_error_codes_to_http_status(string code, int status) =>
        SelfServeController.StatusFor(code).Should().Be(status);

    [Fact]
    public void Blocklist_parser_keeps_valid_unique_lowercase_domains()
    {
        var parsed = BlocklistRefresher.Parse("""
            # disposable domains
            Mailinator.com
            mailinator.com
            @10minutemail.com
            // comment
            not a domain
            -bad-.com
            yopmail.fr
            """);

        parsed.Should().Equal("mailinator.com", "10minutemail.com", "yopmail.fr");
        BlocklistRefresher.Parse(null).Should().BeEmpty();
    }
}
