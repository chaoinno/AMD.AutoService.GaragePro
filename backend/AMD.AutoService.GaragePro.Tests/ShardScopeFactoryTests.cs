using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// พิสูจน์ plumbing ของ ICurrentUser นอก HTTP: override ถูกตั้งก่อน resolve · มีผลเฉพาะ scope ของตัวเอง · scope ปกติยังเดินทางเดิม
/// — ถ้าพัง signup/hosted job จะได้ shard/ผู้ใช้ผิดทั้ง scope โดยไม่มีอะไรฟ้อง
/// </summary>
public class ShardScopeFactoryTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddScoped<CurrentUserOverride>();
        services.AddSingleton<IShardScopeFactory, ShardScopeFactory>();
        // แบบเดียวกับ Program.cs — ต่างแค่ fallback: ในเทสต์ไม่มี HttpContext จึงโยนแทนการสร้าง CurrentUser
        services.AddScoped<ICurrentUser>(sp =>
            sp.GetRequiredService<CurrentUserOverride>().User
            ?? throw new InvalidOperationException("ไม่มี HttpContext"));
        services.AddScoped<Probe>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Work_runs_with_the_given_user_and_the_override_does_not_leak_outside()
    {
        await using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IShardScopeFactory>();
        var owner = new ScopedCurrentUser("sp1", 1001, 42, "SP1001.M01", source: EventSource.Web);

        var seen = await factory.RunAsAsync(owner,
            (sp, _) => Task.FromResult(sp.GetRequiredService<Probe>().User));

        seen.Should().BeSameAs(owner);

        using var ordinary = provider.CreateScope();
        ordinary.ServiceProvider.GetRequiredService<CurrentUserOverride>().User.Should().BeNull();
        var act = () => ordinary.ServiceProvider.GetRequiredService<ICurrentUser>();
        act.Should().Throw<InvalidOperationException>("scope ปกติไม่มี override จึงต้องเดินทาง HttpContext ตามเดิม");
    }

    [Fact]
    public async Task Each_run_gets_its_own_scope_and_user()
    {
        await using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IShardScopeFactory>();

        var first = await factory.RunAsAsync(new ScopedCurrentUser("sp1", 1, 1, "a"),
            (sp, _) => Task.FromResult(sp.GetRequiredService<ICurrentUser>().UserName));
        var second = await factory.RunAsAsync(new ScopedCurrentUser("db2", 227, 2, "b"),
            (sp, _) => Task.FromResult(sp.GetRequiredService<ICurrentUser>()));

        first.Should().Be("a");
        second.UserName.Should().Be("b");
        second.ShardKey.Should().Be("db2");
        second.BranchId.Should().Be(227);
    }

    [Fact]
    public async Task Non_generic_overload_runs_the_work_and_forwards_cancellation()
    {
        await using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IShardScopeFactory>();
        using var cts = new CancellationTokenSource();
        CancellationToken observed = default;
        string? shard = null;

        await factory.RunAsAsync(new ScopedCurrentUser("sp1", 5, 5, "x"), (sp, token) =>
        {
            observed = token;
            shard = sp.GetRequiredService<ICurrentUser>().ShardKey;
            return Task.CompletedTask;
        }, cts.Token);

        observed.Should().Be(cts.Token);
        shard.Should().Be("sp1");
    }

    [Fact]
    public async Task Null_user_is_rejected()
    {
        await using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IShardScopeFactory>();

        var act = () => factory.RunAsAsync(null!, (_, _) => Task.FromResult(0));

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>
    /// service ที่รับ ICurrentUser ผ่าน ctor เหมือน service จริง — ยืนยันว่า override มีผลกับ constructor injection
    /// ไม่ใช่แค่ GetRequiredService ตรงๆ
    /// </summary>
    private sealed class Probe(ICurrentUser user)
    {
        public ICurrentUser User => user;
    }
}
