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

    [Fact]
    public void Revocation_rules()
    {
        var now = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        var end = now.AddDays(5);

        PlanAccess.RevocationReason(WorkspacePlan.Legacy, null, null, now).Should().BeNull();
        PlanAccess.RevocationReason(FakeWorkspacePolicy.Trial(expiresAt: end), PlanKind.Trial, end, now).Should().BeNull();
        // ต่ออายุก่อนหมด — ทั้ง token และนโยบายยังเขียนได้ ไม่เตะ
        PlanAccess.RevocationReason(FakeWorkspacePolicy.Trial(expiresAt: end.AddDays(30)), PlanKind.Trial, end, now).Should().BeNull();
        // หมดอายุตามธรรมชาติ — เปลี่ยนพร้อมกันทั้งคู่ ไม่เตะ
        PlanAccess.RevocationReason(FakeWorkspacePolicy.Trial(expiresAt: end, readOnly: true), PlanKind.Trial, end, end.AddHours(1))
            .Should().BeNull();
        // ต่ออายุหลังหมด — token บอกอ่านอย่างเดียว นโยบายเขียนได้ → login ใหม่ให้ได้ claim ใหม่
        PlanAccess.RevocationReason(FakeWorkspacePolicy.Trial(expiresAt: end.AddDays(30)), PlanKind.Trial, end, end.AddDays(1))
            .Should().Be(PlanAccess.PlanChangedReason);
        PlanAccess.RevocationReason(FakeWorkspacePolicy.Paid(), PlanKind.Trial, end, now).Should().Be(PlanAccess.PlanChangedReason);
        PlanAccess.RevocationReason(FakeWorkspacePolicy.Trial(expiresAt: end), null, null, now).Should().Be(PlanAccess.PlanChangedReason);
        PlanAccess.RevocationReason(FakeWorkspacePolicy.Trial(readOnly: true, locked: true), PlanKind.Trial, end, now)
            .Should().Be(PlanAccess.PlanLockedReason);
        // paid ที่ย้ายไปสาขาจริงแล้ว (อ่านอย่างเดียวแบบไม่ขึ้นกับเวลา) ต้องไม่วนเตะออก — filter บังคับแทน
        PlanAccess.RevocationReason(FakeWorkspacePolicy.Paid(readOnly: true), PlanKind.Paid, null, now).Should().BeNull();
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
