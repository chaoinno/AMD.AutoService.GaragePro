using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Internal;
using AMD.AutoService.GaragePro.API.Internal;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure;
using AMD.AutoService.GaragePro.Infrastructure.Internal;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// SQL ของ API ภายใน (docs/17 §8.6) โดยไม่ต่อฐาน: query ของ EF แปลได้และรวมเป็น GROUP BY ครั้งเดียว ·
/// SQL ของ Garage เดิมเลือกคอลัมน์ + READUNCOMMITTED + กลุ่ม Service · DI resolve ได้ทั้งมีและไม่มี self-serve
/// </summary>
public class InternalSiteSqlTests
{
    private const string Cs = "Server=offline;Database=Offline;User Id=a;Password=b";

    [Fact]
    public void Job_activity_is_one_grouped_query_per_shard()
    {
        using var db = new ServiceDbContext(new DbContextOptionsBuilder<ServiceDbContext>().UseSqlServer(Cs).Options);

        var sql = InternalSiteReader.JobActivityQuery(db, "db2", new DateTime(2026, 9, 22, 3, 0, 0, DateTimeKind.Utc))
            .ToQueryString();

        sql.Should().Contain("[svc_Job]").And.Contain("[LegacyShardKey] = ").And.Contain("GROUP BY [s].[BranchId]");
        sql.Should().Contain("COUNT(CASE").And.Contain("MAX(");
        sql.Should().NotContain("WHERE [s].[CreatedAt]", "ช่วง 30 วันอยู่ใน COUNT เพื่อให้จ๊อบล่าสุดเป็นค่าตลอดกาล");
    }

    [Fact]
    public void Payment_summary_is_one_grouped_query()
    {
        var options = new DbContextOptionsBuilder<SelfServeDbContext>();
        SelfServeDbContext.UseSelfServeSqlServer(options, Cs);
        using var db = new SelfServeDbContext((DbContextOptions<SelfServeDbContext>)options.Options);

        var sql = PackagePaymentRepository.SummaryQuery(db).ToQueryString();

        sql.Should().Contain("[sp_PackagePayment]").And.Contain("GROUP BY [s].[WorkspaceId]")
            .And.Contain("N'Pending'").And.Contain("MAX([s].[SubmittedAt])");
    }

    [Fact]
    public void Legacy_branch_sql_selects_columns_without_locks_and_only_the_service_group()
    {
        var sql = InternalSiteReader.GarageProBranchesSql;

        sql.Should().Contain("WITH (READUNCOMMITTED)").And.Contain("WHERE b.BranchGroupId = 7");
        sql.Should().NotContain("*").And.NotContainAny("INSERT", "UPDATE", "DELETE", "MERGE");
        sql.Should().ContainAll("b.Id", "b.Name", "b.PhoneNumber1", "b.Status", "b.StartPackageDate",
            "b.EndPackageDate", "b.CreatedDate");

        SelfServeAdminRepository.BranchPackagesSql.Should().Contain("WITH (READUNCOMMITTED)")
            .And.ContainAll("b.Status", "b.StartPackageDate", "b.EndPackageDate").And.NotContain("*");
    }

    private sealed class StubUser : ICurrentUser
    {
        public long UserId => 0;
        public string UserName => "anonymous";
        public UserRole Role => UserRole.FrontDesk;
        public string ShardKey => "db2";
        public int BranchId => 0;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Sites_service_resolves_with_or_without_self_serve(bool selfServe)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:ServiceDb"] = "Server=model-only;Database=GarageService;User Id=a;Password=b",
            ["LegacyShards:ConnectionStrings:db2"] = "Server=model-only;Database=Garage;User Id=a;Password=b",
            ["Internal:ApiKey"] = "0123456789abcdef0123456789abcdef"
        };
        if (selfServe)
        {
            settings["SelfServe:ConnectionString"] = "Server=model-only;Database=GarageSelfServe_Dev;User Id=a;Password=b";
            settings["LegacyShards:ConnectionStrings:sp1"] = settings["SelfServe:ConnectionString"];
        }
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGarageProInfrastructure(configuration);
        services.AddScoped<ICurrentUser, StubUser>();
        InternalApi.AddInternalApi(services, configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<InternalSitesService>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IInternalSiteReader>().Should().BeOfType<InternalSiteReader>();
        scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<InternalOptions>>().Value
            .EffectiveKey.Should().NotBeNull();
    }
}
