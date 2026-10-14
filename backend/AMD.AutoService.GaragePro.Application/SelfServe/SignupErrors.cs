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
        $"ส่งอีเมลรหัสยืนยันไม่สำเร็จ — กรุณาลองใหม่อีกครั้งในอีกสักครู่ หรือสมัครด้วย Google หรือติดต่อ {PlanErrors.ContactLine}");

    /// <summary>อ่านรายชื่อจังหวัดจากฐานอ้างอิงไม่ได้ (503) — ช่องจังหวัดไม่บังคับ เว็บซ่อนช่องแล้วสมัครต่อได้</summary>
    public const string LookupUnavailableCode = "SIGNUP_LOOKUP_UNAVAILABLE";

    public static ApiError LookupUnavailable() => new(LookupUnavailableCode,
        "โหลดรายชื่อจังหวัดไม่ได้ในขณะนี้ — เว้นช่องจังหวัดไว้แล้วสมัครต่อได้ (ไม่บังคับ)");

    /// <summary>HTTP status ของทุกรหัสที่ signup คืน — ไม่รู้จัก = 422 (validation)</summary>
    public static int StatusCodeFor(string? code) => code switch
    {
        EmailSendFailedCode => 503,
        LookupUnavailableCode => 503,
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
