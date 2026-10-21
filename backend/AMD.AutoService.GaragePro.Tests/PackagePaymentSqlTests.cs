using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// sp_PackagePayment กับฐาน GarageSelfServe_Dev จริง (docs/17 §10 "การทดสอบที่ต้องมี"): filtered unique index · requestId ซ้ำ ·
/// อนุมัติ + ต่อวัน + audit ใน transaction เดียว · อนุมัติพร้อมกันได้ผลครั้งเดียว · วันหมดเปลี่ยนระหว่างอนุมัติ = ยกเลิกทั้งหมด ·
/// ความละเอียด datetime ของ Branch · ค้นด้วย 8 ตัวแรกบน SQL Server · ข้อมูลของ purge
/// · [RISK] เขียนข้อมูลจริง: ทุกแถวมีรหัสสุ่มของตัวเองและถูกลบใน finally · ตั้ง <c>GARAGEPRO_SELFSERVE_SQL_CONNECTION</c> เฉพาะฐาน dev
/// </summary>
public class PackagePaymentSqlTests
{
    private const string Shard = "sp1";
    private static string Connection => Environment.GetEnvironmentVariable(SelfServeShardSqlFactAttribute.Variable)!;

    private static readonly SelfServeOptions SelfServe = new() { ShardKey = Shard };

    private static IOptions<LegacyShardOptions> LegacyOptions() =>
        Options.Create(new LegacyShardOptions { ConnectionStrings = { [Shard] = Connection } });

    private static IOptions<SelfServeOptions> SelfServeOptionsFor() =>
        Options.Create(new SelfServeOptions { ShardKey = Shard, ConnectionString = Connection });

