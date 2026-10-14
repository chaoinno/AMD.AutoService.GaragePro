using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// ส่วนของแถว sp_Account ที่การเข้าสู่ระบบต้องใช้ (อีเมล → ชื่อผู้ใช้ใน shard sp1 · Google login)
/// · ไม่มี PasswordHash ของ sp_Account โดยตั้งใจ — รหัสผ่านที่ใช้เข้าสู่ระบบคือ [User].Password ของ sp1 เสมอ
/// </summary>
public sealed record SelfServeLoginAccount(
    Guid AccountId,
    string NormalizedEmail,
    SelfServeAccountProvider Provider,
    string? GoogleSubject,
    bool EmailVerified,
    string? LoginUserName,
    long? UserId);

/// <summary>
/// อ่าน sp_Account / แตะ sp_Workspace สำหรับการเข้าสู่ระบบ (B5)
/// · ลงทะเบียนเฉพาะเมื่อตั้ง <c>SelfServe:ConnectionString</c> — ผู้ใช้ต้อง resolve แบบ optional
///   (<c>IServiceProvider.GetService</c> หรือพารามิเตอร์ที่เป็น null ได้) ไม่งั้น deployment ที่ไม่มี self-serve สตาร์ทไม่ได้
/// </summary>
public interface ISelfServeAccountReader
{
    /// <summary>บัญชีที่ Status = Active ของอีเมลที่ normalize แล้ว — null = ไม่มี (หรือยังไม่เปิดอู่ / ถูกระงับ / ถูกลบ)</summary>
    Task<SelfServeLoginAccount?> FindActiveByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default);

    /// <summary>
    /// บันทึกครั้งแรกที่มีคนในอู่เข้าแอปมือถือ (sp_Workspace.MobileLoginSeenAt — ใช้ใน checklist ต้อนรับ)
    /// · ตั้งค่าเฉพาะเมื่อยังว่าง (ครั้งแรกเท่านั้น) · best effort: implementation ต้องไม่โยน exception
    /// </summary>
    Task MarkMobileLoginSeenAsync(int branchId, CancellationToken ct = default);
}
