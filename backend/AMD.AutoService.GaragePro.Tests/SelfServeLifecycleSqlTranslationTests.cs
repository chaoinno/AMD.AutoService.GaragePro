using System.Data;
using System.Data.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// รัน EF ของ S4 จริง (แผนลบ svc_* · repository ของงานรายวัน) โดย<b>ไม่ต่อฐาน</b>: interceptor ระงับการเปิด connection
/// และการรันคำสั่ง แล้วเก็บ SQL ที่ EF แปลได้ — จับ LINQ ที่แปลเป็น SQL ไม่ได้ (ExecuteDelete + subquery) ตั้งแต่ unit test
/// และยืนยันว่าทุกคำสั่งลบ/แก้กรองด้วยขอบเขตของสาขา ไม่ใช่ทั้งตาราง
/// </summary>
public class SelfServeLifecycleSqlTranslationTests
{
    private sealed class OfflineSql : DbConnectionInterceptor, IDbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData,
            InterceptionResult result) => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
            ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());

        public ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
        }

        public ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(new DataTable().CreateDataReader()));
        }
    }

    private const string Cs = "Server=offline;Database=GarageSelfServe_Offline;User Id=a;Password=b";

    private static (ServiceDbContext Db, OfflineSql Sql) ServiceDb()
    {
        var sql = new OfflineSql();
        var db = new ServiceDbContext(new DbContextOptionsBuilder<ServiceDbContext>()
            .UseSqlServer(Cs).AddInterceptors(sql).Options);
        return (db, sql);
    }

    [Fact]
    public async Task Every_purge_step_translates_and_every_statement_is_scoped()
    {
        var (db, sql) = ServiceDb();
        await using var _ = db;
        var scope = new ServicePurgeScope("sp1", 61, [11, 12]);

        await ServicePurgePlan.AttachmentPathsAsync(db, scope, CancellationToken.None);
        foreach (var step in ServicePurgePlan.Steps)
        {
            var act = () => step.Run(db, scope, CancellationToken.None);
            await act.Should().NotThrowAsync($"{step.Table} ต้องแปลเป็น SQL ได้");
        }

        var writes = sql.Commands.Where(c => c.Contains("DELETE") || c.StartsWith("UPDATE")).ToList();
        writes.Count.Should().BeGreaterThanOrEqualTo(ServicePurgePlan.Steps.Count);
        foreach (var command in writes)
            command.Should().Match(c => c.Contains("ShardKey") || c.Contains("PerformedByUserId"),
                $"ทุกคำสั่งต้องกรองด้วย shard/สาขา (หรือผู้ใช้ของสาขา):\n{command}");

        writes.Should().Contain(c => c.Contains("[svc_ActivityEvent]") && c.Contains("[PerformedByUserId]"));
        writes.Should().Contain(c => c.StartsWith("UPDATE") && c.Contains("[ReplyToMessageId] = NULL"));
    }

    [Fact]
    public void Quotation_count_translates_with_the_branch_filter()
    {
        var (db, _) = ServiceDb();
        using var __ = db;

        var text = SelfServeLifecycleRepository.QuotationsEverSent(db, "sp1", 61).ToQueryString();

        text.Should().Contain("[svc_Quotation]").And.Contain("[SentAt] IS NOT NULL").And.Contain("[BranchId]");
    }

    [Fact]
    public async Task Repository_queries_and_claims_translate()
    {
        var sql = new OfflineSql();
        // ไม่เปิด retry: execution strategy จะ buffer reader แล้วตรวจจำนวนคอลัมน์ของผลลัพธ์ว่างที่ interceptor คืน
        await using var db = new SelfServeDbContext(new DbContextOptionsBuilder<SelfServeDbContext>()
            .UseSqlServer(Cs).AddInterceptors(sql).Options);
        var repository = new SelfServeLifecycleRepository(db, new ServiceDbConnectionResolver(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), Options.Create(new SelfServeOptions())),
            Options.Create(new LegacyShardOptions()), Options.Create(new SelfServeOptions()));
        var now = new DateTime(2026, 10, 14, 3, 0, 0, DateTimeKind.Utc);

        (await repository.FindByBranchAsync(61)).Should().BeNull();
        (await repository.ListForLifecycleAsync()).Should().BeEmpty();
        (await repository.ListAbandonedNeedingServicePurgeAsync()).Should().BeEmpty();
        (await repository.TryRecordExtensionRequestAsync(Guid.NewGuid(), now, now.AddHours(-24), "x", null))
            .Should().BeFalse("UPDATE ที่ไม่โดนแถวไหน = มีคำขอในช่วงนั้นแล้ว");
        await repository.RemoveNoticeAsync(Guid.NewGuid(), "reminder-7");
        await repository.CompleteJobRunAsync("maintenance", new DateOnly(2026, 10, 14), now, "ok", null);

        sql.Commands.Should().Contain(c => c.Contains("[sp_Workspace]") && c.Contains("LEFT JOIN [sp_Account]"));
        sql.Commands.Should().Contain(c => c.StartsWith("UPDATE") && c.Contains("[ExtensionRequestedAt]"));
        sql.Commands.Should().Contain(c => c.Contains("DELETE") && c.Contains("[sp_WorkspaceNotice]"));
        sql.Commands.Should().Contain(c => c.Contains("[sp_WorkspaceAudit]") && c.Contains("provision-abandoned")
                                           && c.Contains("abandoned-svc-purged"));
        sql.Commands.Should().Contain(c => c.StartsWith("UPDATE") && c.Contains("[sp_JobRun]"));
    }

    [Fact]
    public void Workspace_status_values_are_stored_as_strings()
    {
        var options = new DbContextOptionsBuilder<SelfServeDbContext>();
        SelfServeDbContext.UseSelfServeSqlServer(options, Cs);
        using var db = new SelfServeDbContext((DbContextOptions<SelfServeDbContext>)options.Options);

        db.Workspaces.Where(w => w.Status != WorkspaceStatus.Purged).ToQueryString().Should().Contain("N'Purged'",
            "Status เป็น string ในฐาน — repository เทียบ enum ได้โดยไม่ต้องรู้ตัวเลข");
    }
}
