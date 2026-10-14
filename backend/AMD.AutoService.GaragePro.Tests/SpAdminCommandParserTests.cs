using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Conversion;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.CatalogCategories;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.QuotationTemplates;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Application.Warehouses;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using AMD.AutoService.GaragePro.SpAdmin.Cli;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// tools/spadmin (S5): ตัว parse คำสั่งไม่เดา (ตัวเลือกผิด/ขาด/ซ้ำ = error) · ค่าวันที่/เวลา · DI ของ host resolve ได้ทุกคำสั่ง
/// </summary>
public class SpAdminCommandParserTests
{
    [Fact]
    public void Parses_a_command_with_values_flags_and_equals_syntax()
    {
        var result = CommandParser.Parse(
            ["convert", "--workspace", "SP1042", "--target-branch=431", "--target-user", "98765", "--dry-run", "--out", "r.json"]);

        result.Error.Should().BeNull();
        var command = result.Command!;
        command.Name.Should().Be("convert");
        command.Get("workspace").Should().Be("SP1042");
        command.Get("target-branch").Should().Be("431");
        command.Get("target-user").Should().Be("98765");
        command.Get("out").Should().Be("r.json");
        command.Has("dry-run").Should().BeTrue();
        command.Has("yes").Should().BeFalse();
    }

    [Fact]
    public void Negative_numbers_are_values_not_options()
    {
        var command = CommandParser.Parse(["extend", "--workspace", "1042", "--days", "-31"]).Command!;

        command.Get("days").Should().Be("-31");
        ValueParsers.TryParseInt(command.Get("days"), out var days).Should().BeTrue();
        days.Should().Be(-31);
    }

    [Theory]
    [InlineData(new[] { "extend", "--workspace", "1", "--dayz", "3" }, "--dayz")]
    [InlineData(new[] { "extend", "--workspace", "1" }, "--days")]
    [InlineData(new[] { "extend", "--workspace", "1", "--days", "3", "--days", "4" }, "ซ้ำ")]
    [InlineData(new[] { "extend", "--workspace", "--days", "3" }, "ต้องมีค่า")]
    [InlineData(new[] { "list", "--status", "deleted" }, "provisioning|active|expired|purged")]
    [InlineData(new[] { "set-plan", "--workspace", "1", "--plan", "converted" }, "trial|paid|suspended")]
    [InlineData(new[] { "purge", "--workspace", "1", "--yes=true" }, "ไม่รับค่า")]
    [InlineData(new[] { "nuke" }, "ไม่รู้จักคำสั่ง")]
    [InlineData(new[] { "show", "SP1042" }, "ไม่เข้าใจ")]
    [InlineData(new[] { "run-jobs", "--as-of", "+1d", "--date", "+2d" }, "อย่างใดอย่างหนึ่ง")]
    public void Rejects_malformed_commands_instead_of_guessing(string[] args, string expected)
    {
        var result = CommandParser.Parse(args);

        result.Command.Should().BeNull();
        result.Error.Should().Contain(expected);
    }

    [Fact]
    public void Allowed_values_are_normalised_to_lower_case()
    {
        CommandParser.Parse(["list", "--plan", "Trial"]).Command!.Get("plan").Should().Be("trial");
    }

