using AMD.AutoService.GaragePro.API.Controllers;

namespace AMD.AutoService.GaragePro.API.RateLimiting;

/// <summary>
/// ชื่อ policy ของ ASP.NET rate limiter — ใช้กับ <c>[EnableRateLimiting(...)]</c> และตัดสิน error code ตอนถูกปฏิเสธ
/// (<see cref="RateLimitRules.DescribeRejection"/>) · ลงทะเบียนที่ <see cref="RateLimitingSetup"/> ที่เดียว
/// </summary>
public static class RateLimitPolicies
{
    /// <summary>
    /// [SECURITY] login ต่อ IP: sliding 10 ครั้ง/นาที — ใช้ซ้ำกับ <c>POST /auth/google</c> ได้เลย
    /// (เพดาน 50 ครั้ง/ชม. ต่อ IP อยู่ใน global limiter ตาม path · ต่อ username อยู่ที่ <c>ILoginAttemptLimiter</c>)
    /// </summary>
    public const string AuthLogin = "auth-login";

    /// <summary>
    /// สมัครทดลองใช้ต่อ IP 3 ครั้ง/ชม. — **global limiter บังคับกับ <c>email/start</c> และ <c>google</c> ตาม path อยู่แล้ว**
    /// (3/ชม. + 10/วัน) ติด attribute นี้ซ้ำได้โดยพฤติกรรมไม่เปลี่ยน (ตัวเลขเดียวกัน) แต่ไม่จำเป็น
    /// </summary>
    public const string PublicSignup = "public-signup";

    /// <summary>ยืนยัน/ขอ OTP ใหม่ต่อ IP 10 ครั้ง/ชม. — global limiter บังคับกับ <c>email/verify</c>/<c>email/resend</c> ตาม path อยู่แล้ว</summary>
    public const string PublicOtp = "public-otp";

    /// <summary>ยืนยัน PIN อนุมัติจัดซื้อ ต่อ shard:สาขา:ผู้ใช้ 10 ครั้ง/นาที (เดิม — ยังเขียนเป็น string ใน PurchasingControllers)</summary>
    public const string PurchasingPin = "purchasing-pin";

    /// <summary>ฟอร์มติดต่อ ต่อ IP 5 ครั้ง/10 นาที (เดิม)</summary>
    public const string PublicContact = PublicContactController.RateLimitPolicy;

    /// <summary>แจ้งชำระแพ็กเกจ ต่อ shard:สาขา (ต่ออู่) 10 ครั้ง/ชม. (docs/17 §5.1) · นับทุกคำขอรวมที่ถูกปฏิเสธ (กันยิงอัปโหลดไฟล์ซ้ำ)</summary>
    public const string PackagePaymentSubmit = "package-payment-submit";
}
