using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ล็อกสัญญากลางของ self-serve (แผน §3) ที่เว็บ/แอปอ่านเป็น string: ชื่อฟีเจอร์ camelCase · ชื่อแพ็กเกจตัวพิมพ์เล็ก ·
/// รหัส error + HTTP status · ค่าเริ่มต้นของ SelfServeOptions — เปลี่ยนแล้วต้องแก้ทุกฝั่งพร้อมกัน
/// </summary>
public class PlanContractsTests
{
    [Fact]
    public void All_features_map_to_seven_camel_case_names_in_bit_order()
    {
        PlanFeatureNames.ToJsonNames(PlanFeature.All).Should().Equal(
            "purchasing", "inventory", "pos", "taxInvoice", "advancedReports", "export", "multiBranch");
    }

    [Fact]
    public void No_features_map_to_an_empty_list()
    {
        PlanFeatureNames.ToJsonNames(PlanFeature.None).Should().BeEmpty();
    }

    [Fact]
    public void Feature_names_round_trip_and_tolerate_case_and_unknown_values()
    {
        var names = PlanFeatureNames.ToJsonNames(PlanFeature.Pos | PlanFeature.Export);

        PlanFeatureNames.Parse(names).Should().Be(PlanFeature.Pos | PlanFeature.Export);
        PlanFeatureNames.Parse(["TAXINVOICE", " multiBranch ", "not-a-feature"])
            .Should().Be(PlanFeature.TaxInvoice | PlanFeature.MultiBranch);
        PlanFeatureNames.Parse(null).Should().Be(PlanFeature.None);
        PlanFeatureNames.TryParseOne("not-a-feature", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(PlanKind.Legacy, "legacy")]
    [InlineData(PlanKind.Trial, "trial")]
    [InlineData(PlanKind.Paid, "paid")]
    [InlineData(PlanKind.Suspended, "suspended")]
    public void Plan_kind_names_are_lowercase_and_round_trip(PlanKind kind, string json)
    {
        PlanKindNames.ToJsonName(kind).Should().Be(json);
        PlanKindNames.TryParse(json, out var parsed).Should().BeTrue();
        parsed.Should().Be(kind);
        PlanKindNames.TryParse(json.ToUpperInvariant(), out _).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("7")]
    [InlineData("premium")]
    public void Unknown_plan_kind_does_not_parse(string? value)
    {
        PlanKindNames.TryParse(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Legacy_plan_is_paid_like_with_everything_open()
    {
        var plan = WorkspacePlan.Legacy;

        plan.Kind.Should().Be(PlanKind.Legacy);
        plan.Features.Should().Be(PlanFeature.All);
        plan.HasFeature(PlanFeature.Purchasing | PlanFeature.MultiBranch).Should().BeTrue();
        plan.Limits.Should().Be(PlanLimits.Unlimited);
        plan.Limits.For(PlanLimitKind.Jobs).Should().BeNull();
        plan.IsReadOnly.Should().BeFalse();
        plan.IsLocked.Should().BeFalse();
        plan.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Unlimited_policy_never_limits_anything()
    {
        var policy = new UnlimitedWorkspacePolicy();

        (await policy.GetAsync("sp1", 1001)).Should().Be(WorkspacePlan.Legacy);
        (await policy.CheckLimitAsync("sp1", 1001, PlanLimitKind.Staff)).Should().BeNull();
    }

    [Fact]
    public void Plan_errors_carry_codes_messages_and_status()
    {
        var limit = PlanErrors.LimitReached(PlanLimitKind.CatalogItems, 200);
        limit.Code.Should().Be("PLAN_LIMIT_REACHED");
        limit.MessageTh.Should().Contain("สินค้า/บริการ").And.Contain("200").And.Contain("LINE @garagepro");

        var locked = PlanErrors.FeatureLocked(PlanFeature.Purchasing);
        locked.Code.Should().Be("PLAN_FEATURE_LOCKED");
        locked.MessageTh.Should().Be("ฟีเจอร์นี้เปิดในแพ็กเกจเต็ม ติดต่อ LINE @garagepro");

        PlanErrors.TrialExpired().Code.Should().Be("TRIAL_EXPIRED");
        PlanErrors.TrialExpired().MessageTh.Should().Contain("30 วัน");
        PlanErrors.TrialLocked().Code.Should().Be("TRIAL_EXPIRED");

        PlanErrors.StatusCodeFor("TRIAL_EXPIRED").Should().Be(403);
        PlanErrors.StatusCodeFor("PLAN_FEATURE_LOCKED").Should().Be(403);
        PlanErrors.StatusCodeFor("PLAN_LIMIT_REACHED").Should().Be(422);
        PlanErrors.StatusCodeFor("QUOTE_NOT_FOUND").Should().BeNull();
    }

    [Theory]
    [InlineData("AUTH_RATE_LIMITED", 429)]
    [InlineData("SIGNUP_RATE_LIMITED", 429)]
    [InlineData("POS_RECEIPT_CONFLICT", 409)]
    [InlineData("SIGNUP_DISABLED", 503)]
    [InlineData("SIGNUP_VALIDATION", 422)]
    [InlineData("SIGNUP_BOT_CHECK_FAILED", 422)]
    [InlineData("SIGNUP_EMAIL_DISPOSABLE", 422)]
    [InlineData("SIGNUP_EMAIL_DOMAIN_INVALID", 422)]
    [InlineData("SIGNUP_OTP_THROTTLED", 429)]
    [InlineData("SIGNUP_OTP_INVALID", 422)]
    [InlineData("SIGNUP_OTP_EXPIRED", 422)]
    [InlineData("SIGNUP_OTP_LOCKED", 422)]
    [InlineData("SIGNUP_TOKEN_INVALID", 401)]
    [InlineData("SIGNUP_GOOGLE_INVALID", 422)]
    [InlineData("SIGNUP_ACCOUNT_EXISTS", 409)]
    [InlineData("SIGNUP_PROVISION_FAILED", 503)]
    [InlineData("SELFSERVE_NOT_APPLICABLE", 404)]
    [InlineData("SELFSERVE_EXTENSION_ALREADY_REQUESTED", 409)]
    [InlineData("SELFSERVE_SAMPLE_FORBIDDEN", 403)]
    [InlineData("AUTH_BRANCH_FORBIDDEN", 403)]
    public void Self_serve_error_codes_map_to_the_planned_http_status(string code, int status)
    {
        SelfServeErrors.StatusCodeFor(code).Should().Be(status);
    }

    [Fact]
    public void Self_serve_error_factories_use_the_planned_wording()
    {
        SelfServeErrors.SignupEmailDisposable().MessageTh.Should().Be("ไม่รองรับอีเมลชั่วคราว กรุณาใช้อีเมลที่ใช้งานจริง");
        SelfServeErrors.SignupEmailDisposable().Field.Should().Be("email");
        SelfServeErrors.SignupValidation("กรุณากรอกชื่ออู่", "garageName").Field.Should().Be("garageName");
        SelfServeErrors.AuthRateLimited(900).MessageTh.Should().Contain("15 นาที");
        SelfServeErrors.AuthRateLimited(45).MessageTh.Should().Contain("45 วินาที");
        SelfServeErrors.AuthRateLimited().MessageTh.Should().Contain("รอสักครู่");
        SelfServeErrors.SignupOtpInvalid(2).MessageTh.Should().Contain("2 ครั้ง");
        SelfServeErrors.StatusCodeFor("QUOTE_NOT_FOUND").Should().BeNull();
    }

    [Fact]
    public void Self_serve_options_defaults_match_the_plan()
    {
        var options = new SelfServeOptions();

        options.Enabled.Should().BeFalse("ปิดไว้ก่อนจนกว่า ops จะเปิดใน production");
        options.ShardKey.Should().Be("sp1");
        options.Trial.Days.Should().Be(30);
        options.Trial.ReadOnlyDays.Should().Be(90);
        options.Trial.EffectiveReminderDays.Should().Equal(7, 1);
        options.Trial.ComebackDay.Should().Be(45);
        options.Limits.Staff.Should().Be(5);
        options.Limits.CatalogItems.Should().Be(200);
        options.Limits.Customers.Should().Be(200);
        options.Limits.Jobs.Should().Be(100);
        options.Limits.AttachmentMb.Should().Be(200);
        options.Limits.ToPlanLimits().Should().Be(new PlanLimits(5, 200, 200, 100));
        options.Email.Provider.Should().Be("log");
        options.Email.IsLogProvider.Should().BeTrue();
        options.Blocklist.RefreshDays.Should().Be(30);
        options.Jobs.DailyAtLocalTime.Should().Be("03:30");
        options.Jobs.SummaryAtLocalTime.Should().Be("08:30");
        options.PublicBaseUrl.Should().Be("https://service.garage-pro.net");
        options.SalesLineGroupId.Should().BeEmpty();
    }

    [Fact]
    public void Reminder_days_configured_explicitly_replace_the_default()
    {
        var options = new SelfServeOptions { Trial = { ReminderDays = [3] } };

        options.Trial.EffectiveReminderDays.Should().Equal(3);
    }
}
