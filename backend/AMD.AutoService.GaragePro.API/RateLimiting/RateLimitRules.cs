using System.Globalization;
using System.Net;
using System.Net.Sockets;
using AMD.AutoService.GaragePro.Application.SelfServe;

namespace AMD.AutoService.GaragePro.API.RateLimiting;

/// <summary>กลุ่มของคำขอที่ global limiter แยกถังให้ (ตัดสินจาก method + path ล้วน — ไม่ต้องมี endpoint)</summary>
public enum RateLimitedRoute
{
    /// <summary>ไม่อยู่ในกลุ่มที่จำกัดด้วย global limiter (endpoint ที่ต้องล็อกอินส่วนใหญ่)</summary>
    None,

    /// <summary><c>/api/v1/auth/login</c> และ <c>/api/v1/auth/google</c> (ในอนาคต)</summary>
    Login,

    /// <summary><c>POST /api/v1/public/signup/email/start</c> และ <c>POST .../google</c> — จุดเริ่มสมัคร (สร้างบัญชี/ส่งอีเมล)</summary>
    SignupEntry,

    /// <summary><c>/api/v1/public/signup/email/verify</c> และ <c>.../email/resend</c></summary>
    SignupOtp,

    /// <summary>path อื่นใต้ <c>/api/v1/public/signup</c> (config · provinces · workspace)</summary>
    SignupOther,

    /// <summary>path อื่นใต้ <c>/api/v1/public</c> (ฟอร์มติดต่อ) — ถังรวม <c>public-global</c> เดิม</summary>
    PublicOther,
}

/// <summary>ผลการปฏิเสธที่จะเขียนลง envelope + header <c>Retry-After</c></summary>
public sealed record RateLimitRejection(string Code, string MessageTh, int RetryAfterSeconds);

/// <summary>
/// กติกาล้วน (ไม่มี state) ของ rate limiter — แยกจาก <see cref="RateLimitingSetup"/> เพื่อให้ทดสอบได้โดยไม่ต้องยก host ขึ้นมา
/// · path เทียบแบบไม่สนตัวพิมพ์และไม่สน "/" ท้าย (<see cref="PathString.StartsWithSegments(PathString)"/>)
///   ตรงกับ routing ของ ASP.NET — ไม่งั้น <c>/API/V1/AUTH/LOGIN</c> หรือ <c>/login/</c> จะเลี่ยงเพดานได้
/// · route ของ signup มาจากสัญญาในแผน §3 (S1 ยังไม่ได้เขียน controller) — ถ้า S1 เปลี่ยน path ต้องแก้ที่นี่ด้วย
/// </summary>
public static class RateLimitRules
{
    public static readonly PathString LoginPath = "/api/v1/auth/login";
    public static readonly PathString GoogleLoginPath = "/api/v1/auth/google";
    public static readonly PathString PublicPath = "/api/v1/public";
    public static readonly PathString SignupPath = "/api/v1/public/signup";

    private static readonly PathString[] SignupEntryPaths =
        ["/api/v1/public/signup/email/start", "/api/v1/public/signup/google"];

    private static readonly PathString[] SignupOtpPaths =
        ["/api/v1/public/signup/email/verify", "/api/v1/public/signup/email/resend"];

    // ---------- ตัวเลข (แผน §3 "Rate-limit policies") ----------
    public const int LoginPerMinute = 10;          // sliding ต่อ IP (policy auth-login)
    public const int LoginPerHour = 50;            // fixed ต่อ IP (global)
    public const int SignupEntryPerHour = 3;       // fixed ต่อ IP (global + policy public-signup)
    public const int SignupEntryPerDay = 10;       // fixed ต่อ IP (global)
    public const int OtpPerHour = 10;              // fixed ต่อ IP (global + policy public-otp)

    /// <summary>
    /// [ASSUME] เพดานสำรองของทุกคำขอใต้ /public/signup ต่อ IP — สมัครหนึ่งรอบใช้ ~6–10 คำขอ
    /// (config · provinces · start · verify · resend · workspace · พิมพ์ OTP ผิด) จึงเผื่อ ~3 รอบเต็ม/ชม.
    /// ถ้าใช้ 3/ชม. กับทุก path ตาม brief เดิม รอบสมัครปกติรอบเดียวก็โดน 429 ก่อนถึงขั้นสร้างอู่
    /// </summary>
    public const int SignupAnyPerHour = 30;

    public const int PublicGlobalPerHour = 60;     // ถังรวมเดิมของฟอร์มติดต่อ (ไม่แยก IP)
    public const int ContactPerTenMinutes = 5;     // policy public-contact เดิม
    public const int PurchasingPinPerMinute = 10;  // policy purchasing-pin เดิม

