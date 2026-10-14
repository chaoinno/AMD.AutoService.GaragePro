using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// เส้นแบ่งฐานข้อมูลระหว่างอู่ทดลองใช้ (sp1 → GarageSelfServe) กับลูกค้าจริง (→ GarageService)
/// — ถ้าพัง แถว svc_* ของ sp1 จะไปปนในฐานลูกค้าจริงโดยไม่มีอะไรฟ้อง
/// </summary>
public class ServiceDbConnectionResolverTests
{
    private const string ServiceDb = "Server=x;Database=GarageService";
    private const string SelfServeDb = "Server=x;Database=GarageSelfServe_Dev";

    private static ServiceDbConnectionResolver Resolver(string? selfServeConnection, string shardKey = "sp1")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:ServiceDb"] = ServiceDb })
            .Build();
        var options = new SelfServeOptions { ShardKey = shardKey, ConnectionString = selfServeConnection ?? "" };
        return new ServiceDbConnectionResolver(configuration, Options.Create(options));
    }

    [Theory]
    [InlineData("db2")]
    [InlineData("db1")]
    [InlineData("rc-0123456789abcdef")]
    public void Legacy_shards_keep_using_GarageService(string shard)
    {
        Resolver(SelfServeDb).Resolve(shard).Should().Be(ServiceDb);
    }

    [Theory]
    [InlineData("sp1")]
    [InlineData("SP1")]
    [InlineData(" sp1 ")]
    public void Self_serve_shard_uses_its_own_database(string shard)
    {
        Resolver(SelfServeDb).Resolve(shard).Should().Be(SelfServeDb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Self_serve_shard_without_a_connection_fails_loudly_instead_of_falling_back(string? connection)
    {
        var act = () => Resolver(connection).Resolve("sp1");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SelfServe:ConnectionString*");
    }

    [Fact]
    public void Legacy_shard_works_without_any_self_serve_configuration()
    {
        Resolver(null).Resolve("db2").Should().Be(ServiceDb);
    }

    [Fact]
    public void The_self_serve_shard_key_comes_from_configuration()
    {
        var resolver = Resolver(SelfServeDb, shardKey: "sp9");

        resolver.Resolve("sp9").Should().Be(SelfServeDb);
        resolver.Resolve("sp1").Should().Be(ServiceDb, "sp1 ไม่ใช่ shard self-serve ในการตั้งค่านี้");
    }
}
