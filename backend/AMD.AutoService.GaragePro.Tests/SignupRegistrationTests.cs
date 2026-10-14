using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// DI ของ signup (S1): ลงทะเบียนเฉพาะเมื่อตั้ง SelfServe:ConnectionString และ resolve ได้แม้ S2/S3 ยังไม่ลงทะเบียน
/// (provisioner/starter data เป็น optional — ไม่งั้น ValidateOnBuild ของ Development ทำให้ API สตาร์ทไม่ได้)
/// </summary>
public sealed class SignupRegistrationTests
{
    private sealed class StubNotifier : IContactNotifier
    {
        public bool IsConfigured => false;
        public Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct) => Task.FromResult(false);
    }

    private sealed class StubSessions : ISignupSessionIssuer
    {
        public Task<Result<LoginResultDto>> IssueAsync(string shardKey, long userId, CancellationToken ct = default) =>
            Task.FromResult(Result<LoginResultDto>.Fail("X", "x"));
    }

    private sealed class StubReference : ISignupReferenceData
    {
        public Task<IReadOnlyList<SignupProvinceDto>> GetProvincesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<SignupProvinceDto>>([]);
    }

    private static ServiceProvider Build(bool configured)
    {
        var settings = new Dictionary<string, string?> { ["SelfServe:Enabled"] = "true" };
        if (configured) settings["SelfServe:ConnectionString"] = "Server=.;Database=GarageSelfServe_Dev";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        // ของที่ host จริงลงทะเบียนจากที่อื่น (DependencyInjection.cs / B5) — แทนด้วย stub ที่ไม่ต้องต่อฐาน
        services.AddSingleton<IContactNotifier, StubNotifier>();
        services.AddScoped<ISignupSessionIssuer, StubSessions>();
        services.AddScoped<ISignupReferenceData, StubReference>();
        services.AddSelfServeInfrastructure(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    [Fact]
    public void Signup_services_resolve_when_self_serve_is_configured()
    {
        using var provider = Build(configured: true);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        var service = sp.GetRequiredService<ISignupService>().Should().BeOfType<SignupService>().Subject;
        service.GetConfig().Enabled.Should().BeTrue();
        sp.GetRequiredService<ITurnstileVerifier>().Should().BeOfType<TurnstileVerifier>();
        sp.GetRequiredService<IGoogleIdTokenValidator>().Should().BeOfType<GoogleIdTokenValidator>();
        sp.GetRequiredService<IEmailDomainChecker>().Should().BeOfType<DnsEmailDomainChecker>();
        sp.GetRequiredService<ISelfServeSignupRepository>().Should().BeOfType<SelfServeSignupRepository>();
    }

    [Fact]
    public void Nothing_is_registered_without_a_self_serve_database()
    {
        using var provider = Build(configured: false);
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetService<ISignupService>().Should().BeNull("controller ตอบ SIGNUP_DISABLED เอง");
        scope.ServiceProvider.GetService<ITurnstileVerifier>().Should().BeNull();
    }
}
