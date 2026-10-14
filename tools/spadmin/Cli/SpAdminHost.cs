using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace AMD.AutoService.GaragePro.SpAdmin.Cli;

/// <summary>
/// สร้าง host ของ spadmin — DI ชุดเดียวกับ API (<c>AddGarageProInfrastructure</c>) แต่ไม่มี HTTP/JWT/hosted job
/// · ค่าตั้ง (ตัวหลังชนะ): appsettings.json ข้างไฟล์โปรแกรม → dotnet user-secrets ของ API (เฉพาะนอก container) → ตัวแปรแวดล้อม
///   ใน container ของ API จึงใช้ env file ของ production ตรงๆ (<c>SelfServe__ConnectionString</c> ฯลฯ) ไม่ต้องมีไฟล์ค่าตั้งเพิ่ม
/// · ไม่ส่ง args เข้า configuration — คำสั่ง/ตัวเลือกของ spadmin ไม่ใช่ค่าตั้ง
/// · ไม่ลงทะเบียน SelfServeDailyJob (อยู่ฝั่ง API) — spadmin รันงานรายวันเฉพาะเมื่อสั่ง run-jobs
/// </summary>
public static class SpAdminHost
{
    /// <summary>ค่าเดียวกับ UserSecretsId ของ API และ tools/devseed</summary>
    public const string UserSecretsId = "b844a272-cad9-4f64-af12-483db746d53a";

    /// <summary>ผู้ใช้เริ่มต้นของ scope ที่ไม่ได้ตั้ง override (คำสั่งส่วนใหญ่ไม่แตะ ServiceDbContext นอก RunAsAsync เลย)</summary>
    public static readonly ICurrentUser DefaultUser = new ScopedCurrentUser("db2", 0, 0, "spadmin");

    public static IHost Build(bool verbose)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ApplicationName = "spadmin",
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                              ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                              ?? Environments.Production
        });

        AddConfiguration(builder.Configuration, builder.Environment.EnvironmentName);

        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
        // log ไป stderr ทั้งหมด — stdout เป็นของผลลัพธ์ (ตาราง/สรุป) ให้ส่งต่อ/redirect ได้สะอาด
        builder.Services.Configure<ConsoleLoggerOptions>(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        if (verbose)
            builder.Logging.SetMinimumLevel(LogLevel.Information);
        else
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));

        ConfigureServices(builder.Services, builder.Configuration);
        return builder.Build();
    }

    /// <summary>DI ของ spadmin (แยกออกมาให้เทสต์ตรวจว่าทุกคำสั่ง resolve ได้โดยไม่ต้องยก host/อ่าน user-secrets)</summary>
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddGarageProInfrastructure(configuration);

        // ICurrentUser แบบเดียวกับ Program.cs ของ API — override ของ IShardScopeFactory ก่อน ไม่มีก็ผู้ใช้ระบบของ db2
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserOverride>().User ?? DefaultUser);
    }

    public static void AddConfiguration(IConfigurationBuilder configuration, string? environmentName = null)
    {
        configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: true,
            reloadOnChange: false);
        // เครื่อง dev: ค่าที่ไม่ใช่ความลับซึ่ง API อ่านจาก appsettings.Development.json ของมันเอง (เช่น Ftp:Host/RootPath)
        // · image production ลบไฟล์นี้ทิ้ง (Dockerfile.api) — ใน container ค่าเหล่านี้มาจาก env file/compose
        if (!string.IsNullOrWhiteSpace(environmentName))
            configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, $"appsettings.{environmentName}.json"),
                optional: true, reloadOnChange: false);

        // ใน container ไม่มีไฟล์ user-secrets (และ HOME ของผู้ใช้ระบบอาจหาไม่ได้) — ใช้ env file แทน
        if (!string.Equals(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER"), "true",
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                configuration.AddUserSecrets(UserSecretsId, reloadOnChange: false);
            }
            catch (InvalidOperationException)
            {
                // ไม่มีที่เก็บ user-secrets บนเครื่องนี้ — ใช้ env อย่างเดียว
            }
        }

        configuration.AddEnvironmentVariables();
    }

    /// <summary>null = พร้อมใช้ · ข้อความ = ค่าตั้งที่ขาด (ไม่แสดงค่าจริง)</summary>
    public static string? CheckConfiguration(IConfiguration configuration)
    {
        if (!SelfServeInfrastructure.IsConfigured(configuration))
            return """
                ยังไม่ได้ตั้งค่า SelfServe:ConnectionString, spadmin ต้องต่อฐาน GarageSelfServe
                (self-serve database is not configured: set SelfServe:ConnectionString)
                · เครื่อง dev: dotnet user-secrets set "SelfServe:ConnectionString" "..." --project backend/AMD.AutoService.GaragePro.API
                · container: รันผ่าน docker compose run ของ service api (ใช้ env file เดียวกับ API)
                """;

        var shard = configuration[$"{SelfServeOptions.SectionName}:ShardKey"];
        if (string.IsNullOrWhiteSpace(shard)) shard = SelfServeOptions.DefaultShardKey;
        if (string.IsNullOrWhiteSpace(configuration[$"{LegacyShardOptions.SectionName}:ConnectionStrings:{shard}"]))
            return $"""
                ยังไม่ได้ตั้งค่า {LegacyShardOptions.SectionName}:ConnectionStrings:{shard} (ค่าเดียวกับ SelfServe:ConnectionString)
                (missing {LegacyShardOptions.SectionName}:ConnectionStrings:{shard})
                """;

        return null;
    }
}
