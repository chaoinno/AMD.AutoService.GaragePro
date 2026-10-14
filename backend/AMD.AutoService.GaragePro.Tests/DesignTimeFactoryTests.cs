using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// design-time factory แทน host ของ API ตอน dotnet ef / efbundle — ลำดับแหล่ง connection string ต้องไม่เพี้ยน:
/// env (container) → appsettings + user-secrets (เฉพาะ Development, เครื่อง dev) → placeholder ที่ต่อไม่ได้
/// </summary>
public class DesignTimeFactoryTests
{
    // คีย์ทดสอบที่ไม่มีใน user-secrets จริงของเครื่องไหน — เทสต์จึงไม่อ่านความลับจริงมาเทียบ
    private const string Key = "DesignTimeTest:ConnectionString";

    private static Func<string, string?> Env(Dictionary<string, string?> values) =>
        name => values.GetValueOrDefault(name);

    [Fact]
    public void Environment_variable_wins()
    {
        var result = DesignTimeConnection.Resolve("X__Conn", Key, "Db",
            getEnvironment: Env(new() { ["X__Conn"] = "Server=env;Database=FromEnv" }));

        result.Should().Be("Server=env;Database=FromEnv");
    }

    [Fact]
    public void Development_reads_the_app_settings_files_like_the_api_does()
    {
        var dir = Directory.CreateTempSubdirectory("gp-designtime-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "appsettings.Development.json"),
                """{ "DesignTimeTest": { "ConnectionString": "Server=dev;Database=FromFile" } }""");

            var result = DesignTimeConnection.Resolve("X__Conn", Key, "Db", dir.FullName,
                Env(new() { ["ASPNETCORE_ENVIRONMENT"] = "Development" }));

            result.Should().Be("Server=dev;Database=FromFile");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Outside_development_files_are_ignored_and_the_placeholder_is_used()
    {
        var dir = Directory.CreateTempSubdirectory("gp-designtime-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "appsettings.json"),
                """{ "DesignTimeTest": { "ConnectionString": "Server=prod;Database=ShouldNotBeUsed" } }""");

            var result = DesignTimeConnection.Resolve("X__Conn", Key, "GarageSelfServe", dir.FullName,
                Env(new() { ["ASPNETCORE_ENVIRONMENT"] = "Production" }));

            result.Should().Be(DesignTimeConnection.Placeholder("GarageSelfServe"));
            result.Should().Contain("design-time-placeholder");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Self_serve_factory_uses_its_own_history_table()
    {
        using var db = new SelfServeDbContextDesignTimeFactory().CreateDbContext([]);

        db.Database.GetConnectionString().Should().NotBeNullOrWhiteSpace();
        db.Database.GetMigrations().Should().Contain(m => m.EndsWith("_InitSelfServe"));
        db.Database.GenerateCreateScript().Should().Contain("sp_Workspace").And.NotContain("svc_Job");
    }

    [Fact]
    public void Service_factory_still_sees_every_service_migration()
    {
        using var db = new ServiceDbContextDesignTimeFactory().CreateDbContext([]);

        // ไม่ล็อกจำนวน (integrator ยังเพิ่ม migration ของ ServiceDb ต่อได้) — ล็อกว่าสองชุดไม่ปนกัน
        var migrations = db.Database.GetMigrations().ToList();
        migrations.Should().Contain(m => m.EndsWith("_InitialCreate"))
            .And.Contain(m => m.EndsWith("_AddReceiptScope"))
            .And.NotContain(m => m.EndsWith("_InitSelfServe"));
        migrations.Count.Should().BeGreaterThanOrEqualTo(22);
    }
}
