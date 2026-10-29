using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.AppUpdates;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class SessionUpdatesSqlFactAttribute : FactAttribute
{
    public const string Variable = "GARAGEPRO_AUTH_SQL_CONNECTION";
    public SessionUpdatesSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = "Set GARAGEPRO_AUTH_SQL_CONNECTION to a disposable GaragePro_Test_* database (never a real service database).";
    }
}

public sealed class SessionAndUpdatesSqlTests
{
    [SessionUpdatesSqlFact]
    public async Task Durable_fences_and_policy_audits_are_atomic_across_independent_instances()
    {
        var connection = Environment.GetEnvironmentVariable(SessionUpdatesSqlFactAttribute.Variable)!;
        var config = new SqlConnectionStringBuilder(connection);
        if (!config.InitialCatalog.StartsWith("GaragePro_Test_", StringComparison.Ordinal))
            throw new InvalidOperationException("This test may only create/migrate a disposable GaragePro_Test_* database.");
        await using (var schema = new ServiceDbContext(new DbContextOptionsBuilder<ServiceDbContext>().UseSqlServer(connection).Options))
            await schema.Database.MigrateAsync();
        AppUpdatesDbContext NewPolicyDb() => new(new DbContextOptionsBuilder<AppUpdatesDbContext>().UseSqlServer(connection,
            sql => sql.MigrationsHistoryTable("__AppUpdatesMigrationsHistory")).Options);
        await using (var schema = NewPolicyDb()) await schema.Database.MigrateAsync();
        var shard = "test-" + Guid.NewGuid().ToString("N")[..12];
        var platform = "t" + Guid.NewGuid().ToString("N")[..8];
        var a = new SessionVersionStore(new Connections(connection), NullLogger<SessionVersionStore>.Instance);
        var b = new SessionVersionStore(new Connections(connection), NullLogger<SessionVersionStore>.Instance);
        try
        {
            (await a.ReadAsync(shard, 1)).Version.Should().Be(0);
            await a.ChangeStaffStatusAsync(shard, 7, [1, 2], true, async () => {
                (await b.ReadAsync(shard, 1)).Should().Be(new SessionVersion(1, true));
                (await b.ReadAsync(shard, 2)).Should().Be(new SessionVersion(1, true));
                return true;
            });
            (await b.ReadAsync(shard, 1)).Should().Be(new SessionVersion(1, false));
            await b.ChangeStaffStatusAsync(shard, 7, [1, 2], false, () => Task.FromResult(true));
            (await a.ReadAsync(shard, 1)).Version.Should().Be(1);
            // error ของฐานเดิมส่งต่อตามจริง ไม่ถูกห่อเป็น "ตรวจสอบสิทธิ์ไม่ได้"
            await Assert.ThrowsAsync<IOException>(() => a.ChangeStaffStatusAsync(shard, 7, [1, 2], true,
                () => throw new IOException("simulated legacy write failure")));
            (await b.ReadAsync(shard, 1)).Should().Be(new SessionVersion(2, false));
            // A stale observation must not bump repeatedly, even with different processes racing.
            await Task.WhenAll(a.RevokeObservedAsync(shard, 1, 2), b.RevokeObservedAsync(shard, 1, 2));
            (await a.ReadAsync(shard, 1)).Version.Should().Be(3);
            (await a.ReadAsync(shard, 3)).Version.Should().Be(0);
            (await a.ReadAsync("other", 1)).Version.Should().Be(0);

            var changing = 0;
            async Task<bool> LegacyWrite()
            {
                Interlocked.Increment(ref changing).Should().Be(1, "both status writes must share the same staff lock");
                (await b.ReadAsync(shard, 11)).Changing.Should().BeTrue();
                await Task.Delay(75);
                Interlocked.Decrement(ref changing);
                return true;
            }
            await Task.WhenAll(a.ChangeStaffStatusAsync(shard, 17, [11], true, LegacyWrite),
                b.ChangeStaffStatusAsync(shard, 17, [11], false, LegacyWrite));
            (await a.ReadAsync(shard, 11)).Should().Be(new SessionVersion(1, false));

            // ธง Changing ที่ process ตายทิ้งไว้ต้องหมดผลเอง ไม่งั้นบัญชีได้ 503 ตลอดไป
            await using (var sql = new SqlConnection(connection))
            {
                await sql.ExecuteAsync("UPDATE svc_UserSessionVersion SET Changing=1,UpdatedAt=SYSUTCDATETIME() WHERE ShardKey=@shard AND UserId=11", new { shard });
                (await a.ReadAsync(shard, 11)).Changing.Should().BeTrue();
                await sql.ExecuteAsync("UPDATE svc_UserSessionVersion SET UpdatedAt=DATEADD(SECOND,-@age,SYSUTCDATETIME()) WHERE ShardKey=@shard AND UserId=11",
                    new { shard, age = SessionVersionStore.StaleChangingSeconds + 1 });
                (await a.ReadAsync(shard, 11)).Should().Be(new SessionVersion(1, false));
            }

            var unavailable = new SqlConnectionStringBuilder(connection) { DataSource = "127.0.0.1,1", ConnectTimeout = 1 };
            var offline = new SessionVersionStore(new Connections(unavailable.ConnectionString), NullLogger<SessionVersionStore>.Instance);
            await Assert.ThrowsAsync<SessionVerificationException>(() => offline.ReadAsync(shard, 1));
            var legacyCalled = false;
            await Assert.ThrowsAsync<SessionVerificationException>(() => offline.ChangeStaffStatusAsync(shard, 7, [1], true,
                () => { legacyCalled = true; return Task.FromResult(true); }));
            legacyCalled.Should().BeFalse("legacy status cannot change before durable revocation commits");

            var actor = new ScopedCurrentUser(shard, 1, 1, "SQL test");
            var initial = AppVersions.Default(platform);
            await using var firstDb = NewPolicyDb(); await using var secondDb = NewPolicyDb();
            var first = new AppUpdateRepository(firstDb, TimeProvider.System, NullLogger<AppUpdateRepository>.Instance);
            var second = new AppUpdateRepository(secondDb, TimeProvider.System, NullLogger<AppUpdateRepository>.Instance);
            var created = await Task.WhenAll(first.SaveAsync(initial, initial with { Revision = 1 }, actor, default),
                second.SaveAsync(initial, initial with { Revision = 1 }, actor, default));
            created.Count(x => x).Should().Be(1);
            await using var thirdDb = NewPolicyDb(); await using var fourthDb = NewPolicyDb();
            var third = new AppUpdateRepository(thirdDb, TimeProvider.System, NullLogger<AppUpdateRepository>.Instance);
            var fourth = new AppUpdateRepository(fourthDb, TimeProvider.System, NullLogger<AppUpdateRepository>.Instance);
            var current = await third.ReadAsync(platform, default);
            var updated = await Task.WhenAll(third.SaveAsync(current, current with { Revision = 2, Enabled = true }, actor, default),
                fourth.SaveAsync(current, current with { Revision = 2, Enabled = false }, actor, default));
            updated.Count(x => x).Should().Be(1);
            var history = await third.HistoryAsync(platform, default);
            history.Count.Should().Be(2);
            history[0].Before.Revision.Should().Be(1);
            history[0].After.Revision.Should().Be(2);
            history[0].EditedByShard.Should().Be(shard);
        }
        finally
        {
            await using var sql = new SqlConnection(connection);
            await sql.ExecuteAsync("DELETE svc_UserSessionVersion WHERE ShardKey=@shard; DELETE svc_AppUpdateAudit WHERE Platform=@platform; DELETE svc_AppUpdatePolicy WHERE Platform=@platform", new { shard, platform });
        }
    }
    private sealed class Connections(string value) : IServiceDbConnectionResolver
    {
        public string Resolve(string shardKey) => value;
    }
}