    private static SelfServeDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<SelfServeDbContext>();
        SelfServeDbContext.UseSelfServeSqlServer(builder, Connection);
        return new SelfServeDbContext(builder.Options);
    }

    private static SelfServeAdminRepository NewAdmin(SelfServeDbContext db) => new(db,
        new WorkspacePlanReader(LegacyOptions(), db), new ConfigurationBuilder().Build(), LegacyOptions(), SelfServeOptionsFor());

    private sealed record Fixture(Guid AccountId, Workspace Workspace, int BranchId, DateTime Now);

    /// <summary>อู่ทดลองใช้ที่เปิดเสร็จแล้ว (Active) + Branch ในตาราง shadow · วันหมด = now + 30 วัน (ไม่ใช่เที่ยงคืน)</summary>
    private static async Task<Fixture> CreateWorkspaceAsync(SelfServeDbContext db, string tag)
    {
        var accountId = Guid.NewGuid();
        db.Accounts.Add(new SelfServeAccount
        {
            Id = accountId, NormalizedEmail = $"pkg-sql-{tag}@example.invalid", Email = $"pkg-sql-{tag}@example.invalid",
            Status = SelfServeAccountStatus.Active, CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var repository = new WorkspaceRepository(db, LegacyOptions(), new MemoryCache(new MemoryCacheOptions()),
            Options.Create(SelfServe), NullLogger<WorkspaceRepository>.Instance);
        var now = DateTime.SpecifyKind(DateTime.UtcNow.AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond)),
            DateTimeKind.Utc);
        var workspace = await repository.AddWorkspaceAsync(new Workspace
        {
            Id = Guid.NewGuid(), Name = $"PKG SQL {tag}", Status = WorkspaceStatus.Provisioning,
            SignupRequestId = $"req-{tag}", OwnerAccountId = accountId, CreatedAt = now
        });
        var branchId = await repository.CreateShadowBranchAsync(Shard, workspace, new ShadowBranchSpec(
            $"PKG SQL {tag}", null, null, null, "1234", now, 30, now), WorkspaceProvisionSteps.BranchCreated, now);

        await db.Workspaces.Where(w => w.Id == workspace.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkspaceStatus.Active).SetProperty(w => w.PurgeAfter, now.AddDays(200)));
        db.ChangeTracker.Clear();
        return new Fixture(accountId, workspace, branchId, now);
    }

    private static async Task CleanupAsync(Fixture? f)
    {
        if (f is null) return;
        await using var sql = new SqlConnection(Connection);
        await sql.ExecuteAsync("""
            DELETE FROM sp_PackagePayment WHERE WorkspaceId = @WorkspaceId;
            DELETE FROM sp_WorkspaceAudit WHERE WorkspaceId = @WorkspaceId;
            DELETE FROM sp_Workspace WHERE Id = @WorkspaceId;
            DELETE FROM sp_Account WHERE Id = @AccountId;
            DELETE FROM Branch WHERE Id = @BranchId AND NOT EXISTS (SELECT 1 FROM Staff WHERE BranchId = @BranchId);
            """, new { WorkspaceId = f.Workspace.Id, f.AccountId, f.BranchId });
    }

    private static PackagePayment NewPayment(Guid workspaceId, string requestId, DateTime now, Guid? id = null,
        string sha = "0000000000000000000000000000000000000000000000000000000000000000") => new()
    {
        Id = id ?? Guid.NewGuid(),
        WorkspaceId = workspaceId,
        RequestId = requestId,
        Status = PackagePaymentStatus.Pending,
        MonthCount = 3,
        Amount = 1770m,
        ExpectedAmount = 1770m,
        PaidAt = now.AddHours(-1),
        SlipPath = $"sp1/0/package-slip/{Guid.NewGuid():N}/{Guid.NewGuid():N}.jpg",
        SlipSha256 = sha,
        SlipContentType = "image/jpeg",
        SlipSizeBytes = 1000,
        Note = "จากเทสต์",
        SubmittedByUserId = 1,
        SubmittedByUserName = "SP0000.M01",
        SubmittedAt = now
    };

    private static PackagePaymentAuditInfo Audit(string actor) => new(actor, null, "{}");

    [SelfServeShardSqlFact]
    public async Task Contract_indexes_exist_with_the_pending_filter()
    {
        await using var sql = new SqlConnection(Connection);
        var rows = (await sql.QueryAsync<(string Name, bool IsUnique, bool HasFilter, string? Filter)>("""
            SELECT i.name, i.is_unique, i.has_filter, i.filter_definition
            FROM sys.indexes i WHERE i.object_id = OBJECT_ID(N'dbo.sp_PackagePayment')
            """)).ToDictionary(r => r.Name);

        rows[SelfServeDbContext.OnePendingIndex].IsUnique.Should().BeTrue();
        rows[SelfServeDbContext.OnePendingIndex].HasFilter.Should().BeTrue();
        // login ของแอป (gp_selfserve_app) ไม่มี VIEW DEFINITION จึงอ่าน filter_definition เป็น NULL · พฤติกรรมจริงของ filter
        // ตรวจด้วย INSERT ตรงๆ ใน One_pending_per_workspace_... อยู่แล้ว
        if (rows[SelfServeDbContext.OnePendingIndex].Filter is { } filter) filter.Should().Contain("Pending");
        rows[SelfServeDbContext.WorkspaceRequestIndex].IsUnique.Should().BeTrue();
        rows.Should().ContainKeys("IX_sp_PackagePayment_Status_SubmittedAt", "IX_sp_PackagePayment_SlipSha256");
    }

    [SelfServeShardSqlFact]
    public async Task One_pending_per_workspace_and_request_ids_return_the_same_row()
    {
        await using var db = NewContext();
        Fixture? f = null;
        try
        {
            f = await CreateWorkspaceAsync(db, Guid.NewGuid().ToString("N")[..12]);
            var repo = new PackagePaymentRepository(db);
            var ws = f.Workspace.Id;

            var first = await repo.TryInsertAsync(NewPayment(ws, "req-a", f.Now), Audit("SP0000.M01"));
            first.Outcome.Should().Be(PackagePaymentInsertOutcome.Inserted);

            var second = await repo.TryInsertAsync(NewPayment(ws, "req-b", f.Now), Audit("SP0000.M01"));
            second.Outcome.Should().Be(PackagePaymentInsertOutcome.PendingExists);
            second.Payment!.Id.Should().Be(first.Payment!.Id);

            var again = await repo.TryInsertAsync(NewPayment(ws, "req-a", f.Now), Audit("SP0000.M01"));
            again.Outcome.Should().Be(PackagePaymentInsertOutcome.DuplicateRequest);
            again.Payment!.Id.Should().Be(first.Payment.Id);

            // ฐานเองต้องปฏิเสธ (ไม่ใช่แค่ repository เช็คก่อน)
            await using (var sql = new SqlConnection(Connection))
            {
                var raw = () => sql.ExecuteAsync("""
                    INSERT INTO sp_PackagePayment (Id, WorkspaceId, RequestId, Status, MonthCount, Amount, PaidAt, SlipPath,
                        SlipSha256, SlipContentType, SlipSizeBytes, SubmittedByUserId, SubmittedByUserName, SubmittedAt)
                    VALUES (NEWID(), @WorkspaceId, 'raw', N'Pending', 1, 1, SYSUTCDATETIME(), 'x', REPLICATE('0', 64),
                        'image/jpeg', 1, 1, 'raw', SYSUTCDATETIME())
                    """, new { WorkspaceId = ws });
                (await raw.Should().ThrowAsync<SqlException>()).Which.Number.Should().BeOneOf(2601, 2627);
            }

            (await repo.TryRejectAsync(first.Payment.Id, ws, PackagePaymentRejectReason.NotReceived, "ไม่พบยอด",
                f.Now.AddMinutes(1), Audit("spadmin:test"))).Should().BeTrue();
            (await repo.TryRejectAsync(first.Payment.Id, ws, PackagePaymentRejectReason.Duplicate, null,
                f.Now.AddMinutes(2), Audit("spadmin:other"))).Should().BeFalse("ปฏิเสธซ้ำไม่ได้");

            var afterReject = await repo.TryInsertAsync(NewPayment(ws, "req-c", f.Now), Audit("SP0000.M01"));
            afterReject.Outcome.Should().Be(PackagePaymentInsertOutcome.Inserted, "ปฏิเสธแล้วแจ้งใหม่ได้");
            (await repo.TryCancelAsync(afterReject.Payment!.Id, ws, f.Now.AddMinutes(3), Audit("SP0000.M01"))).Should().BeTrue();
            (await repo.TryCancelAsync(afterReject.Payment.Id, ws, f.Now.AddMinutes(4), Audit("SP0000.M01"))).Should().BeFalse();
            (await repo.TryInsertAsync(NewPayment(ws, "req-d", f.Now), Audit("SP0000.M01"))).Outcome
                .Should().Be(PackagePaymentInsertOutcome.Inserted, "ยกเลิกแล้วแจ้งใหม่ได้");

            var rejected = await repo.GetAsync(first.Payment.Id);
            rejected!.Status.Should().Be(PackagePaymentStatus.Rejected);
            rejected.RejectReason.Should().Be(PackagePaymentRejectReason.NotReceived);
            rejected.ReviewedBy.Should().Be("spadmin:test");

            var audits = await db.WorkspaceAudits.AsNoTracking().Where(a => a.WorkspaceId == ws).Select(a => a.Action).ToListAsync();
            audits.Count(a => a == PackagePaymentAuditActions.Submit).Should().Be(3);
            audits.Should().Contain([PackagePaymentAuditActions.Reject, PackagePaymentAuditActions.Cancel]);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    [SelfServeShardSqlFact]
    public async Task Approval_extends_the_branch_and_workspace_and_closes_the_payment_in_one_transaction()
    {
        await using var db = NewContext();
        Fixture? f = null;
        try
        {
            f = await CreateWorkspaceAsync(db, Guid.NewGuid().ToString("N")[..12]);

            // ค่ามิลลิวินาทีที่ datetime ปัดเป็น 1/300 วิ — การอนุมัติต้องเทียบค่าที่อ่านมาได้ตรงทุก tick
            await using (var sql = new SqlConnection(Connection))
                await sql.ExecuteAsync("UPDATE Branch SET EndPackageDate = DATEADD(ms, 123, EndPackageDate) WHERE Id = @Id",
                    new { Id = f.BranchId });

            var payments = new PackagePaymentRepository(db);
            var inserted = await payments.TryInsertAsync(NewPayment(f.Workspace.Id, "req-approve", f.Now), Audit("SP0000.M01"));
            var shortId = inserted.Payment!.Id.ToString("N")[..8];

            var clock = new PackageTestClock(f.Now.AddMinutes(5));
            var notifier = new FakePackageNotifier();
            var service = new PackagePaymentAdminService(payments, NewAdmin(db), notifier,
                new SelfServeOptions { ShardKey = Shard, ConnectionString = Connection }, clock);

            var result = await service.ApproveAsync(shortId, null, null, "spadmin:sql-test");

            result.Error.Should().BeNull();
            var r = result.Data!;
            var expectedEnd = PackageRenewal.Compute(r.EndsFrom, clock.Now, 3).Ends;
            r.EndsTo.Should().Be(expectedEnd);

            await using (var sql = new SqlConnection(Connection))
            {
                var branch = await sql.QuerySingleAsync<(DateTime Start, int Days, DateTime End)>(
                    "SELECT StartPackageDate, PackageDaysAmout, EndPackageDate FROM Branch WHERE Id = @Id", new { Id = f.BranchId });
                branch.End.Should().Be(BranchPackageDates.ToBranchLocal(expectedEnd));
                branch.End.TimeOfDay.Should().Be(TimeSpan.Zero, "วันหมดของรอบที่จ่ายเงินเป็นเที่ยงคืนไทย");
                branch.Days.Should().Be((int)(branch.End.Date - branch.Start.Date).TotalDays);
            }

            var workspace = await db.Workspaces.AsNoTracking().SingleAsync(w => w.Id == f.Workspace.Id);
            workspace.Plan.Should().Be(WorkspacePlanKind.Paid);
            workspace.Status.Should().Be(WorkspaceStatus.Active);
            workspace.TrialEndsAt.Should().Be(expectedEnd);
            workspace.PurgeAfter.Should().BeNull();

            var payment = await payments.GetAsync(inserted.Payment.Id);
            payment!.Status.Should().Be(PackagePaymentStatus.Approved);
            payment.ReviewedBy.Should().Be("spadmin:sql-test");
            payment.ReviewedAt.Should().Be(clock.Now);
            payment.PeriodEndsAt.Should().Be(expectedEnd);
            payment.PeriodStartsAt.Should().Be(PackageRenewal.CeilToThaiMidnight(r.EndsFrom!.Value));
            payment.PrevEndsAt.Should().Be(r.EndsFrom);

            var approveAudits = await db.WorkspaceAudits.AsNoTracking()
                .Where(a => a.WorkspaceId == f.Workspace.Id && a.Action == PackagePaymentAuditActions.Approve).ToListAsync();
            approveAudits.Should().ContainSingle();
            using var payload = JsonDocument.Parse(approveAudits[0].PayloadJson!);
            payload.RootElement.GetProperty("paymentId").GetGuid().Should().Be(inserted.Payment.Id);
            notifier.Reviewed.Should().ContainSingle();

            (await service.ApproveAsync(shortId, null, null, "spadmin:sql-test")).Error!.Code
                .Should().Be(PackageErrors.NotPendingCode);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    [SelfServeShardSqlFact]
    public async Task Two_concurrent_approvals_apply_exactly_once()
    {
        await using var setup = NewContext();
        Fixture? f = null;
        try
        {
            f = await CreateWorkspaceAsync(setup, Guid.NewGuid().ToString("N")[..12]);
            var inserted = await new PackagePaymentRepository(setup)
                .TryInsertAsync(NewPayment(f.Workspace.Id, "req-race", f.Now), Audit("SP0000.M01"));
            var branchEnd = (await new WorkspacePlanReader(LegacyOptions(), setup).ReadAsync(Shard, f.BranchId)).Branch!.EndsAtUtc;
            var baseAt = PackageRenewal.CeilToThaiMidnight(branchEnd!.Value);

            async Task<ApprovePaymentOutcome> Approve(string actor, int months)
            {
                await using var db = NewContext();
                var now = f.Now.AddMinutes(months);
                var end = PackageRenewal.AddMonthsThai(baseAt, months);
                return await NewAdmin(db).ApprovePaymentAsync(
                    new WorkspacePackageChange(f.Workspace.Id, f.BranchId, true, end, WorkspacePlanKind.Paid,
                        WorkspaceStatus.Active, true, actor, PackagePaymentAuditActions.Approve, null, "{}", now),
                    new PackagePaymentApproval(inserted.Payment!.Id, branchEnd, actor, now, null, branchEnd, baseAt, end));
            }

            var outcomes = await Task.WhenAll(Approve("spadmin:a", 1), Approve("spadmin:b", 2));

            outcomes.Should().BeEquivalentTo([ApprovePaymentOutcome.Applied, ApprovePaymentOutcome.PaymentNotPending]);
            var winner = outcomes[0] == ApprovePaymentOutcome.Applied ? 1 : 2;
            await using var sql = new SqlConnection(Connection);
            (await sql.QuerySingleAsync<DateTime>("SELECT EndPackageDate FROM Branch WHERE Id = @Id", new { Id = f.BranchId }))
                .Should().Be(BranchPackageDates.ToBranchLocal(PackageRenewal.AddMonthsThai(baseAt, winner)), "ต่อครั้งเดียว ไม่ซ้อน");
            (await sql.QuerySingleAsync<int>(
                    "SELECT COUNT(*) FROM sp_WorkspaceAudit WHERE WorkspaceId = @Id AND Action = @Action",
                    new { Id = f.Workspace.Id, Action = PackagePaymentAuditActions.Approve }))
                .Should().Be(1);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    [SelfServeShardSqlFact]
    public async Task End_date_changed_by_someone_else_rolls_everything_back()
    {
        await using var db = NewContext();
        Fixture? f = null;
        try
        {
            f = await CreateWorkspaceAsync(db, Guid.NewGuid().ToString("N")[..12]);
            var inserted = await new PackagePaymentRepository(db)
                .TryInsertAsync(NewPayment(f.Workspace.Id, "req-conflict", f.Now), Audit("SP0000.M01"));
            var branchEnd = (await new WorkspacePlanReader(LegacyOptions(), db).ReadAsync(Shard, f.BranchId)).Branch!.EndsAtUtc!.Value;
            var newEnd = PackageRenewal.AddMonthsThai(PackageRenewal.CeilToThaiMidnight(branchEnd), 1);

            var outcome = await NewAdmin(db).ApprovePaymentAsync(
                new WorkspacePackageChange(f.Workspace.Id, f.BranchId, true, newEnd, WorkspacePlanKind.Paid,
                    WorkspaceStatus.Active, true, "spadmin:x", PackagePaymentAuditActions.Approve, null, "{}", f.Now),
                new PackagePaymentApproval(inserted.Payment!.Id, branchEnd.AddDays(1), "spadmin:x", f.Now, null, branchEnd,
                    branchEnd, newEnd));

            outcome.Should().Be(ApprovePaymentOutcome.BranchEndChanged);
            db.ChangeTracker.Clear();
            (await db.PackagePayments.AsNoTracking().SingleAsync(p => p.Id == inserted.Payment.Id)).Status
                .Should().Be(PackagePaymentStatus.Pending, "rollback ทั้ง transaction");
            var workspace = await db.Workspaces.AsNoTracking().SingleAsync(w => w.Id == f.Workspace.Id);
            workspace.Plan.Should().Be(WorkspacePlanKind.Trial);
            workspace.PurgeAfter.Should().NotBeNull();
            await using var sql = new SqlConnection(Connection);
            (await sql.QuerySingleAsync<DateTime>("SELECT EndPackageDate FROM Branch WHERE Id = @Id", new { Id = f.BranchId }))
                .Should().Be(BranchPackageDates.ToBranchLocal(branchEnd));
            (await sql.QuerySingleAsync<int>("SELECT COUNT(*) FROM sp_WorkspaceAudit WHERE WorkspaceId = @Id AND Action = @A",
                new { Id = f.Workspace.Id, A = PackagePaymentAuditActions.Approve })).Should().Be(0);
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    [SelfServeShardSqlFact]
    public async Task Short_id_prefix_lookup_and_slip_hash_lookup_work_on_sql_server()
    {
        await using var db = NewContext();
        Fixture? f = null;
        try
        {
            f = await CreateWorkspaceAsync(db, Guid.NewGuid().ToString("N")[..12]);
            var repo = new PackagePaymentRepository(db);
            var id = Guid.NewGuid();
            var sha = Convert.ToHexString(Guid.NewGuid().ToByteArray()).ToLowerInvariant().PadRight(64, 'a');
            await repo.TryInsertAsync(NewPayment(f.Workspace.Id, "req-ref", f.Now, id, sha), Audit("SP0000.M01"));

            (await repo.FindByReferenceAsync(id.ToString("N")[..8], 10)).Should().ContainSingle()
                .Which.Payment.Id.Should().Be(id);
            (await repo.FindByReferenceAsync(id.ToString("N")[..8].ToUpperInvariant(), 10)).Should().ContainSingle();
            (await repo.FindByReferenceAsync(id.ToString("D")[..13], 10)).Should().ContainSingle("prefix ข้ามตำแหน่งขีด");
            (await repo.FindByReferenceAsync(id.ToString(), 10)).Single().Workspace.WorkspaceNo.Should().Be(f.Workspace.WorkspaceNo);
            (await repo.FindByReferenceAsync("ffffffff" + id.ToString("N")[8..20], 10)).Should().BeEmpty();

            (await repo.FindBySlipHashesAsync([sha])).Should().ContainSingle().Which.Payment.Id.Should().Be(id);
            (await repo.ListAsync(PackagePaymentStatus.Pending, f.Workspace.WorkspaceNo, 10)).Should().ContainSingle();
        }
        finally
        {
            await CleanupAsync(f);
        }
    }

    [SelfServeShardSqlFact]
    public async Task Purge_data_sees_pending_payments_and_slip_paths_and_the_tombstone_cancels_them()
    {
        await using var db = NewContext();
        Fixture? f = null;
        try
        {
            f = await CreateWorkspaceAsync(db, Guid.NewGuid().ToString("N")[..12]);
            var repo = new PackagePaymentRepository(db);
            var old = await repo.TryInsertAsync(NewPayment(f.Workspace.Id, "req-old", f.Now), Audit("SP0000.M01"));
            await repo.TryRejectAsync(old.Payment!.Id, f.Workspace.Id, PackagePaymentRejectReason.Other, "x", f.Now, Audit("t"));
            var pending = await repo.TryInsertAsync(NewPayment(f.Workspace.Id, "req-new", f.Now), Audit("SP0000.M01"));

            var data = new WorkspacePurgeData(db, new ConfigurationBuilder().Build(), LegacyOptions(), SelfServeOptionsFor());
            var target = await data.FindAsync(f.Workspace.Id);
            target!.HasPendingPayment.Should().BeTrue();
            (await data.ReadPackageSlipPathsAsync(f.Workspace.Id)).Should().HaveCount(2);

            var lifecycle = new SelfServeLifecycleRepository(db, null!, LegacyOptions(), SelfServeOptionsFor());
            (await lifecycle.ListForLifecycleAsync()).Single(w => w.Workspace.Id == f.Workspace.Id)
                .HasPendingPayment.Should().BeTrue();

            await data.MarkPurgedAsync(target, "spadmin:purge-test", f.Now.AddDays(1), "{}");

            db.ChangeTracker.Clear();
            var rows = await db.PackagePayments.AsNoTracking().Where(p => p.WorkspaceId == f.Workspace.Id).ToListAsync();
            rows.Single(p => p.Id == pending.Payment!.Id).Status.Should().Be(PackagePaymentStatus.Cancelled);
            rows.Single(p => p.Id == pending.Payment!.Id).CancelledBy.Should().Be("spadmin:purge-test");
            rows.Single(p => p.Id == old.Payment.Id).Status.Should().Be(PackagePaymentStatus.Rejected);
            rows.Should().OnlyContain(p => p.Note == null, "หมายเหตุของลูกค้าถูกล้าง");
            rows.Should().OnlyContain(p => p.Amount == 1770m, "คอลัมน์การเงินคงไว้");
            (await data.FindAsync(f.Workspace.Id))!.HasPendingPayment.Should().BeFalse();
        }
        finally
        {
            await CleanupAsync(f);
        }
    }
}
