using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.Auth;

/// <summary>คำขอ <c>POST /api/v1/auth/google</c> — ID token จาก Google Identity Services (ปุ่ม "เข้าสู่ระบบด้วย Google")</summary>
public sealed record GoogleLoginRequest(string? IdToken);

public interface IGoogleLoginService
{
    Task<Result<LoginResultDto>> LoginAsync(GoogleLoginRequest request, CancellationToken ct = default);
}

/// <summary>
/// เข้าสู่ระบบด้วย Google สำหรับเจ้าของอู่ที่สมัครเอง (B5) — ไม่สร้างบัญชีใหม่ (สมัครอยู่ที่ /public/signup/google ของ S1)
/// · ลำดับ: ตรวจ token กับ Google (audience = SelfServe:Google:ClientId) → ต้อง email_verified → normalize อีเมล
///   → sp_Account ที่ Active → ออก session ในนาม [User] ของ sp1 ผ่าน <see cref="IAuthService.IssueSessionForUserAsync"/>
///   (เงื่อนไขหลังยืนยันตัวตนชุดเดียวกับ login ด้วยรหัสผ่าน: เป็นพนักงาน · มีสาขา · แพ็กเกจไม่ถูกล็อก)
/// · บัญชีที่ใช้ได้: Provider = Google (ถ้าเก็บ sub ไว้ต้องตรงกับ token — อีเมลของ Google Workspace ถูกโอนให้คนอื่นได้
///   แต่ sub ไม่เปลี่ยน) หรือ Provider = Email ที่ยืนยันอีเมลแล้ว (Google พิสูจน์ความเป็นเจ้าของอีเมลเดียวกันแล้ว)
/// · ลงทะเบียนเฉพาะเมื่อตั้ง self-serve · ตัวตรวจ token (S1) resolve แบบ optional — ไม่มี = ปิดให้บริการ (503)
/// </summary>
public sealed class GoogleLoginService(
    IServiceProvider services,
    ISelfServeAccountReader accounts,
    IAuthService auth,
    string selfServeShardKey) : IGoogleLoginService
{
    public async Task<Result<LoginResultDto>> LoginAsync(GoogleLoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.IdToken))
            return Result<LoginResultDto>.Fail(SelfServeErrors.SignupGoogleInvalid());

        if (services.GetService(typeof(IGoogleIdTokenValidator)) is not IGoogleIdTokenValidator validator)
            return Result<LoginResultDto>.Fail(SelfServeAuthErrors.GoogleDisabled());

        var identity = await validator.ValidateAsync(request.IdToken.Trim(), ct);
        if (identity is null || !identity.EmailVerified)
            return Result<LoginResultDto>.Fail(SelfServeErrors.SignupGoogleInvalid());

        var normalized = EmailNormalizer.Normalize(identity.Email);
        if (normalized is null)
            return Result<LoginResultDto>.Fail(SelfServeErrors.SignupGoogleInvalid());

        var account = await accounts.FindActiveByNormalizedEmailAsync(normalized, ct);
        if (account?.UserId is not long userId || !CanSignInWithGoogle(account, identity))
            return Result<LoginResultDto>.Fail(SelfServeAuthErrors.GoogleNotRegistered());

        return await auth.IssueSessionForUserAsync(selfServeShardKey, userId, ct);
    }

    public static bool CanSignInWithGoogle(SelfServeLoginAccount account, GoogleIdentity identity) =>
        account.Provider switch
        {
            SelfServeAccountProvider.Google =>
                string.IsNullOrEmpty(account.GoogleSubject)
                || string.Equals(account.GoogleSubject, identity.Subject, StringComparison.Ordinal),
            SelfServeAccountProvider.Email => account.EmailVerified,
            _ => false
        };
}
