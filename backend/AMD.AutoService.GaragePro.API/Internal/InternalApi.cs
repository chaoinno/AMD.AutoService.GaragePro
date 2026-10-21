using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Internal;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.API.Internal;

/// <summary>
/// API ภายในสำหรับ GaragePro Admin (docs/17 §8.3, §8.6) · ค่าคงที่ · error · การลงทะเบียน DI
/// · ทางเข้าเดียวคือ docker network <c>garagepro-servicepro</c> (<c>http://garagepro_service_api:8080</c>)
///   Nginx ของโดเมน API ตอบ 404 ให้ <c>/api/v1/internal</c> จากภายนอกเสมอ (marker <c>gp-api-internal-deny</c>)
/// · ไม่ใช้ JWT ของผู้ใช้ · ยืนยันตัวด้วย header <see cref="KeyHeader"/> (<see cref="InternalApiAttribute"/>)
/// </summary>
public static class InternalApi
{
    public const string RoutePrefix = "api/v1/internal";
    public const string KeyHeader = "X-Internal-Key";

    public static readonly PathString PathPrefix = new("/" + RoutePrefix);

    public const string AuthRequiredCode = "INTERNAL_AUTH_REQUIRED";              // 401
    public const string SourceUnavailableCode = "INTERNAL_SOURCE_UNAVAILABLE";    // 503

    public static ApiError AuthRequired() => new(AuthRequiredCode,
        "คำขอภายในไม่มีคีย์หรือคีย์ไม่ถูกต้อง ตรวจว่า SERVICEPRO_INTERNAL_KEY ของ Admin ตรงกับ Internal:ApiKey ของ ServicePro");

    public static ApiError SourceUnavailable() => new(SourceUnavailableCode,
        "อ่านข้อมูลจากฐานของ ServicePro ไม่สำเร็จ ลองใหม่อีกครั้ง (รายละเอียดอยู่ใน log ของ ServicePro API)");

    /// <summary>ไม่สนตัวพิมพ์ เพราะ routing ของ ASP.NET ไม่สน (ใช้ข้าม middleware ของ session/อ่านอย่างเดียว)</summary>
    public static bool IsInternalPath(PathString path) =>
        path.StartsWithSegments(PathPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// [SECURITY] เทียบแบบ constant-time: SHA-256 ของทั้งสองฝั่งได้ความยาวเท่ากันเสมอ แล้ว <see cref="CryptographicOperations.FixedTimeEquals"/>
    /// (เวลาไม่ขึ้นกับว่าตรงกันกี่ตัวอักษร และไม่บอกความยาวของคีย์จริง) · ตัดช่องว่างหัวท้ายของค่าที่ส่งมาแบบเดียวกับฝั่ง Admin
    /// </summary>
    public static bool KeyMatches(string? provided, string expected)
    {
        ArgumentException.ThrowIfNullOrEmpty(expected);
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(provided?.Trim() ?? string.Empty));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    public static IServiceCollection AddInternalApi(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<InternalOptions>(configuration.GetSection(InternalOptions.SectionName));

        // ส่วนของ sp1 มีเฉพาะเมื่อตั้งฐาน self-serve (SelfServeAdminService ลงทะเบียนภายใต้เงื่อนไขนั้น) · ไม่มี = ส่งแค่สาขา db2
        services.AddScoped(sp =>
        {
            var admin = sp.GetService<SelfServeAdminService>();
            var payments = sp.GetService<IPackagePaymentRepository>();
            var selfServe = admin is not null && payments is not null
                ? new SelfServeSiteSources(admin, payments, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value)
                : null;
            return new InternalSitesService(
                sp.GetRequiredService<IInternalSiteReader>(),
                sp.GetService<TimeProvider>() ?? TimeProvider.System,
                sp.GetRequiredService<IOptions<InternalOptions>>().Value.GarageProShardKey,
                selfServe);
        });

        // ระยะ B: ตรวจสลิป/อนุมัติ/ปฏิเสธ · ทุกตัวที่ใช้ลงทะเบียนเฉพาะเมื่อตั้งฐาน self-serve · ไม่ตั้ง = controller ตอบ 404
        // SELFSERVE_NOT_APPLICABLE (resolve แบบ optional)
        if (AMD.AutoService.GaragePro.Infrastructure.SelfServe.SelfServeInfrastructure.IsConfigured(configuration))
            services.AddScoped(sp => ActivatorUtilities.CreateInstance<InternalPackagePaymentsService>(sp));

        return services;
    }

    /// <summary>เตือนตอนสตาร์ตเมื่อคีย์สั้นเกิน (ปิดอยู่) · [SECURITY] ไม่พิมพ์ค่าคีย์หรือความยาว</summary>
    public static void WarnIfMisconfigured(this WebApplication app)
    {
        var options = app.Services.GetRequiredService<IOptions<InternalOptions>>().Value;
        if (options.IsTooShort)
            app.Logger.LogWarning(
                "ตั้ง {Section}:ApiKey ไว้แต่สั้นกว่า {Min} ตัว API ภายใน (/api/v1/internal) จึงปิดอยู่ ทุก route ตอบ 404",
                InternalOptions.SectionName, InternalOptions.MinKeyLength);
    }
}
