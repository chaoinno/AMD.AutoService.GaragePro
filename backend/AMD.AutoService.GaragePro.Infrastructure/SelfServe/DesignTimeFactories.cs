using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ให้ <c>dotnet ef</c> และ migration bundle (efbundle / efbundle-selfserve) สร้าง context ได้โดยไม่ต้องสตาร์ท host ของ API
/// — เดิมต้องยัด <c>Jwt__Key</c> ปลอมตอน build bundle เพราะ EF ไปรัน Program.cs ซึ่งโยน exception เมื่อไม่มี key
/// · เมื่อมี factory นี้ EF ใช้ factory ก่อนทางอื่น (มีอยู่ในแอสเซมบลีเดียวกับ DbContext)
/// · connection string: env var (container/CI) → (เฉพาะ Development) appsettings + user-secrets ของ API แบบเดียวกับที่แอปอ่าน
///   ให้คำสั่ง <c>dotnet ef database update</c> ใน CLAUDE.md ทำงานเหมือนเดิม → placeholder ที่ต่อไม่ได้
///   (พอสำหรับ migrations add/script/bundle ซึ่งไม่ต่อฐาน — ไม่มีวันชี้ไปฐานจริงโดยบังเอิญ)
/// · bundle รับ <c>--connection</c> ทับค่าจาก factory ได้ตามปกติ
/// </summary>
public sealed class ServiceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ServiceDbContext>
{
    public const string EnvironmentVariable = "ConnectionStrings__ServiceDb";

    public ServiceDbContext CreateDbContext(string[] args)
    {
        var connectionString = DesignTimeConnection.Resolve(
            EnvironmentVariable, "ConnectionStrings:" + ServiceDbConnectionResolver.ServiceDbConnectionName,
            "GarageService");

        return new ServiceDbContext(new DbContextOptionsBuilder<ServiceDbContext>()
            .UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3))
            .Options);
    }
}

/// <inheritdoc cref="ServiceDbContextDesignTimeFactory"/>
public sealed class SelfServeDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SelfServeDbContext>
{
    public const string EnvironmentVariable = "SelfServe__ConnectionString";

    public SelfServeDbContext CreateDbContext(string[] args)
    {
        var connectionString = DesignTimeConnection.Resolve(
            EnvironmentVariable, SelfServeInfrastructure.ConnectionStringKey, "GarageSelfServe");

        var builder = new DbContextOptionsBuilder<SelfServeDbContext>();
        SelfServeDbContext.UseSelfServeSqlServer(builder, connectionString);
        return new SelfServeDbContext(builder.Options);
    }
}

/// <summary>migration ของนโยบายอัปเดตแอป — history แยก และชี้ฐานกลาง (GarageService) เสมอ ไม่รันกับฐานของ shard อื่น</summary>
public sealed class AppUpdatesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppUpdatesDbContext>
{
    public AppUpdatesDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AppUpdatesDbContext>()
        .UseSqlServer(DesignTimeConnection.Resolve("ConnectionStrings__ServiceDb", "ConnectionStrings:ServiceDb", "GarageService"),
            sql => sql.MigrationsHistoryTable("__AppUpdatesMigrationsHistory")).Options);
}

internal static class DesignTimeConnection
{
    /// <summary>UserSecretsId ของโปรเจกต์ API (ต้องตรงกับ AMD.AutoService.GaragePro.API.csproj)</summary>
    internal const string ApiUserSecretsId = "b844a272-cad9-4f64-af12-483db746d53a";

    internal static string Placeholder(string database) =>
        $"Server=design-time-placeholder;Database={database};Integrated Security=False;" +
        "TrustServerCertificate=True;Connect Timeout=5";

    internal static string Resolve(string environmentVariable, string configurationKey, string placeholderDatabase,
        string? baseDirectory = null, Func<string, string?>? getEnvironment = null)
    {
        getEnvironment ??= Environment.GetEnvironmentVariable;

        var fromEnvironment = getEnvironment(environmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return fromEnvironment;

        var environment = getEnvironment("ASPNETCORE_ENVIRONMENT")
                          ?? getEnvironment("DOTNET_ENVIRONMENT")
                          ?? "Development";

        // [SECURITY] นอก Development ห้ามเดาจากไฟล์ — container production ส่งค่าผ่าน env เสมอ ถ้าไม่มีให้ได้ placeholder ที่ต่อไม่ได้
        if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(baseDirectory ?? Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: true)
                    .AddJsonFile($"appsettings.{environment}.json", optional: true)
                    .AddUserSecrets(ApiUserSecretsId)
                    .Build();

                var fromConfiguration = configuration[configurationKey];
                if (!string.IsNullOrWhiteSpace(fromConfiguration))
                    return fromConfiguration;
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or FormatException)
            {
                // ไม่มี HOME (หาที่เก็บ user-secrets ไม่ได้) หรือไฟล์ config เสีย — ใช้ placeholder ต่อ
            }
        }

        return Placeholder(placeholderDatabase);
    }
}
