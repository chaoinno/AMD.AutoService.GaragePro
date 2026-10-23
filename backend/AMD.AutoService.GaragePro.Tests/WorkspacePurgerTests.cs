using System.Text.RegularExpressions;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using FluentFTP.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ลบข้อมูลอู่ทดลองใช้ — สิ่งที่ห้ามพลาด: (1) ไม่มีทางลบนอก shard/ฐาน self-serve (2) ลำดับ svc_* → ไฟล์ → shadow → tombstone
/// (3) แผนการลบครอบคลุมทุกตารางใน ServiceDbContext และเรียงลูกก่อนแม่ตาม FK จริงของโมเดล (4) NormalizedEmail ไม่ถูกล้าง
/// </summary>
public class WorkspacePurgerTests
{
    private static readonly DateTime Now = new(2027, 1, 29, 3, 0, 0, DateTimeKind.Utc);
    private const int BranchId = 61;

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(Now);
    }

    private sealed class FakeData(List<string> log) : IWorkspacePurgeData
    {
        public string? Mismatch { get; set; }
        public PurgeTarget? Target { get; set; } = new(Guid.NewGuid(), 1042, BranchId, WorkspacePlanKind.Trial,
            WorkspaceStatus.Expired, Guid.NewGuid());

        public List<string> AttachmentPaths { get; } = [$"sp1/{BranchId}/job1/intake/a.jpg", $"sp1/{BranchId}/job1/chat/b.png"];
        public ServicePurgeScope? ServiceScope { get; private set; }
        public ICurrentUser? ScopeUser { get; private set; }
        public (PurgeTarget Target, string Actor, string Payload)? Marked { get; private set; }
        public List<string> Audits { get; } = [];

        public string? CheckTargetDatabase(string shardKey)
        {
            log.Add("check:" + shardKey);
            return Mismatch;
        }

        public Task<PurgeTarget?> FindAsync(Guid workspaceId, CancellationToken ct = default)
        {
            log.Add("find");
            return Task.FromResult(Target);
        }

        public Task<IReadOnlyList<long>> ReadBranchUserIdsAsync(string shardKey, int branchId, CancellationToken ct = default)
        {
            log.Add("users");
            return Task.FromResult<IReadOnlyList<long>>([11, 12]);
        }

        public Task<IReadOnlyList<string>> ReadAttachmentPathsAsync(IServiceProvider scope, ServicePurgeScope purgeScope,
            CancellationToken ct = default)
        {
            log.Add("paths");
            return Task.FromResult<IReadOnlyList<string>>(AttachmentPaths);
        }

        public List<string> SlipPaths { get; } = [];

        public Task<IReadOnlyList<string>> ReadPackageSlipPathsAsync(Guid workspaceId, CancellationToken ct = default)
        {
            log.Add("slips");
            return Task.FromResult<IReadOnlyList<string>>(SlipPaths);
        }

        public Task<ServicePurgeResult> PurgeServiceDataAsync(IServiceProvider scope, ServicePurgeScope purgeScope,
            CancellationToken ct = default)
        {
            log.Add("svc");
            ServiceScope = purgeScope;
            ScopeUser = (ICurrentUser?)scope.GetService(typeof(ICurrentUser));
            return Task.FromResult(new ServicePurgeResult(AttachmentPaths,
                new Dictionary<string, int> { ["svc_Job"] = 2, ["svc_Attachment"] = 2 }));
        }

        public Task<ShadowPurgeResult> PurgeShadowDataAsync(string shardKey, int branchId, CancellationToken ct = default)
        {
            log.Add("shadow");
            return Task.FromResult(new ShadowPurgeResult(
                new Dictionary<string, int> { ["dbo.Branch"] = 1, ["dbo.Staff"] = 3 }, ["2026/10/5_1.jpg"], ["Car/9.jpg"]));
        }

        public Task MarkPurgedAsync(PurgeTarget target, string actor, DateTime now, string payloadJson,
            CancellationToken ct = default)
        {
            log.Add("mark");
            Marked = (target, actor, payloadJson);
            return Task.CompletedTask;
        }

        public Task AddAuditAsync(Guid workspaceId, string action, string actor, DateTime now, string payloadJson,
            CancellationToken ct = default)
        {
            log.Add("audit:" + action);
            Audits.Add(action);
            return Task.CompletedTask;
        }
    }

    /// <summary>ตั้ง user ของ scope แบบเดียวกับ ShardScopeFactory จริง (provider คืน ICurrentUser ที่ส่งมา)</summary>
    private sealed class FakeScopes(List<string> log) : IShardScopeFactory
    {
        public List<ICurrentUser> Users { get; } = [];

        public async Task<T> RunAsAsync<T>(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task<T>> work,
            CancellationToken ct = default)
        {
            log.Add("scope:" + user.ShardKey + "/" + user.BranchId);
            Users.Add(user);
            return await work(new UserProvider(user), ct);
        }

        public Task RunAsAsync(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task> work,
            CancellationToken ct = default) => throw new NotSupportedException();

        private sealed class UserProvider(ICurrentUser user) : IServiceProvider
        {
            public object? GetService(Type serviceType) => serviceType == typeof(ICurrentUser) ? user : null;
        }
    }

    private sealed class FakeAttachments(List<string> log) : IAttachmentStorage
    {
        public HashSet<string> Missing { get; } = [];
        public HashSet<string> Broken { get; } = [];
        public HashSet<string> Crash { get; } = [];

        public AttachmentValidation Validate(string contentType, long sizeBytes) => new(true, null, null);

        public Task<StoredFile> SaveAsync(Stream content, string shardKey, int branchId, Guid jobId, string kind,
            string fileName, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        {
            log.Add("file:" + relativePath);
            if (Missing.Contains(relativePath)) throw new FtpCommandException("550", "File not found");
            if (Broken.Contains(relativePath)) throw new IOException("ftp down");
            if (Crash.Contains(relativePath)) throw new OperationCanceledException("process stopped");
            return Task.CompletedTask;
        }
    }

    private sealed class FakeImages(List<string> log) : IStaffImageStorage, IVehicleImageStorage
    {
        public StaffImageValidation Validate(StaffImageUpload upload) => throw new NotSupportedException();
        public VehicleImageValidation Validate(VehicleImageUpload upload) => throw new NotSupportedException();
        public Task<string> SaveAsync(long staffId, StaffImageUpload upload, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<string> SaveAsync(long vehicleId, VehicleImageUpload upload, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public bool TryResolve(string relativePath, out string fullPath)
        {
            fullPath = "";
            return false;
        }

        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
        {
            log.Add("image:" + relativePath);
            return Task.CompletedTask;
        }

        public string GetContentType(string relativePath) => "image/jpeg";
    }

    private sealed record Harness(WorkspacePurger Purger, FakeData Data, FakeScopes Scopes, FakeAttachments Files,
        List<string> Log);

    private static Harness Create()
    {
        var log = new List<string>();
        var data = new FakeData(log);
        var scopes = new FakeScopes(log);
        var files = new FakeAttachments(log);
        var images = new FakeImages(log);
        var purger = new WorkspacePurger(data, scopes, files, images, images, Options.Create(new SelfServeOptions()),
            new FixedClock(), NullLogger<WorkspacePurger>.Instance);
        return new Harness(purger, data, scopes, files, log);
    }

    [Fact]
    public async Task Stopping_while_deleting_files_leaves_the_svc_rows_so_the_next_run_still_knows_the_paths()
    {
        // ลบแถวก่อนไฟล์ = process ตายกลางทางแล้ว path หายไปพร้อมแถว ไฟล์ค้างบน FTP ตลอดไป (พบตอน E2E)
        var h = Create();
        h.Files.Crash.Add($"sp1/{BranchId}/job1/chat/b.png");

        var act = () => h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target!.WorkspaceId, "system:lifecycle"));

        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Log.Should().Contain($"file:sp1/{BranchId}/job1/intake/a.jpg");
        h.Log.Should().NotContain("svc").And.NotContain("shadow").And.NotContain("mark");
    }

    [Fact]
    public async Task Purge_deletes_files_before_svc_rows_then_shadow_then_tombstone()
    {
        var h = Create();

        var result = await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target!.WorkspaceId, "system:lifecycle"));

        result.Success.Should().BeTrue();
        h.Log.Should().Equal(
            "check:sp1", "find", "users", $"scope:sp1/{BranchId}", "paths",
            $"file:sp1/{BranchId}/job1/intake/a.jpg", $"file:sp1/{BranchId}/job1/chat/b.png",
            $"scope:sp1/{BranchId}", "svc", "slips",
            "shadow", "image:2026/10/5_1.jpg", "image:Car/9.jpg", "mark");
        h.Data.ServiceScope.Should().BeEquivalentTo(new ServicePurgeScope("sp1", BranchId, [11, 12]));
        h.Data.ScopeUser!.ShardKey.Should().Be("sp1", "ServiceDbContext ของ scope ต้องชี้ GarageSelfServe");
        h.Data.ScopeUser.BranchId.Should().Be(BranchId);
        h.Data.ScopeUser.IsAdministrator.Should().BeFalse();

        var report = result.Data!;
        report.AlreadyPurged.Should().BeFalse();
        report.FilesDeleted.Should().Be(2);
        report.DeletedRows.Should().Contain("svc_Job", 2).And.Contain("dbo.Branch", 1);
        h.Data.Marked!.Value.Actor.Should().Be("system:lifecycle");
        h.Data.Marked.Value.Payload.Should().Contain("\"filesDeleted\":2");
    }

    [Fact]
    public async Task Package_slip_files_are_deleted_before_the_tombstone_and_through_the_branch_prefix_guard()
    {
        var h = Create();
        h.Data.SlipPaths.Add($"sp1/{BranchId}/package-slip/aa/bb.jpg");
        h.Data.SlipPaths.Add("sp1/999/package-slip/cc/dd.jpg");   // อ้างสาขาอื่น · ห้ามลบ

        var result = await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target!.WorkspaceId, "system:lifecycle"));

        result.Success.Should().BeTrue();
        h.Log.IndexOf($"file:sp1/{BranchId}/package-slip/aa/bb.jpg").Should().BeLessThan(h.Log.IndexOf("mark"));
        h.Log.Should().NotContain("file:sp1/999/package-slip/cc/dd.jpg");
        result.Data!.FilesFailed.Should().Contain("sp1/999/package-slip/cc/dd.jpg");
    }

    [Fact]
    public async Task Workspace_with_a_pending_payment_is_not_purged_without_confirmation()
    {
        var h = Create();
        h.Data.Target = h.Data.Target! with { HasPendingPayment = true };

        var refused = await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target.WorkspaceId, "system:lifecycle"));
        refused.Success.Should().BeFalse();
        refused.Error!.Code.Should().Be(SelfServeLifecycleErrors.PurgeRefusedCode);
        refused.Error.MessageTh.Should().Contain("แจ้งชำระรอตรวจ");
        h.Log.Should().NotContain("svc").And.NotContain("mark");

        var allowed = await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target.WorkspaceId, "spadmin:noi",
            AllowPaid: true));
        allowed.Success.Should().BeTrue("ทีมงานยืนยัน --allow-paid แล้ว");
        h.Log.Should().Contain("mark");
    }

    [Theory]
    [InlineData("db2")]
    [InlineData("db1")]
    [InlineData("maindb")]
    public async Task Refuses_any_shard_other_than_the_self_serve_shard(string shard)
    {
        var h = Create();

        var result = await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target!.WorkspaceId, "spadmin", shard));

        result.Error!.Code.Should().Be(SelfServeLifecycleErrors.PurgeRefusedCode);
        h.Log.Should().BeEmpty("ต้องปฏิเสธก่อนแตะฐานข้อมูลหรือไฟล์ใดๆ");
    }

    [Fact]
    public async Task Refuses_when_the_connection_settings_do_not_point_at_GarageSelfServe()
    {
        var h = Create();
        h.Data.Mismatch = "LegacyShards:ConnectionStrings ของ shard self-serve ชี้คนละฐานกับ SelfServe:ConnectionString";

        var result = await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target!.WorkspaceId, "system"));

        result.Error!.Code.Should().Be(SelfServeLifecycleErrors.PurgeRefusedCode);
        result.Error.MessageTh.Should().Contain("ชี้คนละฐาน");
        h.Log.Should().Equal("check:sp1");
    }

    [Fact]
    public async Task Paid_workspace_needs_an_explicit_confirmation()
    {
        var h = Create();
        h.Data.Target = h.Data.Target! with { Plan = WorkspacePlanKind.Paid };

        (await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target.WorkspaceId, "system")))
            .Error!.Code.Should().Be(SelfServeLifecycleErrors.PurgeRefusedCode);
        h.Log.Should().NotContain("svc");

        (await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target.WorkspaceId, "spadmin", AllowPaid: true)))
            .Success.Should().BeTrue();
    }

    [Fact]
    public async Task Converted_workspace_needs_an_explicit_confirmation()
    {
        // S5: ข้อมูลหลักย้ายไปสาขาจริงแล้ว = ลูกค้าที่จ่ายเงิน — ลบได้เฉพาะเมื่อยืนยัน (spadmin --allow-paid / job หลัง PurgeAfter)
        var h = Create();
        h.Data.Target = h.Data.Target! with { Plan = WorkspacePlanKind.Converted };

        var refused = await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target.WorkspaceId, "system"));
        refused.Error!.Code.Should().Be(SelfServeLifecycleErrors.PurgeRefusedCode);
        refused.Error.MessageTh.Should().Contain("converted");
        h.Log.Should().NotContain("svc");

        (await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target.WorkspaceId, "system", AllowPaid: true)))
            .Success.Should().BeTrue();
    }

    [Fact]
    public async Task Already_purged_workspace_is_a_no_op()
    {
        var h = Create();
        h.Data.Target = h.Data.Target! with { Status = WorkspaceStatus.Purged };

        var result = await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target.WorkspaceId, "system"));

        result.Data!.AlreadyPurged.Should().BeTrue();
        h.Log.Should().Equal("check:sp1", "find");
    }

    [Fact]
    public async Task Unknown_workspace_is_not_found()
    {
        var h = Create();
        h.Data.Target = null;

        (await h.Purger.PurgeAsync(new WorkspacePurgeRequest(Guid.NewGuid(), "system")))
            .Error!.Code.Should().Be(SelfServeLifecycleErrors.WorkspaceNotFoundCode);
    }

    [Fact]
    public async Task Workspace_without_a_branch_is_tombstoned_without_touching_data()
    {
        var h = Create();
        h.Data.Target = h.Data.Target! with { BranchId = null };

        (await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target.WorkspaceId, "system"))).Success.Should().BeTrue();

        h.Log.Should().Equal("check:sp1", "find", "mark");
    }

    [Fact]
    public async Task Missing_files_are_counted_and_failed_files_kept_for_the_team()
    {
        var h = Create();
        var foreign = "sp1/999/other/intake/x.jpg";
        h.Data.AttachmentPaths.Add(foreign);
        h.Files.Missing.Add(h.Data.AttachmentPaths[0]);
        h.Files.Broken.Add(h.Data.AttachmentPaths[1]);

        var report = (await h.Purger.PurgeAsync(new WorkspacePurgeRequest(h.Data.Target!.WorkspaceId, "system"))).Data!;

        report.FilesDeleted.Should().Be(0);
        report.FilesMissing.Should().Be(1);
        report.FilesFailed.Should().BeEquivalentTo(h.Data.AttachmentPaths[1], foreign);
        h.Log.Should().NotContain("file:" + foreign, "ไฟล์นอก prefix ของสาขาห้ามถูกลบ");
        h.Log.Should().Contain("shadow").And.Contain("mark", "ไฟล์ค้างไม่หยุดการลบส่วนที่เหลือ");
        h.Data.Marked!.Value.Payload.Should().Contain("x.jpg");
    }

    [Fact]
    public async Task Abandoned_provisioning_purges_svc_rows_only_and_keeps_the_account()
    {
        var h = Create();
        h.Data.Target = h.Data.Target! with { Status = WorkspaceStatus.Purged };

        var result = await h.Purger.PurgeAbandonedServiceRowsAsync(h.Data.Target.WorkspaceId, "system:lifecycle");

        result.Success.Should().BeTrue();
        h.Log.Should().Contain("svc").And.Contain($"audit:{WorkspacePurger.AbandonedServicePurgedAction}");
        h.Log.Should().NotContain("shadow").And.NotContain("mark");
    }

    [Fact]
    public async Task Abandoned_purge_refuses_workspaces_that_are_still_live()
    {
        var h = Create();

        (await h.Purger.PurgeAbandonedServiceRowsAsync(h.Data.Target!.WorkspaceId, "system"))
            .Error!.Code.Should().Be(SelfServeLifecycleErrors.PurgeRefusedCode);
        h.Log.Should().NotContain("svc");
    }

    [Fact]
    public void Tombstone_clears_personal_data_but_keeps_the_normalized_email()
    {
        var workspace = new Workspace { Name = "อู่ช่างเอก", Phone = "+66812345678", Status = WorkspaceStatus.Expired };
        var account = new SelfServeAccount
        {
            NormalizedEmail = "ab@gmail.com", Email = "A.b+trial@gmail.com", PasswordHash = "$pbkdf2-sha256$...",
            GoogleSubject = "1234", LoginUserName = "SP1042.M01", UserId = 5, SetupTokenHash = "abc",
            Status = SelfServeAccountStatus.Active, CreatedIp = "203.0.113.4", CreatedUserAgent = "Mozilla"
        };

        WorkspaceTombstone.Apply(workspace, account, Now);

        workspace.Status.Should().Be(WorkspaceStatus.Purged);
        workspace.PurgedAt.Should().Be(Now);
        workspace.Name.Should().BeEmpty();
        workspace.Phone.Should().BeNull();

        account.Status.Should().Be(SelfServeAccountStatus.Purged);
        account.NormalizedEmail.Should().Be("ab@gmail.com", "tombstone กันสมัครซ้ำด้วยอีเมลเดิม");
        account.Email.Should().BeNull();
        account.PasswordHash.Should().BeNull();
        account.GoogleSubject.Should().BeNull();
        account.LoginUserName.Should().BeNull();
        account.UserId.Should().BeNull();
        account.SetupTokenHash.Should().BeNull();
        account.CreatedIp.Should().BeNull();
        account.CreatedUserAgent.Should().BeNull();
    }

    // ---------- แผนการลบตรวจกับโมเดล/DDL จริง ----------

    private static ServiceDbContext ModelOnlyContext() => new(new DbContextOptionsBuilder<ServiceDbContext>()
        .UseSqlServer("Server=model-only;Database=GarageSelfServe_Model;Integrated Security=false").Options);

    [Fact]
    public void Purge_plan_covers_every_table_of_ServiceDbContext_exactly_once()
    {
        using var db = ModelOnlyContext();
        var modelTypes = db.Model.GetEntityTypes().Where(e => !e.IsOwned()).Select(e => e.ClrType).ToList();
        var planned = ServicePurgePlan.Steps.Select(s => s.Entity).ToList();

        planned.Should().OnlyHaveUniqueItems();
        planned.Should().BeEquivalentTo(modelTypes,
            "ตารางใหม่ใน ServiceDbContext ต้องเพิ่มใน ServicePurgePlan ด้วย ไม่งั้นข้อมูลของอู่ที่ลบแล้วจะค้างอยู่เงียบๆ");

        foreach (var step in ServicePurgePlan.Steps)
            step.Table.Should().Be(db.Model.FindEntityType(step.Entity)!.GetTableName(), "ชื่อในรายงานต้องตรงกับตารางจริง");
    }

    [Fact]
    public void Purge_plan_deletes_children_before_parents_for_every_foreign_key_in_the_model()
    {
        using var db = ModelOnlyContext();
        var order = ServicePurgePlan.Steps.Select((s, i) => (s.Entity, i)).ToDictionary(x => x.Entity, x => x.i);

        foreach (var entity in db.Model.GetEntityTypes())
        foreach (var fk in entity.GetForeignKeys())
        {
            var child = fk.DeclaringEntityType.ClrType;
            var parent = fk.PrincipalEntityType.ClrType;
            if (child == parent) continue;   // self-reference ตัดลิงก์ก่อนลบในขั้นเดียวกัน
            order[child].Should().BeLessThan(order[parent],
                $"{child.Name} อ้าง {parent.Name} ผ่าน FK จึงต้องถูกลบก่อน");
        }
    }

    [Fact]
    public void Shadow_deletes_follow_the_foreign_keys_of_the_generated_shadow_ddl()
    {
        var ddl = File.ReadAllText(FindRepoFile("deploy/selfserve/02-shadow-ddl.generated.sql"));
        var foreignKeys = Regex.Matches(ddl,
                @"ALTER TABLE \[dbo\]\.\[(\w+)\] WITH CHECK ADD CONSTRAINT \[\w+\] FOREIGN KEY \(\[\w+\]\) REFERENCES \[dbo\]\.\[(\w+)\]")
            .Select(m => (Child: m.Groups[1].Value, Parent: m.Groups[2].Value))
            .ToList();
        foreignKeys.Should().NotBeEmpty();

        var deleteOrder = WorkspacePurgeData.ShadowDeletes
            .Select((d, i) => (d.Table, i))
            .Where(d => !d.Table.Contains(':'))
            .ToDictionary(d => d.Table["dbo.".Length..], d => d.i);

        foreach (var (child, parent) in foreignKeys.Where(f => deleteOrder.ContainsKey(f.Child) && deleteOrder.ContainsKey(f.Parent)))
            deleteOrder[child].Should().BeLessThan(deleteOrder[parent], $"{child} อ้าง {parent} จึงต้องถูกลบก่อน");

        deleteOrder.Keys.Should().Contain(["Branch", "Staff", "User", "Customer", "Car", "CarCustomer", "ChangeLog",
            "PJCarPickUp", "StaffSectorPosition", "StaffRepairTypeNPoint"]);
    }

    [Fact]
    public void Shadow_sql_is_scoped_to_one_branch()
    {
        foreach (var (table, sql) in WorkspacePurgeData.ShadowDeletes)
            sql.Should().MatchRegex("#Purge|@BranchId", $"{table} ต้องกรองด้วยชุดของสาขานี้ ไม่ใช่ทั้งตาราง");
        WorkspacePurgeData.CollectShadowRowsSql.Should().Contain("WHERE s.BranchId = @BranchId");
    }

    // ---------- guard ของค่าตั้ง ----------

    private const string SelfServe = "Server=10.10.4.11;Database=GarageSelfServe;User Id=gp_selfserve_app;Password=x";

    [Fact]
    public void Database_guard_accepts_matching_self_serve_settings()
    {
        SelfServeDatabaseGuard.Check(SelfServe,
                "Data Source=10.10.4.11;Initial Catalog=GarageSelfServe;User Id=gp_selfserve_app;Password=x",
                "Server=10.10.4.11;Database=GarageService;User Id=a;Password=b",
                [new("db2", "Server=10.10.4.11;Database=Garage;User Id=a;Password=b")])
            .Should().BeNull();
    }

    [Fact]
    public void Database_guard_rejects_a_shadow_connection_pointing_at_the_legacy_garage_database()
    {
        SelfServeDatabaseGuard.Check(SelfServe, "Server=10.10.4.11;Database=Garage;User Id=a;Password=b", null, [])
            .Should().Contain("ชี้คนละฐาน");
    }

    [Fact]
    public void Database_guard_rejects_a_self_serve_database_shared_with_GarageService_or_another_shard()
    {
        SelfServeDatabaseGuard.Check(SelfServe, SelfServe, SelfServe, []).Should().Contain("GarageService");
        SelfServeDatabaseGuard.Check(SelfServe, SelfServe, null, [new("db2", SelfServe)]).Should().Contain("db2");
    }

    [Fact]
    public void Database_guard_requires_both_connections()
    {
        SelfServeDatabaseGuard.Check(null, SelfServe, null, []).Should().NotBeNull();
        SelfServeDatabaseGuard.Check(SelfServe, null, null, []).Should().NotBeNull();
        SelfServeDatabaseGuard.Check(SelfServe, "Server=10.10.4.11;User Id=a;Password=b", null, [])
            .Should().NotBeNull("ไม่ระบุชื่อฐาน = เดาไม่ได้ว่าจะลบที่ไหน");
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException(relative);
    }
}
