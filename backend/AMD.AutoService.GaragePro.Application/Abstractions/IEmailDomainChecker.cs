namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// ผลตรวจโดเมนอีเมลตอนสมัคร (docs/12 §7 ชั้น 2–3)
/// · <see cref="Disposable"/> → SIGNUP_EMAIL_DISPOSABLE · <see cref="NoMailServer"/> (NXDOMAIN/ไม่มี MX และ A) → SIGNUP_EMAIL_DOMAIN_INVALID
/// · <see cref="Unknown"/> = resolver ล่ม/timeout → ปล่อยผ่าน + log (ไม่ให้ DNS ของเราล่มแล้วสมัครไม่ได้ทั้งระบบ)
/// </summary>
public enum EmailDomainVerdict
{
    Ok,
    Disposable,
    NoMailServer,
    Unknown
}

public interface IEmailDomainChecker
{
    /// <param name="domain">ส่วนหลัง @ ที่ lowercase แล้ว (ผู้เรียก normalize ก่อน)</param>
    /// <param name="ct">ยกเลิกการรอ DNS</param>
    Task<EmailDomainVerdict> CheckAsync(string domain, CancellationToken ct = default);
}
