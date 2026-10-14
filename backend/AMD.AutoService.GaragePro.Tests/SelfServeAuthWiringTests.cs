using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Infrastructure;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// B5 — DI ของการเข้าสู่ระบบ: AuthService/StaffService ต้อง resolve ได้ทั้ง deployment ที่ไม่มี self-serve
/// (ตัวอ่าน sp_Account ไม่ถูกลงทะเบียน → null) และที่มี · ไม่แตะฐานข้อมูลจริง (DbContext สร้างแบบ lazy)
/// </summary>
public sealed class SelfServeAuthWiringTests
{
    private static ServiceProvider Build(bool selfServe)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ServiceDb"] = "Server=127.0.0.1,1;Database=Unused;Connect Timeout=1",
            ["LegacyShards:DefaultShard"] = "db2"
        };
        if (selfServe)
            settings[SelfServeInfrastructure.ConnectionStringKey] = "Server=127.0.0.1,1;Database=UnusedSelfServe;Connect Timeout=1";

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser>(sp =>
            sp.GetRequiredService<CurrentUserOverride>().User ?? new ScopedCurrentUser("db2", 0, 0, "anon"));
        services.AddScoped<ITokenIssuer, NoTokens>();
        services.AddGarageProInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Auth_and_staff_services_resolve_with_and_without_self_serve(bool selfServe)
    {
        using var provider = Build(selfServe);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IAuthService>().Should().BeOfType<AuthService>();
        sp.GetRequiredService<IStaffService>().Should().BeOfType<StaffService>();
        sp.GetService<ISelfServeAccountReader>().Should().Match(r => selfServe ? r is SelfServeAccountReader : r == null);
        sp.GetService<IGoogleLoginService>().Should().Match(g => selfServe ? g is GoogleLoginService : g == null);

        var resolver = sp.GetRequiredService<ILoginIdentityResolver>();
        (await resolver.ResolveAsync("employee01")).Should().Be(new LoginIdentity("db2", "employee01"));
        (await resolver.ResolveAsync("sp1001.t01")).Should().Be(new LoginIdentity("sp1", "SP1001.T01"));
        if (!selfServe)
            (await resolver.ResolveAsync("owner@example.com")).Should().Be(new LoginIdentity("sp1", ""));
    }

    private sealed class NoTokens : ITokenIssuer
    {
        public (string Token, DateTime ExpiresAt) IssuePreSessionToken(AuthUserDto user) => throw new NotSupportedException();
        public (string Token, DateTime ExpiresAt) IssueBranchToken(AuthUserDto user, int branchId) => throw new NotSupportedException();
        public (string Token, DateTime ExpiresAt) IssueSessionToken(AuthUserDto user, ShiftSession session) => throw new NotSupportedException();
    }
}