    [Fact]
    public void Date_is_an_alias_of_as_of()
    {
        var command = CommandParser.Parse(["run-jobs", "--task", "maintenance", "--date", "+91d"]).Command!;

        command.Get("as-of").Should().Be("+91d");
        command.Get("date").Should().BeNull();
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "--help" }, null)]
    [InlineData(new[] { "help", "convert" }, "convert")]
    [InlineData(new[] { "convert", "--help" }, "convert")]
    [InlineData(new[] { "purge", "-h" }, "purge")]
    public void Help_is_recognised(string[] args, string? helpFor)
    {
        var result = CommandParser.Parse(args);

        result.IsHelp.Should().BeTrue();
        result.HelpFor.Should().Be(helpFor);
        CommandParser.Usage(helpFor).Should().Contain("spadmin");
    }

    [Fact]
    public void Every_mutating_command_accepts_an_operator_name()
    {
        foreach (var spec in CommandParser.Commands.Where(c => c.Mutates))
            spec.Options.Should().Contain(o => o.Name == "by", $"{spec.Name} ลง sp_WorkspaceAudit ต้องระบุผู้กระทำได้");
    }

    [Theory]
    [InlineData("SP1042", 1042)]
    [InlineData("sp1001", 1001)]
    [InlineData(" 1042 ", 1042)]
    public void Workspace_reference_accepts_code_or_number(string value, int expected)
    {
        SelfServeAdminService.TryParseWorkspaceNo(value, out var no).Should().BeTrue();
        no.Should().Be(expected);
    }

    [Theory]
    [InlineData("SP")]
    [InlineData("SP-1")]
    [InlineData("0")]
    [InlineData("SP1042.M01")]
    public void Workspace_reference_rejects_anything_else(string value) =>
        SelfServeAdminService.TryParseWorkspaceNo(value, out _).Should().BeFalse();

    [Fact]
    public void As_of_accepts_relative_days_thai_dates_and_iso_times()
    {
        var now = new DateTime(2026, 10, 14, 5, 0, 0, DateTimeKind.Utc);
        var dailyAt = new TimeOnly(3, 30);

        ValueParsers.TryParseAsOf("+91d", now, dailyAt, out var plus).Should().BeTrue();
        plus.Should().Be(now.AddDays(91));

        ValueParsers.TryParseAsOf("-1d", now, dailyAt, out var minus).Should().BeTrue();
        minus.Should().Be(now.AddDays(-1));

        ValueParsers.TryParseAsOf("2026-12-31", now, dailyAt, out var date).Should().BeTrue();
        date.Should().Be(new DateTime(2026, 12, 30, 20, 30, 0, DateTimeKind.Utc), "03:30 เวลาไทยของวันนั้น");

        ValueParsers.TryParseAsOf("2026-12-31T03:30:00+07:00", now, dailyAt, out var iso).Should().BeTrue();
        iso.Should().Be(new DateTime(2026, 12, 30, 20, 30, 0, DateTimeKind.Utc));

        ValueParsers.TryParseAsOf("2026-12-31T03:30:00", now, dailyAt, out var thai).Should().BeTrue();
        thai.Should().Be(iso, "ไม่มี offset = เวลาไทย");

        ValueParsers.TryParseAsOf("tomorrow", now, dailyAt, out _).Should().BeFalse();
    }

    [Fact]
    public void Until_is_inclusive_of_the_whole_thai_day()
    {
        SelfServeAdminService.UntilToEndsAtUtc(new DateOnly(2027, 12, 31))
            .Should().Be(new DateTime(2027, 12, 31, 17, 0, 0, DateTimeKind.Utc), "EndPackageDate = 2028-01-01 00:00 เวลาไทย");
    }

    [Fact]
    public void Actor_prefers_the_by_option()
    {
        CommandRunner.Actor(CommandParser.Parse(["suspend", "--workspace", "1", "--reason", "x", "--by", "noi"]).Command!)
            .Should().Be("spadmin:noi");
    }

    // ───────────────────────────────────────────── DI ของ host

    [Fact]
    public async Task Host_services_resolve_for_every_command_without_touching_a_database()
    {
        const string selfServe = "Server=model-only;Database=GarageSelfServe_Dev;User Id=a;Password=b";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:ServiceDb"] = "Server=model-only;Database=GarageService;User Id=a;Password=b",
            ["LegacyShards:ConnectionStrings:db2"] = "Server=model-only;Database=Garage;User Id=a;Password=b",
            ["LegacyShards:ConnectionStrings:sp1"] = selfServe,
            ["SelfServe:ConnectionString"] = selfServe
        }).Build();
        SpAdminHost.CheckConfiguration(configuration).Should().BeNull();

        var services = new ServiceCollection();
        services.AddLogging();
        SpAdminHost.ConfigureServices(services, configuration);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<SelfServeAdminService>().Should().NotBeNull();
        sp.GetRequiredService<IConversionService>().Should().BeOfType<ConversionService>();
        sp.GetRequiredService<ISelfServeAdminRepository>().CheckWritableDatabase().Should().BeNull();
        sp.GetRequiredService<ICurrentUser>().ShardKey.Should().Be("db2", "scope ที่ไม่ได้ตั้ง override = ผู้ใช้ระบบของ db2");

        // ทุก service ที่ ConversionService ใช้ใน scope ปลายทาง resolve ได้ (ICurrentUser มาจาก override)
        var shardScopes = sp.GetRequiredService<IShardScopeFactory>();
        var target = await shardScopes.RunAsAsync(
            new ScopedCurrentUser("db2", 431, 98765, "spadmin", UserRole.Manager, EventSource.Web, 7001),
            (inner, _) =>
            {
                inner.GetRequiredService<ICurrentUser>().BranchId.Should().Be(431);
                inner.GetRequiredService<ICatalogCategoryService>();
                inner.GetRequiredService<IWarehouseService>();
                inner.GetRequiredService<ICatalogService>();
                inner.GetRequiredService<IQuotationTemplateService>();
                inner.GetRequiredService<IStaffService>();
                inner.GetRequiredService<ICustomerVehicleService>();
                inner.GetRequiredService<ServiceDbContext>();
                return Task.FromResult(true);
            });
        target.Should().BeTrue();
    }

    [Fact]
    public void Missing_self_serve_configuration_is_reported_without_values()
    {
        var empty = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        SpAdminHost.CheckConfiguration(empty).Should().Contain("SelfServe:ConnectionString");

        var noShadow = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SelfServe:ConnectionString"] = "Server=x;Database=GarageSelfServe;Password=secret"
        }).Build();
        var message = SpAdminHost.CheckConfiguration(noShadow);
        message.Should().Contain("LegacyShards:ConnectionStrings:sp1");
        message.Should().NotContain("secret");
    }
}
