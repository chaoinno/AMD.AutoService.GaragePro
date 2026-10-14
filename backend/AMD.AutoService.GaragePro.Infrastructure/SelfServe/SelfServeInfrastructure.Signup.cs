using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// S1 — signup API: ลงทะเบียนเฉพาะเมื่อตั้ง <c>SelfServe:ConnectionString</c> (partial นี้ถูกเรียกภายใต้เงื่อนไขนั้นเท่านั้น)
/// · ไม่ได้ตั้ง = ไม่มี <see cref="ISignupService"/> ใน DI → <c>PublicSignupController</c> ตอบ SIGNUP_DISABLED ทุก endpoint
/// · <see cref="IGoogleIdTokenValidator"/> ลงทะเบียนที่นี่ด้วย — B5 (<c>/auth/google</c>) ต้อง resolve แบบ optional เมื่อไม่ได้ตั้ง self-serve
/// · <see cref="SignupHostEnvironment"/> มาจาก API (SelfServeApi.Signup.cs) — host อื่นไม่มี = ถือเป็น Production (fail-closed)
/// </summary>
public static partial class SelfServeInfrastructure
{
    static partial void RegisterSignup(IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<SignupStash>();
        services.TryAddSingleton<IGoogleIdTokenValidator, GoogleIdTokenValidator>();
        services.TryAddSingleton<ITurnstileVerifier, TurnstileVerifier>();
        services.TryAddSingleton<IEmailDomainChecker, DnsEmailDomainChecker>();
        services.TryAddScoped<ISelfServeSignupRepository, SelfServeSignupRepository>();
        services.TryAddScoped<ISignupLog, LoggerSignupLog>();
        services.TryAddScoped<ISignupSessionIssuer, AuthServiceSessionIssuer>();
        services.TryAddScoped<ISignupReferenceData, CachedSignupReferenceData>();

        // SelfServeOptions เป็น POCO ใน Application (ไม่มี IOptions) — ส่งค่าที่ bind แล้วเข้า ctor
        // ActivatorUtilities ให้ IWorkspaceProvisioner/IStarterDataService/SignupHostEnvironment เป็น null ได้เมื่อยังไม่ลงทะเบียน
        services.TryAddScoped<ISignupService>(sp => ActivatorUtilities.CreateInstance<SignupService>(
            sp, sp.GetRequiredService<IOptions<SelfServeOptions>>().Value));
    }
}

/// <summary>ISignupLog → ILogger (หมวดเดียวกับ SignupService) — Application ไม่มี Microsoft.Extensions.Logging</summary>
internal sealed class LoggerSignupLog(ILogger<SignupService> logger) : ISignupLog
{
#pragma warning disable CA2254 // template มาจากโค้ดของเราเท่านั้น (ไม่ใช่ข้อความผู้ใช้)
    public void Info(string message, params object?[] args) => logger.LogInformation(message, args);

    public void Warning(Exception? exception, string message, params object?[] args) =>
        logger.LogWarning(exception, message, args);

    public void Error(Exception? exception, string message, params object?[] args) =>
        logger.LogError(exception, message, args);
#pragma warning restore CA2254
}
