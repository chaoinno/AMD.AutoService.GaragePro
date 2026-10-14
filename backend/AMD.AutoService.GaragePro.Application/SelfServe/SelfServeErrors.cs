using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// error code ใหม่ของ self-serve (แผน §3 "Error codes ใหม่") นอกเหนือจาก <see cref="PlanErrors"/>
/// · ข้อความไทยพร้อมแสดงผล — client ห้ามแปล/แต่งใหม่ · S1 ปรับถ้อยคำได้ที่นี่ที่เดียว
/// · HTTP status ดู <see cref="StatusCodeFor"/> · ค่า <c>retryAfterSeconds</c> อยู่ใน <c>Details</c> (ApiError ไม่มีฟิลด์นี้)
/// · [SECURITY] ข้อความของ signup ต้องไม่เปิดเผยว่าอีเมลไหนมีบัญชีอยู่แล้วใน flow ที่ต้องกัน enumeration
///   (email/start ตอบ 200 เสมอ) — <see cref="SignupAccountExists"/> ใช้เฉพาะทาง Google ที่พิสูจน์เจ้าของอีเมลแล้ว
/// </summary>
public static class SelfServeErrors
{
    // ---------- auth / rate limit (B3) ----------
    public const string AuthRateLimitedCode = "AUTH_RATE_LIMITED";          // 429
    public const string AuthBranchForbiddenCode = "AUTH_BRANCH_FORBIDDEN";  // 403
    public const string SignupRateLimitedCode = "SIGNUP_RATE_LIMITED";      // 429

    // ---------- POS (B1) ----------
    public const string PosReceiptConflictCode = "POS_RECEIPT_CONFLICT";    // 409

    // ---------- signup (S1) ----------
    public const string SignupDisabledCode = "SIGNUP_DISABLED";                       // 503
    public const string SignupValidationCode = "SIGNUP_VALIDATION";                   // 422 (+field)
    public const string SignupBotCheckFailedCode = "SIGNUP_BOT_CHECK_FAILED";         // 422
    public const string SignupEmailDisposableCode = "SIGNUP_EMAIL_DISPOSABLE";        // 422
    public const string SignupEmailDomainInvalidCode = "SIGNUP_EMAIL_DOMAIN_INVALID"; // 422
    public const string SignupOtpThrottledCode = "SIGNUP_OTP_THROTTLED";              // 429
    public const string SignupOtpInvalidCode = "SIGNUP_OTP_INVALID";                  // 422
    public const string SignupOtpExpiredCode = "SIGNUP_OTP_EXPIRED";                  // 422
    public const string SignupOtpLockedCode = "SIGNUP_OTP_LOCKED";                    // 422
    public const string SignupTokenInvalidCode = "SIGNUP_TOKEN_INVALID";              // 401
    public const string SignupGoogleInvalidCode = "SIGNUP_GOOGLE_INVALID";            // 422
    public const string SignupAccountExistsCode = "SIGNUP_ACCOUNT_EXISTS";            // 409
    public const string SignupProvisionFailedCode = "SIGNUP_PROVISION_FAILED";        // 503

    // ---------- /selfserve endpoints (S4) ----------
    public const string NotApplicableCode = "SELFSERVE_NOT_APPLICABLE";                          // 404
    public const string ExtensionAlreadyRequestedCode = "SELFSERVE_EXTENSION_ALREADY_REQUESTED"; // 409
    public const string SampleForbiddenCode = "SELFSERVE_SAMPLE_FORBIDDEN";                      // 403

    public static ApiError AuthRateLimited(int? retryAfterSeconds = null) => new(AuthRateLimitedCode,
        $"พยายามเข้าสู่ระบบผิดพลาดหลายครั้งเกินไป กรุณารอ{Wait(retryAfterSeconds)}แล้วลองใหม่",
        Details: RetryDetails(retryAfterSeconds));

    public static ApiError AuthBranchForbidden() => new(AuthBranchForbiddenCode,
        "ไม่มีสิทธิ์เข้าถึงสาขานี้");

    public static ApiError SignupRateLimited(int? retryAfterSeconds = null) => new(SignupRateLimitedCode,
        $"ส่งคำขอสมัครถี่เกินไป กรุณารอ{Wait(retryAfterSeconds)}แล้วลองใหม่ หรือติดต่อ {PlanErrors.ContactLine}",
        Details: RetryDetails(retryAfterSeconds));

    public static ApiError PosReceiptConflict() => new(PosReceiptConflictCode,
        "เลขที่ใบเสร็จถูกใช้ไปแล้วจากการออกใบเสร็จพร้อมกัน กรุณาลองออกใบเสร็จอีกครั้ง");

    public static ApiError SignupDisabled() => new(SignupDisabledCode,
        $"ระบบสมัครทดลองใช้ปิดให้บริการชั่วคราว ติดต่อ {PlanErrors.ContactLine}");

