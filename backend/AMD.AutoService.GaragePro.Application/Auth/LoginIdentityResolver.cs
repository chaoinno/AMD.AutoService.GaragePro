using System.Text.RegularExpressions;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;

namespace AMD.AutoService.GaragePro.Application.Auth;

/// <summary>
/// ตัดสินว่าช่อง "อีเมลหรือรหัสพนักงาน" ของหน้า login หมายถึงบัญชีใน shard ไหน (แผน §3 · B5)
/// · มี <c>@</c> → อีเมลของอู่ที่สมัครเอง → <c>sp_Account.NormalizedEmail</c> (Status Active) → (sp1, LoginUserName)
/// · รูปแบบ <c>SP1001.T01</c> → (sp1, ตัวพิมพ์ใหญ่ทั้งหมด) — ช่าง/พนักงานที่เจ้าของอู่สร้าง
/// · อื่นๆ → (shard เริ่มต้น db2, ตามที่พิมพ์) = พฤติกรรมเดิมของลูกค้าเดิมทุกประการ
/// · [BIZ] client ไม่ได้ส่ง shard มาเอง — แอปบน store รุ่นเก่าจึงเข้าอู่ sp1 ได้โดยไม่ต้องอัปเดต
/// · [SECURITY] fail-open: ฐาน sp_ ล่ม/ไม่ได้ตั้งค่า → อีเมลถือว่า "ไม่พบ" (ได้ AUTH_INVALID_CREDENTIALS ตามปกติ)
///   ส่วนรหัสพนักงานเดิมไม่แตะฐาน sp_ เลย — ลูกค้าเดิมต้อง login ได้แม้ส่วน self-serve จะล่มทั้งหมด
/// · reader resolve แบบ optional จาก DI (มีเฉพาะเมื่อตั้ง SelfServe:ConnectionString) — ตัว resolver ลงทะเบียนเสมอ
/// </summary>
public sealed partial class LoginIdentityResolver : ILoginIdentityResolver
{
    private readonly IServiceProvider _services;
    private readonly string _legacyShardKey;
    private readonly string _selfServeShardKey;

    public LoginIdentityResolver(IServiceProvider services, string legacyShardKey, string selfServeShardKey)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyShardKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(selfServeShardKey);

        _services = services;
        _legacyShardKey = legacyShardKey;
        _selfServeShardKey = selfServeShardKey;
    }

    /// <summary>
    /// ชื่อผู้ใช้ของ sp1 — WorkspaceNo เริ่ม 1001 (IDENTITY) และ D4 ขยายเกิน 4 หลักเมื่อเกิน 9999 จึงรับ 4 หลักขึ้นไป
    /// (integrator 2026-10-14: <c>^SP\d{4,}\.</c> ไม่ให้อู่ที่ 10000 กลายเป็น login ของ db2) · ตรวจแล้ว db2 ไม่มี
    /// UserName ขึ้นต้น SP+ตัวเลข 4 หลักหรือมี '@' เลย (2026-10-14) จึงไม่ชนกับลูกค้าเดิม
    /// </summary>
    [GeneratedRegex(@"^SP\d{4,}\.[A-Za-z0-9]{1,10}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SelfServeUserNamePattern();

    public static bool IsSelfServeUserName(string? input) =>
        input is not null && SelfServeUserNamePattern().IsMatch(input.Trim());

    public async Task<LoginIdentity> ResolveAsync(string input, CancellationToken ct = default)
    {
        var trimmed = (input ?? string.Empty).Trim();

        if (trimmed.Contains('@'))
            return new LoginIdentity(_selfServeShardKey, await FindUserNameByEmailAsync(trimmed, ct) ?? string.Empty);

        if (SelfServeUserNamePattern().IsMatch(trimmed))
            return new LoginIdentity(_selfServeShardKey, trimmed.ToUpperInvariant());

        return new LoginIdentity(_legacyShardKey, trimmed);
    }

    /// <summary>null = ไม่พบ (รวมถึงอีเมลผิดรูป · ไม่ได้ตั้ง self-serve · ฐาน sp_ ล่ม) — ผู้เรียกตอบเหมือนรหัสผ่านผิด</summary>
    private async Task<string?> FindUserNameByEmailAsync(string email, CancellationToken ct)
    {
        var normalized = EmailNormalizer.Normalize(email);
        if (normalized is null) return null;

        var reader = _services.GetService(typeof(ISelfServeAccountReader)) as ISelfServeAccountReader;
        if (reader is null) return null;

        try
        {
            var account = await reader.FindActiveByNormalizedEmailAsync(normalized, ct);
            return string.IsNullOrWhiteSpace(account?.LoginUserName) ? null : account.LoginUserName;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // fail-open — ตัว reader (Infrastructure) เขียน log warning ก่อนโยนแล้ว (Application ไม่มี logging)
            return null;
        }
    }
}
