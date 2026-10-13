namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>ตัวตนจาก Google ID token ที่ตรวจลายเซ็น/audience แล้ว — <paramref name="Subject"/> คือ id ถาวร (อีเมลเปลี่ยนได้)</summary>
public sealed record GoogleIdentity(
    string Subject,
    string Email,
    bool EmailVerified,
    string? GivenName,
    string? FamilyName,
    string? Picture);

/// <summary>
/// ตรวจ ID token จาก Google Identity Services (signup/login ด้วย Google — แผน §3 · S1/B5)
/// · null = token ไม่ถูกต้อง/หมดอายุ/audience ไม่ตรง <c>SelfServe:Google:ClientId</c>
/// · ผู้เรียกต้องปฏิเสธเมื่อ <see cref="GoogleIdentity.EmailVerified"/> = false — Google ยืนยันอีเมลแทน OTP ของเรา
/// </summary>
public interface IGoogleIdTokenValidator
{
    Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default);
}
