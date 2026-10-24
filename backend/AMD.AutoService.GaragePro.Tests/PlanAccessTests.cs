using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class PlanAccessTests
{
    // สมัคร 14/10 10:00 (ไทย) + 30 วัน = หมด 13/11 10:00 (ไทย) = 03:00 UTC
    private static readonly DateTime TrialEndsUtc = new(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("2026-10-14T03:00:00Z", 30)]  // วันสมัคร
    [InlineData("2026-11-05T16:59:00Z", 8)]   // วันที่ 22 เวลา 23:59 ไทย — ไม่ลดลงระหว่างวัน
    [InlineData("2026-11-05T17:00:00Z", 7)]   // 06/11 00:00 ไทย = วันที่ 23 → เหลือ 7 (docs/12 §6 แถบเตือน)
    [InlineData("2026-11-13T02:59:00Z", 0)]   // วันสุดท้าย ก่อนเวลาหมด = "หมดอายุวันนี้"
    [InlineData("2026-11-13T03:00:00Z", 0)]   // หมดแล้ววันเดียวกัน
    [InlineData("2026-11-14T03:00:00Z", -1)]
    public void Days_left_counts_thai_calendar_days_to_the_last_usable_day(string now, int expected) =>
        PlanAccess.DaysLeft(TrialEndsUtc, DateTime.Parse(now).ToUniversalTime()).Should().Be(expected);

    [Fact]
    public void Expiry_at_thai_midnight_belongs_to_the_previous_day()
    {
        var midnightThai = new DateTime(2026, 11, 13, 17, 0, 0, DateTimeKind.Utc); // 14/11 00:00 ไทย
        PlanAccess.DaysLeft(midnightThai, new DateTime(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc)).Should().Be(0);
        PlanAccess.DaysLeft(midnightThai, new DateTime(2026, 11, 12, 10, 0, 0, DateTimeKind.Utc)).Should().Be(1);
        PlanAccess.DaysLeft(null, DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void Legacy_plans_have_no_dto_or_workspace()
    {
        PlanAccess.ToDto(WorkspacePlan.Legacy, DateTime.UtcNow).Should().BeNull();
        PlanAccess.ToWorkspaceInfo(WorkspacePlan.Legacy, "อู่").Should().BeNull();
    }

    [Fact]
    public void Trial_dto_lists_no_features_and_the_limits()
    {
        var dto = PlanAccess.ToDto(FakeWorkspacePolicy.Trial(expiresAt: TrialEndsUtc), TrialEndsUtc.AddDays(-10))!;

        dto.Kind.Should().Be("trial");
        dto.Features.Should().BeEmpty();
        dto.Limits.Should().Be(FakeWorkspacePolicy.TrialLimits);
        dto.TrialEndsAt.Should().Be(TrialEndsUtc);
        dto.DaysLeft.Should().Be(10);
        PlanAccess.ToWorkspaceInfo(FakeWorkspacePolicy.Trial(workspaceNo: 1042), "อู่ช่างเอ")
            .Should().Be(new WorkspaceInfoDto("SP1042", "อู่ช่างเอ"));
    }

    [Fact]
    public void Paid_dto_lists_every_feature_and_omits_unlimited_limits()
    {
        var dto = PlanAccess.ToDto(FakeWorkspacePolicy.Paid(), DateTime.UtcNow)!;

        dto.Kind.Should().Be("paid");
        dto.Features.Should().HaveCount(7).And.Contain("advancedReports");
        dto.Limits.Should().BeNull();
        dto.DaysLeft.Should().BeNull();
    }

    [Fact]
    public void Read_only_error_wording_depends_on_the_plan()
    {
        PlanAccess.ReadOnlyError(FakeWorkspacePolicy.Trial(readOnly: true), 30).Code.Should().Be(PlanErrors.TrialExpiredCode);
        PlanAccess.ReadOnlyError(FakeWorkspacePolicy.Trial(readOnly: true), 30).MessageTh.Should().Contain("30 วัน");
        var paid = PlanAccess.ReadOnlyError(FakeWorkspacePolicy.Paid(readOnly: true), 30);
        paid.Code.Should().Be(PlanErrors.TrialExpiredCode);
        paid.MessageTh.Should().NotContain("ทดลองใช้");
    }

    // ตาราง docs/17 หัวข้อ 4.1 (D8): เตะเฉพาะเมื่อนโยบายเข้มกว่า claim · ตัวเลขเป็นวันนับจาก E = วันหมดใน claim
    // policy: legacy | trial | paid | locked (เลยช่วงอ่านอย่างเดียว/ระงับ) | converted (ย้ายไปสาขาจริงแล้ว)
    [Theory]
    [InlineData("1 token legacy", "legacy", null, null, null, -1d, "ok")]
    [InlineData("1b claim legacy", "legacy", null, PlanKind.Legacy, null, -1d, "ok")]
    [InlineData("2 ถูกล็อกระหว่างทดลองใช้", "locked", 0d, PlanKind.Trial, 0d, -1d, "locked")]
    [InlineData("2b ถูกล็อก claim paid", "locked", null, PlanKind.Paid, null, -1d, "locked")]
    [InlineData("3 ตรงกัน ยังไม่หมด", "trial", 0d, PlanKind.Trial, 0d, -1d, "ok")]
    [InlineData("4 หมดอายุตามธรรมชาติ (วินาทีที่หมดพอดี)", "trial", 0d, PlanKind.Trial, 0d, 0d, "ok")]
    [InlineData("4b หมดอายุตามธรรมชาติ", "trial", 0d, PlanKind.Trial, 0d, 1d, "ok")]
    [InlineData("5 ต่อก่อนหมด", "trial", 30d, PlanKind.Trial, 0d, -1d, "ok")]
    [InlineData("6 ต่อก่อนหมด แล้วเลยวันหมดเดิมใน claim", "trial", 30d, PlanKind.Trial, 0d, 1d, "ok")]
    [InlineData("7 ต่ออายุหลังหมด", "trial", 35d, PlanKind.Trial, 0d, 5d, "ok")]
    [InlineData("8 trial→paid มีวันหมด", "paid", 365d, PlanKind.Trial, 0d, -1d, "ok")]
    [InlineData("8b trial→paid แล้วเลยวันหมดทดลองเดิม", "paid", 365d, PlanKind.Trial, 0d, 1d, "ok")]
    [InlineData("9 trial→paid ไม่มีกำหนด", "paid", null, PlanKind.Trial, 0d, -1d, "ok")]
    [InlineData("9b trial→paid ไม่มีกำหนด หลังวันหมดทดลองเดิม", "paid", null, PlanKind.Trial, 0d, 1d, "ok")]
    [InlineData("10 ต่อ paid แล้วเลยวันหมดเดิม", "paid", 365d, PlanKind.Paid, 0d, 1d, "ok")]
    [InlineData("11 ทีมงานตัดวันจนหมด (spadmin extend ติดลบ)", "trial", -11d, PlanKind.Trial, 0d, -10d, "changed")]
    [InlineData("11b ตัดวันให้หมดวินาทีนี้พอดี", "trial", -10d, PlanKind.Trial, 0d, -10d, "changed")]
    [InlineData("12 ตัดวัน paid", "paid", -11d, PlanKind.Paid, 0d, -10d, "changed")]
    [InlineData("12b paid ไม่มีกำหนด ถูกตั้งวันหมดย้อนหลัง", "paid", -11d, PlanKind.Paid, null, -10d, "changed")]
    [InlineData("13 paid→trial", "trial", 0d, PlanKind.Paid, null, -1d, "changed")]
    [InlineData("13b paid→trial วันเดียวกัน", "trial", 0d, PlanKind.Paid, 0d, -1d, "changed")]
    [InlineData("14 converted กับ claim paid", "converted", null, PlanKind.Paid, null, 1d, "ok")]
    [InlineData("15 token sp1 ไม่มี claim plan", "trial", 0d, null, null, -1d, "changed")]
    [InlineData("16 claim trial แต่นโยบายเป็น legacy", "legacy", null, PlanKind.Trial, 0d, -1d, "changed")]
    // ชนิดเพิ่มสิทธิ์แต่เวลาเข้มกว่า: paid ที่วันหมดผ่านไปแล้ว ขณะ claim trial ยังเขียนได้ → เตะ (ไม่ใช่ข้อยกเว้นของ trial→paid)
    [InlineData("17 trial→paid ที่หมดไปแล้ว", "paid", -11d, PlanKind.Trial, 0d, -10d, "changed")]
    public void Revocation_kicks_only_when_the_policy_is_stricter_than_the_claim(
        string row, string policy, double? policyEnd, PlanKind? claimKind, double? claimEnd, double now, string expected)
    {
        var nowUtc = At(now)!.Value;
        var plan = PlanFor(policy, At(policyEnd), nowUtc);

        var reason = PlanAccess.RevocationReason(plan, claimKind, At(claimEnd), nowUtc);

        reason.Should().Be(expected switch
        {
            "ok" => null,
            "locked" => PlanAccess.PlanLockedReason,
            "changed" => PlanAccess.PlanChangedReason,
            _ => throw new ArgumentOutOfRangeException(nameof(expected), expected, null)
        }, row);
    }

    [Theory]
    [InlineData("trial", 30d, 0d)]
    [InlineData("trial", 30d, 31d)]
    [InlineData("paid", 365d, 0d)]
    [InlineData("paid", 365d, 400d)]
    [InlineData("paid", null, 0d)]
    [InlineData("converted", null, 0d)]
    public void A_claim_issued_from_the_current_policy_is_never_revoked(string policy, double? policyEnd, double now)
    {
        // ไม่วนเตะ: login ใหม่ออก claim จาก ToDto ของนโยบาย ณ ตอนนั้น (JwtTokenIssuer: plan = Kind · plan_exp = TrialEndsAt)
        var nowUtc = At(now)!.Value;
        var plan = PlanFor(policy, At(policyEnd), nowUtc);
        var dto = PlanAccess.ToDto(plan, nowUtc)!;
        PlanKindNames.TryParse(dto.Kind, out var claimKind).Should().BeTrue();

        PlanAccess.RevocationReason(plan, claimKind, dto.TrialEndsAt, nowUtc).Should().BeNull();
    }

    private static DateTime? At(double? daysFromClaimEnd) =>
        daysFromClaimEnd is { } days ? TrialEndsUtc.AddDays(days) : null;

    private static WorkspacePlan PlanFor(string policy, DateTime? expiresAt, DateTime nowUtc)
    {
        // อ่านอย่างเดียวตามเวลาเหมือน PlanPolicy (RevocationReason คิดจาก ExpiresAt เอง ค่านี้ใส่ให้ข้อมูลสมจริง)
        var readOnly = expiresAt is { } end && nowUtc >= end;
        return policy switch
        {
            "legacy" => WorkspacePlan.Legacy,
            "trial" => FakeWorkspacePolicy.Trial(expiresAt: expiresAt, readOnly: readOnly),
            "paid" => FakeWorkspacePolicy.Paid(expiresAt: expiresAt, readOnly: readOnly),
            "locked" => FakeWorkspacePolicy.Trial(expiresAt: expiresAt, readOnly: true, locked: true),
            // PlanPolicy: Converted = Paid ไม่มีวันหมด อ่านอย่างเดียวแบบไม่ขึ้นกับเวลา (ไม่อยู่ใน claim · filter บังคับเอง)
            "converted" => FakeWorkspacePolicy.Paid(expiresAt: null, readOnly: true),
            _ => throw new ArgumentOutOfRangeException(nameof(policy), policy, null)
        };
    }
}

public sealed class PlanClaimsTests
{
    [Fact]
    public void Token_carries_plan_claims_that_current_user_reads_back()
    {
        var ends = new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc);
        var token = Issuer().IssueBranchToken(User(new WorkspacePlanDto("trial", ends, 30, false, [])), 5001).Token;

        var current = CurrentUserFor(token);

        current.Plan.Should().Be(PlanKind.Trial);
        current.PlanExpiresAt.Should().Be(ends);
        current.ShardKey.Should().Be("sp1");
    }

    [Fact]
    public void Legacy_token_has_no_plan_claims()
    {
        var token = Issuer().IssueBranchToken(User(null), 105).Token;
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.Select(c => c.Type).ToList();

        claims.Should().NotContain(GarageClaims.Plan).And.NotContain(GarageClaims.PlanExpiresAt);
        CurrentUserFor(token).Plan.Should().BeNull();
    }

    [Fact]
    public void Paid_plan_without_end_date_has_no_plan_exp()
    {
        var token = Issuer().IssueMobileToken(User(new WorkspacePlanDto("paid", null, null, false, [])), 5001).Token;
        var current = CurrentUserFor(token);

        current.Plan.Should().Be(PlanKind.Paid);
        current.PlanExpiresAt.Should().BeNull();
    }

    private static JwtTokenIssuer Issuer() => new(
        Options.Create(new JwtOptions { Key = new string('k', 48) }), TimeProvider.System);

    private static AuthUserDto User(WorkspacePlanDto? plan) => new(
        9001, "SP1001.M01", "เจ้าของ", "Manager", "ผู้จัดการสาขา", false, plan is null ? "db2" : "sp1",
        9101, null, null, true, true, plan, null);

    private static CurrentUser CurrentUserFor(string token)
    {
        // อ่าน claim ตามที่ออกจริง (ไม่ผ่าน JwtBearer — แค่ยืนยันชื่อ claim/รูปแบบค่าให้ตรงกันสองฝั่ง)
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(jwt.Claims, "test")) };
        return new CurrentUser(new HttpContextAccessor { HttpContext = http });
    }
}
