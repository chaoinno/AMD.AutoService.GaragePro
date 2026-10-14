using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Email;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// จุดลงทะเบียนของ self-serve ฝั่ง Infrastructure — เรียกท้าย <c>AddGarageProInfrastructure</c> (ทุก host: API · devseed · spadmin)
/// · WP ถัดไปแทนที่ implementation ที่นี่ (S1: Google/Turnstile/DNS · อีเมลจริง) แทนการแก้ DependencyInjection.cs ซ้ำกันหลายสาย
/// · ค่าเริ่มต้นทั้งหมดต้องทำให้ลูกค้าเดิมเห็นพฤติกรรมเดิม: นโยบายไม่จำกัด · อีเมลลง log · ไม่มี override ผู้ใช้
/// · [BIZ] "ตั้งค่า self-serve แล้ว" = มี <c>SelfServe:ConnectionString</c> (<see cref="IsConfigured"/>) — ไม่ใช่ <c>SelfServe:Enabled</c>
///   ซึ่งเป็นแค่สวิตช์ปิดรับสมัครใหม่: ปิดรับสมัครแล้วอู่ที่มีอยู่ยังต้องถูกบังคับแพ็กเกจ/วันหมดอายุตามปกติ
/// </summary>
public static partial class SelfServeInfrastructure
{
    public static IServiceCollection AddSelfServeInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SelfServeOptions>(configuration.GetSection(SelfServeOptions.SectionName));

        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IEmailSender, LoggingEmailSender>();
        services.AddSingleton<IShardScopeFactory, ShardScopeFactory>();

        // ServiceDbContext เลือกฐานตาม shard ผ่านตัวนี้ (DependencyInjection.cs) — ต้องมีเสมอ แม้ไม่ได้ตั้ง self-serve
        // สร้างจาก configuration ที่ส่งเข้ามา (ไม่ resolve IConfiguration จาก DI — host อย่าง CLI/เทสต์อาจไม่ได้ลงทะเบียนไว้)
        services.TryAddSingleton<IServiceDbConnectionResolver>(sp => new ServiceDbConnectionResolver(
            configuration, sp.GetRequiredService<IOptions<SelfServeOptions>>()));
        services.AddMemoryCache();
        services.TryAddSingleton(TimeProvider.System);

        // Scoped: หนึ่ง override ต่อหนึ่ง scope — คำขอ HTTP ปกติไม่มีใครตั้งค่า (User = null) จึงอ่าน JWT ตามเดิม
        services.TryAddScoped<CurrentUserOverride>();

        if (IsConfigured(configuration))
        {
            var connectionString = configuration[ConnectionStringKey]!;
            services.AddDbContext<SelfServeDbContext>(options =>
                SelfServeDbContext.UseSelfServeSqlServer(options, connectionString));
            services.AddScoped<IWorkspacePlanReader, WorkspacePlanReader>();
            services.AddScoped<IPlanUsageCounter, PlanUsageCounter>();
            services.AddScoped<IWorkspacePolicy, WorkspacePolicy>();

            // จุดลงทะเบียนต่อ work package (partial method — ไม่มีไฟล์ implementation = ไม่มีผล)
            // แต่ละ WP เป็นเจ้าของไฟล์ SelfServeInfrastructure.<ชื่อ>.cs ของตัวเอง กัน merge ชนกันที่ไฟล์นี้
            RegisterAuth(services, configuration);          // B5
            RegisterProvisioning(services, configuration);  // S2
            RegisterSignup(services, configuration);        // S1
            RegisterStarterData(services, configuration);   // S3
            RegisterLifecycle(services, configuration);     // S4
            RegisterAdmin(services, configuration);         // S5
        }
        else
        {
            // deployment ที่ไม่มีฐาน self-serve: ไม่ลงทะเบียน SelfServeDbContext เลย (สตาร์ทได้เหมือนเดิม) และทุก shard เป็น legacy
            services.AddScoped<IWorkspacePolicy, UnlimitedWorkspacePolicy>();
        }

        return services;
    }

    static partial void RegisterAuth(IServiceCollection services, IConfiguration configuration);
    static partial void RegisterProvisioning(IServiceCollection services, IConfiguration configuration);
    static partial void RegisterSignup(IServiceCollection services, IConfiguration configuration);
    static partial void RegisterStarterData(IServiceCollection services, IConfiguration configuration);
    static partial void RegisterLifecycle(IServiceCollection services, IConfiguration configuration);
    static partial void RegisterAdmin(IServiceCollection services, IConfiguration configuration);

    public const string ConnectionStringKey = SelfServeOptions.SectionName + ":ConnectionString";

    /// <summary>
    /// มีฐาน GarageSelfServe ให้ใช้หรือไม่ — service ที่ต้องใช้ <see cref="SelfServeDbContext"/> (S1/S2/S4/S5) ต้องลงทะเบียน
    /// ภายใต้เงื่อนไขนี้เท่านั้น ไม่งั้น DI resolve ไม่ได้บน deployment ที่ไม่มี self-serve
    /// </summary>
    public static bool IsConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[ConnectionStringKey]);
}
