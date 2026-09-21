using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AMD.AutoService.GaragePro.API.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Key { get; set; } = string.Empty;
    public string Issuer { get; set; } = "AMD.AutoService.GaragePro";
    public string Audience { get; set; } = "AMD.AutoService.GaragePro.Clients";

    /// <summary>token ก่อนเลือกสาขา/กะ — อายุสั้น ใช้แค่เดินต่อให้จบขั้นตอน login</summary>
    public int PreSessionMinutes { get; set; } = 15;

    /// <summary>token ใช้งานจริง — ยาวพอสำหรับหนึ่งกะ</summary>
    public int SessionHours { get; set; } = 12;
}

/// <summary>ชื่อ claim ที่ใช้ร่วมกันระหว่างตัวออก token และตัวอ่าน</summary>
public static class GarageClaims
{
    public const string ShardKey = "shard";
    public const string BranchId = "branch";
    public const string SessionId = "sid";
    public const string ShiftId = "shift";
    public const string DisplayName = "name_th";
    public const string IsAdministrator = "is_admin";

    /// <summary>true เมื่อยังไม่ได้เลือกสาขา/กะ — endpoint งานทั้งหมดต้องปฏิเสธ</summary>
    public const string PreSession = "pre_session";

    /// <summary>
    /// legacy Staff.Id — คนละค่ากับ sub (User.Id) · ต้องอยู่ใน token เพราะการอ้างถึง "ช่าง" ทุกที่ใช้ Staff.Id
    /// (QuotationLine.AssignedTechnicianId) และการไป lookup legacy ทุกคำขอจะไปเพิ่ม lock convoy ของ Garage DB
    /// </summary>
    public const string StaffId = "staff";
}

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : ITokenIssuer
{
    private readonly JwtOptions _options = options.Value;

    public (string Token, DateTime ExpiresAt) IssuePreSessionToken(AuthUserDto user)
    {
        var expiresAt = clock.GetUtcNow().UtcDateTime.AddMinutes(_options.PreSessionMinutes);

        var claims = BaseClaims(user);
        claims.Add(new Claim(GarageClaims.PreSession, "true"));

        return (Write(claims, expiresAt), expiresAt);
    }

    public (string Token, DateTime ExpiresAt) IssueBranchToken(AuthUserDto user, int branchId)
    {
        var expiresAt = clock.GetUtcNow().UtcDateTime.AddHours(_options.SessionHours);

        var claims = BaseClaims(user);
        claims.Add(new Claim(GarageClaims.BranchId, branchId.ToString()));

        return (Write(claims, expiresAt), expiresAt);
    }

    public (string Token, DateTime ExpiresAt) IssueSessionToken(AuthUserDto user, ShiftSession session)
    {
        var expiresAt = clock.GetUtcNow().UtcDateTime.AddHours(_options.SessionHours);

        var claims = BaseClaims(user);
        claims.Add(new Claim(GarageClaims.BranchId, session.LegacyBranchId.ToString()));
        claims.Add(new Claim(GarageClaims.SessionId, session.Id.ToString()));
        claims.Add(new Claim(GarageClaims.ShiftId, session.ShiftId.ToString()));

        return (Write(claims, expiresAt), expiresAt);
    }

    private List<Claim> BaseClaims(AuthUserDto user)
    {
        List<Claim> claims =
        [
            new(JwtRegisteredClaimNames.Sub, user.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(ClaimTypes.Role, user.Role),
            new(GarageClaims.IsAdministrator, user.IsAdministrator.ToString().ToLowerInvariant()),
            // ชื่อภาษาไทยอยู่ใน claim ได้ (JWT เป็น UTF-8) ต่างจาก HTTP header ที่รับแต่ ASCII
            new(GarageClaims.DisplayName, user.DisplayName),
            new(GarageClaims.ShardKey, user.ShardKey)
        ];

        // ในทางปฏิบัติมีค่าเสมอ เพราะ AuthService.LoginAsync ปฏิเสธบัญชีที่ StaffId เป็น null
        // ไปแล้วด้วย AUTH_NOT_STAFF — เช็คไว้เพื่อไม่ให้ claim ว่างหลุดเข้า token ถ้ากฎนั้นเปลี่ยน
        if (user.StaffId is long staffId)
            claims.Add(new Claim(GarageClaims.StaffId, staffId.ToString()));

        return claims;
    }

    private string Write(IEnumerable<Claim> claims, DateTime expiresAt)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: clock.GetUtcNow().UtcDateTime,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
