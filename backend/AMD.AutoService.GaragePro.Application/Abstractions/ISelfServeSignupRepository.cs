using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// ตาราง sp_* ที่ signup (S1) อ่าน/เขียน — sp_Account · sp_EmailOtp · sp_SignupAttempt · sp_BlockedDomain (+ อ่าน sp_Workspace)
/// · ทุกการอ่านคืน entity ที่ไม่ถูก track — แก้แล้วต้องส่งกลับผ่าน Update* เอง (ไม่มี SaveChanges ลอยๆ)
/// · การเขียนที่อาจชนกับ WorkspaceProvisioner (S2 เขียน sp_Account/sp_Workspace ระหว่างเปิดอู่) ใช้ UPDATE เฉพาะคอลัมน์
///   (<see cref="TryReplaceSetupTokenHashAsync"/> · <see cref="MarkAccountActiveAsync"/> · <see cref="SetDuplicatePhoneFlagAsync"/>)
///   ห้ามเขียนทับทั้งแถวด้วยสำเนาที่อ่านไว้ก่อนเปิดอู่ — จะลบค่าที่ S2 เพิ่งตั้ง (Status/UserId/LoginUserName)
/// · ชน unique index → <see cref="UniqueConstraintViolationException"/>
/// </summary>
public interface ISelfServeSignupRepository
{
    Task<SelfServeAccount?> FindAccountByEmailAsync(string normalizedEmail, CancellationToken ct = default);
    Task<SelfServeAccount?> FindAccountByGoogleSubjectAsync(string subject, CancellationToken ct = default);

    /// <summary>
    /// บัญชีที่ <c>SetupTokenHash</c> = <paramref name="tokenHash"/> (ยังไม่ถูกจอง) หรือ = <c>{tokenHash}:{requestId}</c>
    /// (ถูกจองให้คำขอเปิดอู่ <c>requestId</c> แล้ว — ดู SignupService.ClaimTokenAsync)
    /// </summary>
    Task<SelfServeAccount?> FindAccountBySetupTokenAsync(string tokenHash, CancellationToken ct = default);

    Task AddAccountAsync(SelfServeAccount account, CancellationToken ct = default);

    /// <summary>เขียนทับทั้งแถว — ใช้ได้เฉพาะก่อนเปิดอู่ (email/start · verify · google)</summary>
    Task UpdateAccountAsync(SelfServeAccount account, CancellationToken ct = default);

    /// <summary>compare-and-swap ของ SetupTokenHash — true เมื่อค่าเดิมเท่ากับ <paramref name="expected"/> และเปลี่ยนสำเร็จ</summary>
    Task<bool> TryReplaceSetupTokenHashAsync(Guid accountId, string expected, string replacement, CancellationToken ct = default);

    /// <summary>Status=Active + UserId/LoginUserName (ถ้ายังว่าง) — idempotent · ไม่แตะบัญชีที่ Suspended/Purged</summary>
    Task MarkAccountActiveAsync(Guid accountId, long userId, string loginUserName, DateTime now, CancellationToken ct = default);

    Task<EmailOtp?> GetOtpAsync(string normalizedEmail, CancellationToken ct = default);
    Task AddOtpAsync(EmailOtp otp, CancellationToken ct = default);
    Task UpdateOtpAsync(EmailOtp otp, CancellationToken ct = default);

    /// <summary>
    /// โดเมน (หรือโดเมนแม่ของมัน) อยู่ใน sp_BlockedDomain — ตารางว่าง (ยังไม่เคยนำเข้า) ใช้รายชื่อที่ฝังมากับ build แทน
    /// </summary>
    Task<bool> IsDomainBlockedAsync(string domain, CancellationToken ct = default);

    Task<Workspace?> FindWorkspaceAsync(Guid accountId, string signupRequestId, CancellationToken ct = default);

    /// <summary>เบอร์นี้เคยใช้เปิดอู่ของบัญชีอื่นแล้ว (docs/12 §7 ชั้น 5 — ไม่บล็อก แค่ติดธง)</summary>
    Task<bool> IsPhoneUsedByOtherAccountAsync(string phoneE164, Guid accountId, CancellationToken ct = default);

    Task SetDuplicatePhoneFlagAsync(Guid workspaceId, CancellationToken ct = default);

    Task AddAttemptAsync(SignupAttempt attempt, CancellationToken ct = default);
}
