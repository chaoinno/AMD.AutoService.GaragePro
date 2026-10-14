using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Email;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ล็อกค่าเริ่มต้นของ DI hook self-serve: ทุก implementation เริ่มต้นต้องทำให้ลูกค้าเดิมเห็นพฤติกรรมเดิม
/// และ SelfServeOptions ต้อง bind จาก section <c>SelfServe</c> ได้ (รวมกรณี array ที่ binder "ต่อท้าย" แทนการแทนที่)
/// </summary>
public class SelfServeInfrastructureTests
{
    private static ServiceProvider Build(IDictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSelfServeInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Default_registrations_keep_legacy_behaviour()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IPasswordHasher>().Should().BeOfType<Pbkdf2PasswordHasher>();
        sp.GetRequiredService<IWorkspacePolicy>().Should().BeOfType<UnlimitedWorkspacePolicy>();
        sp.GetRequiredService<IEmailSender>().Should().BeOfType<LoggingEmailSender>()
            .Which.IsConfigured.Should().BeFalse("provider แบบ log ต้องไม่ถูกนับว่าส่งอีเมลได้จริง");
        sp.GetRequiredService<IShardScopeFactory>().Should().BeOfType<ShardScopeFactory>();
        sp.GetRequiredService<CurrentUserOverride>().User.Should().BeNull("คำขอปกติไม่มี override จึงอ่าน JWT ตามเดิม");
    }

    [Fact]
    public void Override_holder_is_scoped_and_shard_factory_is_singleton()
    {
        using var provider = Build();
        using var a = provider.CreateScope();
        using var b = provider.CreateScope();

        a.ServiceProvider.GetRequiredService<CurrentUserOverride>()
            .Should().NotBeSameAs(b.ServiceProvider.GetRequiredService<CurrentUserOverride>());
        a.ServiceProvider.GetRequiredService<CurrentUserOverride>()
            .Should().BeSameAs(a.ServiceProvider.GetRequiredService<CurrentUserOverride>());
        provider.GetRequiredService<IShardScopeFactory>()
            .Should().BeSameAs(provider.GetRequiredService<IShardScopeFactory>());
    }

    [Fact]
    public void Options_bind_from_the_SelfServe_section_without_duplicating_reminder_days()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["SelfServe:Enabled"] = "true",
            ["SelfServe:ConnectionString"] = "Server=.;Database=GarageSelfServe_Dev",
            ["SelfServe:Trial:Days"] = "14",
            ["SelfServe:Trial:ReminderDays:0"] = "7",
            ["SelfServe:Trial:ReminderDays:1"] = "1",
            ["SelfServe:Limits:Staff"] = "3",
            ["SelfServe:Email:Provider"] = "brevo",
            ["SelfServe:Jobs:DailyAtLocalTime"] = "04:00"
        });

        var options = provider.GetRequiredService<IOptions<SelfServeOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.ShardKey.Should().Be("sp1");
        options.ConnectionString.Should().Contain("GarageSelfServe_Dev");
        options.Trial.Days.Should().Be(14);
        options.Trial.ReadOnlyDays.Should().Be(90, "ค่าที่ไม่ได้ตั้งต้องคง default");
        options.Trial.EffectiveReminderDays.Should().Equal([7, 1],
            "binder ต่อท้าย array เดิม, default จึงต้องว่างแล้ว fallback ตอนอ่าน ไม่งั้นได้ [7,1,7,1]");
        options.Limits.Staff.Should().Be(3);
        options.Limits.Jobs.Should().Be(100);
        options.Email.Provider.Should().Be("brevo");
        options.Email.IsLogProvider.Should().BeFalse();
        options.Jobs.DailyAtLocalTime.Should().Be("04:00");
        options.Jobs.SummaryAtLocalTime.Should().Be("08:30");
    }

    [Fact]
    public void Options_without_the_section_fall_back_to_the_planned_defaults()
    {
        using var provider = Build();

        var options = provider.GetRequiredService<IOptions<SelfServeOptions>>().Value;

        options.Enabled.Should().BeFalse();
        options.Trial.EffectiveReminderDays.Should().Equal([7, 1]);
        options.Limits.ToPlanLimits().Should().Be(new PlanLimits(5, 200, 200, 100));
    }
}
