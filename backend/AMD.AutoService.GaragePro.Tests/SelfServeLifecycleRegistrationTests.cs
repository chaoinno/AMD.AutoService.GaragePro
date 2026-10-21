using AMD.AutoService.GaragePro.API.SelfServe;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// DI ของ S4: ลงทะเบียนเฉพาะเมื่อตั้งฐาน self-serve · service ที่มีพารามิเตอร์ optional (S2/S3) resolve ได้แม้ host ไม่มีของเหล่านั้น
/// · งานรายวันเปิดเฉพาะเมื่อตั้งฐานและไม่ได้ปิด SelfServe:Jobs:Enabled — ไม่มีการต่อฐานจริงในเทสต์นี้
/// </summary>
public class SelfServeLifecycleRegistrationTests
{
    private const string SelfServeCs = "Server=model-only;Database=GarageSelfServe_Dev;User Id=a;Password=b";

    private sealed class StubUser : ICurrentUser
    {
        public long UserId => 1;
        public string UserName => "SP1001.M01";
        public UserRole Role => UserRole.Manager;
        public string ShardKey => "sp1";
        public int BranchId => 1;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
    }

    private static IConfiguration Config(bool selfServe, string? jobsEnabled = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ServiceDb"] = "Server=model-only;Database=GarageService;User Id=a;Password=b",
            ["LegacyShards:ConnectionStrings:db2"] = "Server=model-only;Database=Garage;User Id=a;Password=b"
        };
        if (selfServe)
        {
            settings["SelfServe:ConnectionString"] = SelfServeCs;
            settings["LegacyShards:ConnectionStrings:sp1"] = SelfServeCs;
        }
        if (jobsEnabled is not null) settings["SelfServe:Jobs:Enabled"] = jobsEnabled;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static ServiceProvider Build(IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGarageProInfrastructure(configuration);
        services.AddScoped<ICurrentUser, StubUser>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Lifecycle_services_resolve_when_self_serve_is_configured()
    {
        using var provider = Build(Config(selfServe: true));
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<ITrialLifecycleService>().Should().BeOfType<TrialLifecycleService>();
        sp.GetRequiredService<ISelfServeStatusService>().Should().BeOfType<SelfServeStatusService>();
        sp.GetRequiredService<IWorkspacePurger>().Should().BeOfType<WorkspacePurger>();
        sp.GetRequiredService<ISelfServeSalesNotifier>().Should().BeOfType<SelfServeSalesNotifier>();
        sp.GetRequiredService<IWorkspacePurgeData>().CheckTargetDatabase("sp1").Should().BeNull(
            "ค่าตั้งชุดนี้ชี้ GarageSelfServe_Dev ทั้งสองที่ และไม่ซ้ำกับ GarageService/Garage");

        // docs/17: แจ้งชำระ/ต่ออายุ
        sp.GetRequiredService<Application.SelfServe.Package.IPackagePaymentService>()
            .Should().BeOfType<Application.SelfServe.Package.PackagePaymentService>();
        sp.GetRequiredService<Application.SelfServe.Package.PackagePaymentAdminService>().Should().NotBeNull();
        sp.GetRequiredService<Application.SelfServe.Package.IPackagePaymentRepository>()
            .Should().BeOfType<PackagePaymentRepository>();
        sp.GetRequiredService<Application.SelfServe.Package.IPackageSlipStorage>()
            .Should().BeOfType<Infrastructure.Storage.FtpAttachmentStorage>();
        sp.GetRequiredService<Application.SelfServe.Package.IPackagePaymentNotifier>()
            .Should().BeOfType<PackagePaymentNotifier>();
    }

    [Fact]
    public void Lifecycle_services_are_absent_without_self_serve()
    {
        using var provider = Build(Config(selfServe: false));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<ITrialLifecycleService>().Should().BeNull();
        scope.ServiceProvider.GetService<ISelfServeStatusService>().Should().BeNull(
            "controller จึงตอบ 404 SELFSERVE_NOT_APPLICABLE แทน 500");
        scope.ServiceProvider.GetService<Application.SelfServe.Package.IPackagePaymentService>().Should().BeNull(
            "endpoint แพ็กเกจและลิงก์สลิปตอบ 404 เช่นกัน");
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, "true", true)]
    [InlineData(true, "false", false)]
    [InlineData(true, "False", false)]
    [InlineData(false, null, false)]
    public void Daily_job_runs_only_when_configured_and_not_disabled(bool selfServe, string? enabled, bool expected) =>
        SelfServeDailyJob.IsEnabled(Config(selfServe, enabled)).Should().Be(expected);
}
