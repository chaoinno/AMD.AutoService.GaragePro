using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// DI ของ AddGarageProInfrastructure ทั้งก้อน (ไม่ต่อฐานจริง — แค่ดู connection string ที่ context ได้):
/// · deployment ที่ไม่มี SelfServe:ConnectionString ต้องเหมือนเดิมทุกประการ
/// · ตั้งค่าแล้ว ServiceDbContext ของ scope sp1 ต้องชี้ GarageSelfServe และ scope ปกติยังชี้ GarageService
/// </summary>
public class SelfServeDbPlumbingTests
{
    private const string ServiceDb = "Server=design-time-placeholder;Database=GarageService;TrustServerCertificate=True";
    private const string SelfServeDb = "Server=design-time-placeholder;Database=GarageSelfServe_Dev;TrustServerCertificate=True";

    /// <summary>shard ของคำขอปกติ (แทน CurrentUser ที่อ่าน JWT) — Program.cs ใช้ factory รูปเดียวกันนี้</summary>
    private sealed class RequestUser(string shard) : ICurrentUser
    {
        public long UserId => 1;
        public string UserName => "request";
        public UserRole Role => UserRole.Manager;
        public string ShardKey => shard;
        public int BranchId => 227;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
    }

    private static ServiceProvider Build(bool selfServe, string requestShard = "db2")
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:ServiceDb"] = ServiceDb };
        if (selfServe)
        {
            settings["SelfServe:ConnectionString"] = SelfServeDb;
            settings["LegacyShards:ConnectionStrings:sp1"] = SelfServeDb;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<ICurrentUser>(sp =>
            sp.GetRequiredService<CurrentUserOverride>().User ?? new RequestUser(requestShard));
        services.AddGarageProInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private static string? ConnectionOf(DbContext db) => db.Database.GetConnectionString();

    [Fact]
    public void Without_self_serve_configuration_everything_behaves_like_before()
    {
        using var provider = Build(selfServe: false);
        using var scope = provider.CreateScope();

        ConnectionOf(scope.ServiceProvider.GetRequiredService<ServiceDbContext>()).Should().Be(ServiceDb);
        scope.ServiceProvider.GetRequiredService<IWorkspacePolicy>().Should().BeOfType<UnlimitedWorkspacePolicy>();
        scope.ServiceProvider.GetService<SelfServeDbContext>().Should().BeNull(
            "ไม่มีฐาน self-serve = ไม่ลงทะเบียน context ให้ใครเผลอใช้");
        SelfServeInfrastructure.IsConfigured(new ConfigurationBuilder().Build()).Should().BeFalse();
    }

    [Fact]
    public async Task Self_serve_scope_without_configuration_fails_loudly()
    {
        await using var provider = Build(selfServe: false);
        var factory = provider.GetRequiredService<IShardScopeFactory>();

        var act = () => factory.RunAsAsync(new ScopedCurrentUser("sp1", 1001, 1, "system"),
            (sp, _) => Task.FromResult(sp.GetRequiredService<ServiceDbContext>()));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*SelfServe:ConnectionString*");
    }

    [Fact]
    public async Task With_self_serve_configured_the_database_follows_the_shard_of_the_scope()
    {
        await using var provider = Build(selfServe: true);

        using (var ordinary = provider.CreateScope())
            ConnectionOf(ordinary.ServiceProvider.GetRequiredService<ServiceDbContext>()).Should().Be(ServiceDb);

        var selfServe = await provider.GetRequiredService<IShardScopeFactory>().RunAsAsync(
            new ScopedCurrentUser("sp1", 1001, 1, "system"),
            (sp, _) => Task.FromResult(ConnectionOf(sp.GetRequiredService<ServiceDbContext>())));

        selfServe.Should().Be(SelfServeDb);
    }

    [Fact]
    public void Request_with_an_sp1_token_gets_the_self_serve_database()
    {
        using var provider = Build(selfServe: true, requestShard: "sp1");
        using var scope = provider.CreateScope();

        ConnectionOf(scope.ServiceProvider.GetRequiredService<ServiceDbContext>()).Should().Be(SelfServeDb);
    }

    [Fact]
    public void With_self_serve_configured_the_real_policy_and_context_are_registered()
    {
        using var provider = Build(selfServe: true);
        using var scope = provider.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IWorkspacePolicy>().Should().BeOfType<WorkspacePolicy>();
        sp.GetRequiredService<IPlanUsageCounter>().Should().BeOfType<PlanUsageCounter>();

        var selfServeDb = sp.GetRequiredService<SelfServeDbContext>();
        ConnectionOf(selfServeDb).Should().Be(SelfServeDb);
        RelationalOptionsExtension.Extract(selfServeDb.GetService<IDbContextOptions>())
            .MigrationsHistoryTableName.Should().Be(SelfServeDbContext.MigrationsHistoryTable,
                "ใช้ __EFMigrationsHistory ร่วมกับ svc_* ในฐานเดียวกันไม่ได้");
    }

    [Fact]
    public void Self_serve_model_keeps_the_contract_of_plan_section_3()
    {
        using var provider = Build(selfServe: true);
        using var scope = provider.CreateScope();
        // design-time model — runtime model ไม่เก็บค่าอย่าง identity seed ไว้
        var model = scope.ServiceProvider.GetRequiredService<SelfServeDbContext>().GetService<IDesignTimeModel>().Model;

        model.GetEntityTypes().Select(e => e.GetTableName()).Should().BeEquivalentTo(
            "sp_Account", "sp_Workspace", "sp_SignupAttempt", "sp_EmailOtp", "sp_BlockedDomain", "sp_JobRun",
            "sp_WorkspaceNotice", "sp_SampleEntity", "sp_ConversionMap", "sp_WorkspaceAudit");

        var account = model.FindEntityType(typeof(Domain.Entities.SelfServe.SelfServeAccount))!;
        account.GetIndexes().Single(i => i.GetDatabaseName() == "UX_sp_Account_NormalizedEmail")
            .IsUnique.Should().BeTrue();
        account.FindProperty(nameof(Domain.Entities.SelfServe.SelfServeAccount.Id))!.ValueGenerated
            .Should().Be(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never);
        account.FindProperty(nameof(Domain.Entities.SelfServe.SelfServeAccount.Status))!.GetProviderClrType()
            .Should().Be(typeof(string), "enum ของ sp_* เก็บเป็นข้อความ");

        var workspace = model.FindEntityType(typeof(Domain.Entities.SelfServe.Workspace))!;
        var workspaceNo = workspace.FindProperty(nameof(Domain.Entities.SelfServe.Workspace.WorkspaceNo))!;
        workspaceNo.GetIdentitySeed().Should().Be(1001);
        workspaceNo.GetIdentityIncrement().Should().Be(1);
        workspace.GetIndexes().Single(i => i.GetDatabaseName() == "UX_sp_Workspace_BranchId")
            .GetFilter().Should().Be("[BranchId] IS NOT NULL");
    }
}
