using System.Threading.RateLimiting;
using AMD.AutoService.GaragePro.API.RateLimiting;
using AMD.AutoService.GaragePro.Application.Abstractions;
using Microsoft.AspNetCore.RateLimiting;

// namespace เดียวกับ Program.cs (มี using อยู่แล้ว) — เรียก AddGarageProRateLimiting() ได้โดยไม่ต้องแก้ส่วน using ที่หลาย WP แตะ
namespace AMD.AutoService.GaragePro.API;

/// <summary>
/// ASP.NET rate limiter ทั้งหมดของ API (เดิมอยู่ใน Program.cs) — กติกาล้วนอยู่ที่ <see cref="RateLimitRules"/>
///
/// [SECURITY] สองชั้น:
/// 1. global limiter (ทุกคำขอ ก่อน endpoint policy) แยกถังตาม path:
///    ฟอร์มติดต่อ = ถังรวม <c>public-global</c> 60/ชม. (เดิม) · signup แยกถังต่อ IP <c>public-signup:{ip}</c> (ไม่แย่งโควตาฟอร์มติดต่อ)
///    · login เพดาน 50/ชม. ต่อ IP
/// 2. endpoint policy (<see cref="RateLimitPolicies"/>) ผ่าน <c>[EnableRateLimiting]</c>
/// · ต่อ username (กันเดารหัสผ่านทีละบัญชีจากหลาย IP) อยู่ที่ <c>ILoginAttemptLimiter</c> ใน AuthService ไม่ใช่ที่นี่
///   เพราะ partitioner อ่าน JSON body ไม่ได้
/// · ชั้นนอกสุดคือ Nginx <c>limit_req</c> (deploy/production/nginx-ratelimit.conf) — ชั้นนี้ยังต้องมีเพราะ dev/เครื่องที่ไม่ผ่าน Nginx
/// </summary>
public static class RateLimitingSetup
{
    public static IServiceCollection AddGarageProRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            o.AddPolicy(RateLimitPolicies.PurchasingPin, context =>
            {
                var user = context.RequestServices.GetRequiredService<ICurrentUser>();
                return RateLimitPartition.GetFixedWindowLimiter($"{user.ShardKey}:{user.BranchId}:{user.UserId}", _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = RateLimitRules.PurchasingPinPerMinute, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true
                });
            });

            o.AddPolicy(RateLimitPolicies.PackagePaymentSubmit, PackagePaymentSubmitPartition);

            o.AddPolicy(RateLimitPolicies.PublicContact, context =>
                RateLimitPartition.GetFixedWindowLimiter(Ip(context),
                    _ => Fixed(RateLimitRules.ContactPerTenMinutes, TimeSpan.FromMinutes(10))));

            o.AddPolicy(RateLimitPolicies.AuthLogin, LoginPartition);

            o.AddPolicy(RateLimitPolicies.PublicSignup, context =>
                RateLimitPartition.GetFixedWindowLimiter($"policy-signup:{Ip(context)}",
                    _ => Fixed(RateLimitRules.SignupEntryPerHour, TimeSpan.FromHours(1))));

            o.AddPolicy(RateLimitPolicies.PublicOtp, context =>
                RateLimitPartition.GetFixedWindowLimiter($"policy-otp:{Ip(context)}",
                    _ => Fixed(RateLimitRules.OtpPerHour, TimeSpan.FromHours(1))));

            o.GlobalLimiter = BuildGlobalLimiter();

            o.OnRejected = async (context, ct) =>
            {
                var http = context.HttpContext;
                var policy = http.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName;
                TimeSpan? retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var after) ? after : null;
                var rejection = RateLimitRules.DescribeRejection(policy, http.Request.Method, http.Request.Path, retryAfter);

                http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                http.Response.ContentType = "application/json; charset=utf-8";
                // web client (api/client.ts) อ่าน header นี้เป็น retryAfterSeconds · details ซ้ำไว้ให้ client ที่อ่านแต่ body (มือถือ)
                http.Response.Headers.RetryAfter = RateLimitRules.FormatRetryAfter(rejection.RetryAfterSeconds);
                await http.Response.WriteAsJsonAsync(new
                {
                    success = false,
                    data = (object?)null,
                    error = new
                    {
                        code = rejection.Code,
                        messageTh = rejection.MessageTh,
                        details = new { retryAfterSeconds = rejection.RetryAfterSeconds }
                    },
                    traceId = http.TraceIdentifier
                }, ct);
            };
        });

    /// <summary>
    /// policy <c>package-payment-submit</c>: ต่ออู่ (shard:สาขาจาก JWT) 10 ครั้ง/ชม. · ผู้ใช้หลายคนของอู่เดียวกันใช้ถังเดียว
    /// (แจ้งชำระเป็นเรื่องของอู่ ไม่ใช่ของคน) · endpoint ต้องล็อกอินแล้ว (rate limiter อยู่หลัง UseAuthorization)
    /// </summary>
    public static RateLimitPartition<string> PackagePaymentSubmitPartition(HttpContext context)
    {
        var user = context.RequestServices.GetRequiredService<ICurrentUser>();
        return RateLimitPartition.GetFixedWindowLimiter($"package-submit:{user.ShardKey}:{user.BranchId}",
            _ => Fixed(RateLimitRules.PackagePaymentSubmitPerHour, TimeSpan.FromHours(1)));
    }

    /// <summary>policy <c>auth-login</c>: sliding ต่อ IP 10 ครั้ง/นาที (6 ช่อง ช่องละ 10 วิ) ไม่ต่อคิว</summary>
    public static RateLimitPartition<string> LoginPartition(HttpContext context) =>
        RateLimitPartition.GetSlidingWindowLimiter($"auth-login:{Ip(context)}", _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = RateLimitRules.LoginPerMinute,
            Window = TimeSpan.FromMinutes(1),
            SegmentsPerWindow = 6,
            QueueLimit = 0,
            AutoReplenishment = true
        });

    /// <summary>
    /// global limiter — ทุกคำขอผ่านทุกชั้นของ chain (ชั้นที่ไม่เกี่ยวคืน NoLimiter) ชั้นไหนเต็มก็ปฏิเสธ
    /// · ฟอร์มติดต่อ (path อื่นใต้ /public) ยังเป็นถังรวม 60/ชม. เหมือนเดิม แต่ **ไม่รวม /public/signup** อีกต่อไป
    /// · public ทำเป็น method แยกเพื่อให้เทสต์สร้าง limiter จริงแล้วยิง DefaultHttpContext ได้
    /// </summary>
    public static PartitionedRateLimiter<HttpContext> BuildGlobalLimiter() => PartitionedRateLimiter.CreateChained(
        Layer(RateLimitedRoute.PublicOther, _ => "public-global",
            RateLimitRules.PublicGlobalPerHour, TimeSpan.FromHours(1)),
        Layer(RateLimitedRoute.PublicPackageSlip, ip => $"public-package-slip:{ip}",
            RateLimitRules.PackageSlipPerHour, TimeSpan.FromHours(1)),
        Layer(RateLimitedRoute.SignupEntry, ip => $"public-signup:{ip}",
            RateLimitRules.SignupEntryPerHour, TimeSpan.FromHours(1)),
        Layer(RateLimitedRoute.SignupEntry, ip => $"public-signup-day:{ip}",
            RateLimitRules.SignupEntryPerDay, TimeSpan.FromDays(1)),
        Layer(RateLimitedRoute.SignupOtp, ip => $"public-otp:{ip}",
            RateLimitRules.OtpPerHour, TimeSpan.FromHours(1)),
        Layer(null, ip => $"public-signup-any:{ip}",
            RateLimitRules.SignupAnyPerHour, TimeSpan.FromHours(1)),
        Layer(RateLimitedRoute.Login, ip => $"auth-login-hour:{ip}",
            RateLimitRules.LoginPerHour, TimeSpan.FromHours(1)));

    /// <param name="route">null = ทุกกลุ่มของ signup</param>
    private static PartitionedRateLimiter<HttpContext> Layer(
        RateLimitedRoute? route, Func<string, string> key, int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            var actual = RateLimitRules.Classify(context.Request.Method, context.Request.Path);
            var applies = route is null ? RateLimitRules.IsSignup(actual) : actual == route;
            return applies
                ? RateLimitPartition.GetFixedWindowLimiter(key(Ip(context)), _ => Fixed(permits, window))
                : RateLimitPartition.GetNoLimiter("none");
        });

    private static FixedWindowRateLimiterOptions Fixed(int permits, TimeSpan window) => new()
    {
        PermitLimit = permits, Window = window, QueueLimit = 0, AutoReplenishment = true
    };

    private static string Ip(HttpContext context) => RateLimitRules.ClientKey(context.Connection.RemoteIpAddress);
}
