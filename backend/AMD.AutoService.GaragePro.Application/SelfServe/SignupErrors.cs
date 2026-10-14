using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// error code ของ signup ที่ไม่มีในสัญญากลาง <see cref="SelfServeErrors"/> (S1 เป็นเจ้าของ) — รหัสอื่นทั้งหมดใช้ของ SelfServeErrors
/// </summary>
public static class SignupErrors
{
    /// <summary>ผู้ให้บริการอีเมลปฏิเสธ/ล่ม — รหัส OTP ถูกสร้างแล้วแต่ส่งไม่ถึง (503 · ลองใหม่หลังครบเวลาขอรหัสใหม่)</summary>
    public const string EmailSendFailedCode = "SIGNUP_EMAIL_SEND_FAILED";

    public static ApiError EmailSendFailed() => new(EmailSendFailedCode,
        $"ส่งอีเมลรหัสยืนยันไม่สำเร็จ กรุณาลองใหม่อีกครั้งในอีกสักครู่ หรือสมัครด้วย Google หรือติดต่อ {PlanErrors.ContactLine}");

    /// <summary>อ่านรายชื่อจังหวัดจากฐานอ้างอิงไม่ได้ (503) — ช่องจังหวัดไม่บังคับ เว็บซ่อนช่องแล้วสมัครต่อได้</summary>
    public const string LookupUnavailableCode = "SIGNUP_LOOKUP_UNAVAILABLE";

    public static ApiError LookupUnavailable() => new(LookupUnavailableCode,
        "โหลดรายชื่อจังหวัดไม่ได้ในขณะนี้ เว้นช่องจังหวัดไว้แล้วสมัครต่อได้ (ไม่บังคับ)");

    /// <summary>
    /// email/verify: รหัส OTP ถูกแต่รหัสผ่านไม่ตรงกับที่ตั้งตอน email/start (422 · field <c>password</c>) — นับเป็นการลองผิด
    /// หนึ่งครั้งเหมือนรหัสผิด (ครบ 5 ครั้ง = SIGNUP_OTP_LOCKED) · เกิดได้เมื่อมีคนเริ่มสมัครอีเมลนี้ใหม่ด้วยรหัสผ่านอื่น
    /// ระหว่างที่เจ้าของรอรหัส หรือเจ้าของพิมพ์รหัสผ่านผิดเองหลังโหลดหน้าใหม่
    /// </summary>
    public const string PasswordMismatchCode = "SIGNUP_PASSWORD_MISMATCH";

    public static ApiError PasswordMismatch(int? attemptsLeft = null) => new(PasswordMismatchCode,
        "รหัสผ่านไม่ตรงกับที่ตั้งไว้ตอนเริ่มสมัคร กรุณาตรวจสอบหรือเริ่มสมัครใหม่",
        "password",
        attemptsLeft is null ? null : new { attemptsLeft });

    /// <summary>HTTP status ของทุกรหัสที่ signup คืน — ไม่รู้จัก = 422 (validation)</summary>
    public static int StatusCodeFor(string? code) => code switch
    {
        EmailSendFailedCode => 503,
        LookupUnavailableCode => 503,
        PasswordMismatchCode => 422,
        _ => SelfServeErrors.StatusCodeFor(code) ?? 422
    };

    /// <summary>ค่า retryAfterSeconds ใน Details ของ error (OTP_THROTTLED) — controller ใช้ตั้ง header Retry-After</summary>
    public static int? RetryAfterSeconds(ApiError? error)
    {
        var details = error?.Details;
        var value = details?.GetType().GetProperty("retryAfterSeconds")?.GetValue(details);
        return value is int seconds && seconds > 0 ? seconds : null;
    }
}
