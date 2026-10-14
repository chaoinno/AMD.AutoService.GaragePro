using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// B5 — การเข้าสู่ระบบของอู่ที่สมัครเอง (ไฟล์ของ B5 ตามกติกา partial hook ของ SelfServeInfrastructure)
/// · <see cref="RegisterAuth"/> ทำงานเฉพาะเมื่อตั้ง SelfServe:ConnectionString (ใช้ SelfServeDbContext)
/// · <see cref="CreateLoginIdentityResolver"/> ใช้ลงทะเบียนตัวแปลง login "เสมอ" จาก DependencyInjection.cs —
///   AuthService ต้องมีตัวนี้แม้ไม่ได้ตั้ง self-serve (ตอนนั้นอีเมลถือว่าไม่พบบัญชี ส่วนรหัสพนักงานเดิมไป db2 ตามปกติ)
/// </summary>
public static partial class SelfServeInfrastructure
{
    static partial void RegisterAuth(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<ISelfServeAccountReader, SelfServeAccountReader>();
        services.AddScoped<IGoogleLoginService>(sp => new GoogleLoginService(
            sp,
            sp.GetRequiredService<ISelfServeAccountReader>(),
            sp.GetRequiredService<IAuthService>(),
            sp.GetRequiredService<IOptions<SelfServeOptions>>().Value.ShardKey));
    }

    /// <summary>
    /// shard ปลายทาง: รหัสพนักงานเดิม → <c>LegacyShards:DefaultShard</c> (db2 — ค่าเดียวกับที่ CurrentUser ใช้กับคำขอที่ยังไม่ login)
    /// · อีเมล/SP0000.X00 → <c>SelfServe:ShardKey</c> (sp1)
    /// </summary>
    public static ILoginIdentityResolver CreateLoginIdentityResolver(IServiceProvider sp) =>
        new LoginIdentityResolver(
            sp,
            sp.GetRequiredService<IOptions<LegacyShardOptions>>().Value.DefaultShard,
            sp.GetRequiredService<IOptions<SelfServeOptions>>().Value.ShardKey);
}