    public static RateLimitedRoute Classify(string method, PathString path)
    {
        if (path.StartsWithSegments(LoginPath) || path.StartsWithSegments(GoogleLoginPath))
            return RateLimitedRoute.Login;

        if (path.StartsWithSegments(SignupPath))
        {
            // GET ไม่สร้างอะไร — นับเป็น "อื่นๆ" แม้ path จะตรง (กัน preflight/HEAD กินโควตาสมัคร)
            var isPost = HttpMethods.IsPost(method);
            if (isPost && SignupEntryPaths.Any(p => path.StartsWithSegments(p))) return RateLimitedRoute.SignupEntry;
            if (isPost && SignupOtpPaths.Any(p => path.StartsWithSegments(p))) return RateLimitedRoute.SignupOtp;
            return RateLimitedRoute.SignupOther;
        }

        return path.StartsWithSegments(PublicPath) ? RateLimitedRoute.PublicOther : RateLimitedRoute.None;
    }

    public static bool IsSignup(RateLimitedRoute route) =>
        route is RateLimitedRoute.SignupEntry or RateLimitedRoute.SignupOtp or RateLimitedRoute.SignupOther;

    /// <summary>
    /// key ของถังต่อ IP — IPv6 รวมเป็น /64 เพราะผู้ให้บริการแจก /64 ให้ลูกค้ารายเดียว ถ้าแยก /128
    /// ผู้โจมตีหมุนที่อยู่ภายใน /64 ของตัวเองได้ไม่จำกัด · IPv4-mapped (::ffff:a.b.c.d) แปลงกลับเป็น IPv4
    /// · RemoteIpAddress มาจาก X-Forwarded-For ตัวขวาสุดที่ Nginx เติม (Program.cs UseForwardedHeaders)
    ///   และ Nginx ต้องตั้ง real_ip จาก Cloudflare (deploy/production/nginx-ratelimit.conf) ไม่งั้นค่านี้คือ IP ของ Cloudflare edge
    /// </summary>
    public static string ClientKey(IPAddress? address)
    {
        if (address is null) return "unknown";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return address.ToString();

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }

    /// <summary>
    /// เลือก error code/ข้อความ/Retry-After ตาม policy ของ endpoint (ถ้ามี) แล้วค่อยตาม path
    /// — global limiter ปฏิเสธได้ก่อน endpoint policy เสมอ จึงต้องดู path ด้วย
    /// · <paramref name="retryAfter"/> มาจาก metadata ของ lease (fixed = ทั้ง window · sliding = หนึ่ง segment)
    ///   ไม่มีก็ใช้ความยาว window ของกลุ่มนั้นแทน
    /// </summary>
    public static RateLimitRejection DescribeRejection(
        string? policyName, string method, PathString path, TimeSpan? retryAfter)
    {
        var route = Classify(method, path);

        if (policyName == RateLimitPolicies.PurchasingPin)
            return new("PURCHASING_PIN_RATE_LIMIT",
                "ลองยืนยัน PIN หลายครั้งเกินไป กรุณารอ 1 นาทีแล้วลองใหม่",
                Seconds(retryAfter, TimeSpan.FromMinutes(1)));

        if (policyName == RateLimitPolicies.AuthLogin || route == RateLimitedRoute.Login)
            return new(SelfServeErrors.AuthRateLimitedCode,
                "พยายามเข้าสู่ระบบถี่เกินไป กรุณารอสักครู่แล้วลองใหม่",
                Seconds(retryAfter, TimeSpan.FromMinutes(1)));

        if (policyName is RateLimitPolicies.PublicSignup or RateLimitPolicies.PublicOtp || IsSignup(route))
        {
            var seconds = Seconds(retryAfter, TimeSpan.FromHours(1));
            var error = SelfServeErrors.SignupRateLimited(seconds);
            return new(error.Code, error.MessageTh, seconds);
        }

        return new("RATE_LIMITED",
            "ส่งข้อมูลถี่เกินไป กรุณารอสักครู่แล้วลองใหม่ หรือติดต่อทาง LINE @garagepro / โทร 090-996-6446",
            Seconds(retryAfter, policyName == RateLimitPolicies.PublicContact
                ? TimeSpan.FromMinutes(10)
                : TimeSpan.FromHours(1)));
    }

    public static string FormatRetryAfter(int seconds) => seconds.ToString(CultureInfo.InvariantCulture);

    private static int Seconds(TimeSpan? retryAfter, TimeSpan fallback)
    {
        var value = retryAfter is { } r && r > TimeSpan.Zero ? r : fallback;
        return Math.Max(1, (int)Math.Ceiling(value.TotalSeconds));
    }
}
