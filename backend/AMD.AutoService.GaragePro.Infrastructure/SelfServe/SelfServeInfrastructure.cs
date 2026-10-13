using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// จุดลงทะเบียนของ self-serve ฝั่ง Infrastructure — เรียกท้าย <c>AddGarageProInfrastructure</c> (ทุก host: API · devseed · spadmin)
/// · WP ถัดไปแทนที่ implementation ที่นี่ (B4: WorkspacePolicy แทน Unlimited · S1: Google/Turnstile/DNS · อีเมลจริง)
///   แทนการแก้ DependencyInjection.cs ซ้ำกันหลายสาย
/// · ค่าเริ่มต้นทั้งหมดต้องทำให้ลูกค้าเดิมเห็นพฤติกรรมเดิม: นโยบายไม่จำกัด · อีเมลลง log · ไม่มี override ผู้ใช้
/// </summary>
public static class SelfServeInfrastructure
{
    public static IServiceCollection AddSelfServeInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SelfServeOptions>(configuration.GetSection(SelfServeOptions.SectionName));

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddScoped<IWorkspacePolicy, UnlimitedWorkspacePolicy>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddSingleton<IShardScopeFactory, ShardScopeFactory>();

        // Scoped: หนึ่ง override ต่อหนึ่ง scope — คำขอ HTTP ปกติไม่มีใครตั้งค่า (User = null) จึงอ่าน JWT ตามเดิม
        services.TryAddScoped<CurrentUserOverride>();

        return services;
    }
}