    public static ApiError SignupValidation(string messageTh, string? field = null) =>
        new(SignupValidationCode, messageTh, field);

    public static ApiError SignupBotCheckFailed() => new(SignupBotCheckFailedCode,
        "การยืนยันว่าไม่ใช่บอทไม่ผ่าน กรุณาลองใหม่อีกครั้ง");

    public static ApiError SignupEmailDisposable() => new(SignupEmailDisposableCode,
        "ไม่รองรับอีเมลชั่วคราว กรุณาใช้อีเมลที่ใช้งานจริง", "email");

    public static ApiError SignupEmailDomainInvalid() => new(SignupEmailDomainInvalidCode,
        "โดเมนของอีเมลนี้รับอีเมลไม่ได้ กรุณาตรวจสอบตัวสะกดของอีเมล", "email");

    public static ApiError SignupOtpThrottled(int? retryAfterSeconds = null) => new(SignupOtpThrottledCode,
        $"ขอรหัสยืนยันถี่เกินไป กรุณารอ{Wait(retryAfterSeconds)}แล้วขอรหัสใหม่",
        Details: RetryDetails(retryAfterSeconds));

    public static ApiError SignupOtpInvalid(int? attemptsLeft = null) => new(SignupOtpInvalidCode,
        attemptsLeft is int left ? $"รหัสยืนยันไม่ถูกต้อง (ลองได้อีก {left} ครั้ง)" : "รหัสยืนยันไม่ถูกต้อง",
        "code",
        attemptsLeft is null ? null : new { attemptsLeft });

    public static ApiError SignupOtpExpired() => new(SignupOtpExpiredCode,
        "รหัสยืนยันหมดอายุแล้ว กรุณากดขอรหัสใหม่", "code");

    public static ApiError SignupOtpLocked() => new(SignupOtpLockedCode,
        "ใส่รหัสยืนยันผิดเกินกำหนด กรุณากดขอรหัสใหม่", "code");

    public static ApiError SignupTokenInvalid() => new(SignupTokenInvalidCode,
        "ขั้นตอนการสมัครหมดอายุหรือไม่ถูกต้อง กรุณาเริ่มสมัครใหม่อีกครั้ง");

    public static ApiError SignupGoogleInvalid() => new(SignupGoogleInvalidCode,
        "ยืนยันบัญชี Google ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง");

    public static ApiError SignupAccountExists() => new(SignupAccountExistsCode,
        "อีเมลนี้มีบัญชีอยู่แล้ว กรุณาเข้าสู่ระบบ หรือใช้อีเมลอื่น", "email");

    public static ApiError SignupProvisionFailed() => new(SignupProvisionFailedCode,
        $"สร้างอู่ไม่สำเร็จ กรุณาลองใหม่อีกครั้ง หรือติดต่อ {PlanErrors.ContactLine}");

    public static ApiError NotApplicable() => new(NotApplicableCode,
        "ใช้ได้เฉพาะอู่ที่สมัครทดลองใช้ด้วยตัวเองเท่านั้น");

    public static ApiError ExtensionAlreadyRequested() => new(ExtensionAlreadyRequestedCode,
        "ส่งคำขอต่ออายุไปแล้ววันนี้ ทีมงานจะติดต่อกลับโดยเร็ว");

    public static ApiError SampleForbidden() => new(SampleForbiddenCode,
        "เฉพาะเจ้าของอู่ (ผู้จัดการ) เท่านั้นที่จัดการข้อมูลตัวอย่างได้");

    /// <summary>HTTP status ของรหัสในชุดนี้ — null = ไม่ใช่รหัสของ self-serve</summary>
    public static int? StatusCodeFor(string? code) => code switch
    {
        AuthRateLimitedCode => 429,
        AuthBranchForbiddenCode => 403,
        SignupRateLimitedCode => 429,
        PosReceiptConflictCode => 409,
        SignupDisabledCode => 503,
        SignupValidationCode => 422,
        SignupBotCheckFailedCode => 422,
        SignupEmailDisposableCode => 422,
        SignupEmailDomainInvalidCode => 422,
        SignupOtpThrottledCode => 429,
        SignupOtpInvalidCode => 422,
        SignupOtpExpiredCode => 422,
        SignupOtpLockedCode => 422,
        SignupTokenInvalidCode => 401,
        SignupGoogleInvalidCode => 422,
        SignupAccountExistsCode => 409,
        SignupProvisionFailedCode => 503,
        NotApplicableCode => 404,
        ExtensionAlreadyRequestedCode => 409,
        SampleForbiddenCode => 403,
        _ => null
    };

    private static string Wait(int? seconds) => seconds is int s && s > 0
        ? s >= 60 ? $" {(int)Math.Ceiling(s / 60.0)} นาที " : $" {s} วินาที "
        : "สักครู่";

    private static object? RetryDetails(int? retryAfterSeconds) =>
        retryAfterSeconds is null ? null : new { retryAfterSeconds };
}
