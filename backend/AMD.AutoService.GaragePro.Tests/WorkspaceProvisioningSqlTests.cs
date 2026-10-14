using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using Dapper;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// รัน SQL จริงของ <see cref="WorkspaceRepository"/> และการเปิดอู่ทั้งเส้น (StaffRepository จริง) กับฐาน GarageSelfServe(_Dev)
/// ที่ provision แล้ว (deploy/selfserve) — จับคอลัมน์ NOT NULL ของ Branch, FK ของตาราง shadow (ChangeLog→User, User→Staff),
/// sp_getapplock และ query ของ EF ที่ unit test มองไม่เห็น
/// · [RISK] เขียนข้อมูลจริง: แถวทดสอบทุกแถวมีรหัสสุ่มของตัวเองและถูกลบใน finally (Branch ลบจริง ไม่ใช่แค่ Status=0)
///   ตั้ง <c>GARAGEPRO_SELFSERVE_SQL_CONNECTION</c> เฉพาะฐาน dev เท่านั้น · connection ต้องมีสิทธิ์ INSERT/DELETE ตาราง shadow + sp_*
/// </summary>
public class WorkspaceProvisioningSqlTests
{
    private const string Shard = "sp1";

    private static string Connection => Environment.GetEnvironmentVariable(SelfServeShardSqlFactAttribute.Variable)!;

    private static IOptions<LegacyShardOptions> LegacyOptions() => Options.Create(
        new LegacyShardOptions { ConnectionStrings = { [Shard] = Connection } });

    private static SelfServeDbContext NewContext()
    {
        var builder = new DbContextOptionsBuilder<SelfServeDbContext>();
        SelfServeDbContext.UseSelfServeSqlServer(builder, Connection);
        return new SelfServeDbContext(builder.Options);
    }

    private static WorkspaceRepository NewRepository(SelfServeDbContext db) => new(
        db, LegacyOptions(), new MemoryCache(new MemoryCacheOptions()),
        Options.Create(new SelfServeOptions { ShardKey = Shard }), NullLogger<WorkspaceRepository>.Instance);

    [SelfServeShardSqlFact]
    public async Task Repository_round_trip_against_the_shadow_schema()
    {
        var tag = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        Guid? workspaceId = null;
        int? branchId = null;

        await using var db = NewContext();
        var repository = NewRepository(db);
        try
        {
            db.Accounts.Add(new SelfServeAccount
            {
                Id = accountId, NormalizedEmail = $"s2-sql-{tag}@example.invalid", Email = $"s2-sql-{tag}@example.invalid",
                Status = SelfServeAccountStatus.Verified, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            // ล็อกต่อบัญชี: คนที่สองไม่ได้ (ไม่รอ) จนกว่าคนแรกปล่อย
            var lockKey = $"servicepro:selfserve:provision:{accountId:N}";
            await using (var first = await repository.TryAcquireProvisioningLockAsync(lockKey, TimeSpan.FromSeconds(5)))
            {
                first.Should().NotBeNull();
                await using var otherDb = NewContext();
                (await NewRepository(otherDb).TryAcquireProvisioningLockAsync(lockKey, TimeSpan.Zero)).Should().BeNull();
            }
            await using (var again = await repository.TryAcquireProvisioningLockAsync(lockKey, TimeSpan.Zero))
                again.Should().NotBeNull("ล็อกต้องถูกปล่อยเมื่อ dispose");

            var now = DateTime.SpecifyKind(DateTime.UtcNow.AddTicks(-(DateTime.UtcNow.Ticks % TimeSpan.TicksPerSecond)),
                DateTimeKind.Utc);
            var workspace = await repository.AddWorkspaceAsync(new Workspace
            {
                Id = Guid.NewGuid(), Name = $"S2 SQL {tag}", Phone = "+66900000000", Status = WorkspaceStatus.Provisioning,
                SignupRequestId = $"req-{tag}", OwnerAccountId = accountId, CreatedAt = now.AddHours(-3)
            });
            workspaceId = workspace.Id;
            workspace.WorkspaceNo.Should().BeGreaterThanOrEqualTo(1001, "IDENTITY(1001,1)");
            (await repository.PhoneUsedByAnotherWorkspaceAsync("+66900000000", workspace.Id)).Should().BeFalse();

            branchId = await repository.CreateShadowBranchAsync(Shard, workspace, new ShadowBranchSpec(
                $"S2 SQL {tag}", "0900000000", null, null, "1234", now, 30, now), WorkspaceProvisionSteps.BranchCreated, now);

            workspace.BranchId.Should().Be(branchId);
            workspace.ProvisionStep.Should().Be(WorkspaceProvisionSteps.BranchCreated);
            workspace.TrialEndsAt.Should().Be(now.AddDays(30));

            await using (var sql = new SqlConnection(Connection))
            {
                var row = await sql.QuerySingleAsync<(int Status, int BranchGroupId, DateTime Start, int Days, DateTime End, long CreatrdBy)>(
                    "SELECT Status, BranchGroupId, StartPackageDate, PackageDaysAmout, EndPackageDate, CreatrdBy FROM Branch WHERE Id = @Id",
                    new { Id = branchId });
                row.Should().Be((1, 7, BranchPackageDates.ToBranchLocal(now), 30, BranchPackageDates.ToBranchLocal(now.AddDays(30)), 0L));
            }

            // ผูกซ้ำต้องไม่ได้ (กัน Branch ที่สอง)
            await repository.Invoking(r => r.CreateShadowBranchAsync(Shard, workspace, new ShadowBranchSpec(
                    "dup", null, null, null, "0000", now, 30, now), WorkspaceProvisionSteps.BranchCreated, now))
                .Should().ThrowAsync<InvalidOperationException>();

            (await repository.FindShadowUserAsync(Shard, branchId.Value, "SP0000.M01")).Should().BeNull();
            // ขั้นผูกสาขาเพิ่งแตะ UpdatedAt = now (สร้างไว้ตั้งแต่ 3 ชม.ก่อน) — "ค้าง" นับจากการแตะล่าสุด ไม่ใช่วันสร้าง
            (await repository.ListStuckProvisioningAsync(now.AddHours(-1))).Should().NotContain(w => w.Id == workspace.Id);
            (await repository.ListStuckProvisioningAsync(now.AddMinutes(1))).Should().Contain(w => w.Id == workspace.Id);

            // บันทึก error = UPDATE สองคอลัมน์ ไม่ใช่ SaveChanges ของการแก้ที่ค้าง
            workspace.Name = "must-not-be-saved";
            workspace.UpdatedAt = now;
            await repository.RecordProvisionFailureAsync(workspace, "owner: test", new InvalidOperationException("test"));
            var stored = await db.Workspaces.AsNoTracking().SingleAsync(w => w.Id == workspace.Id);
            (stored.ProvisionError, stored.Name, stored.UpdatedAt).Should().Be(("owner: test", $"S2 SQL {tag}", (DateTime?)now));

            var removed = await repository.RemoveShadowBranchAsync(Shard, branchId.Value, now);
            removed.Should().Be(new ShadowCleanupResult(0, 0));
        }
        finally
        {
            await DeleteTestRowsAsync(accountId, workspaceId, branchId);
        }
    }

    [SelfServeShardSqlFact]
    public async Task Provisions_a_workspace_end_to_end_with_the_real_staff_repository_and_cleans_up()
    {
        var tag = Guid.NewGuid().ToString("N")[..12];
        var accountId = Guid.NewGuid();
        ProvisionResult? provisioned = null;

        await using var db = NewContext();
        var repository = NewRepository(db);
        try
        {
            db.Accounts.Add(new SelfServeAccount
            {
                Id = accountId, NormalizedEmail = $"s2-e2e-{tag}@example.invalid", Email = $"s2-e2e-{tag}@example.invalid",
                Status = SelfServeAccountStatus.Verified, CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();

            // ส่วนที่ต้องมี ServiceDb/AuthService จริง (บทบาท/ข้อมูลหลัก/กะ) ใช้ stub — เทสต์นี้พิสูจน์ SQL ของ shadow + sp_* เท่านั้น
            var (auth, authStub) = InterfaceStub<IAuthRepository>.Create();
            authStub.On(nameof(IAuthRepository.UpsertRoleOverrideAsync), _ => Task.CompletedTask)
                .On(nameof(IAuthRepository.SaveChangesAsync), _ => Task.FromResult(1));
            var (authService, authServiceStub) = InterfaceStub<IAuthService>.Create();
            authServiceStub.On(nameof(IAuthService.GetShiftsAsync), _ =>
                Task.FromResult(Result<IReadOnlyList<ShiftOptionDto>>.Ok(Array.Empty<ShiftOptionDto>())));
            var scopes = new StubScopes(new Dictionary<Type, object>
            {
                [typeof(IAuthRepository)] = auth,
                [typeof(IAuthService)] = authService,
                [typeof(IBranchDefaultsSeeder)] = new NoopSeeder()
            });

            var hasher = new Pbkdf2PasswordHasher();
            var provisioner = new WorkspaceProvisioner(repository, new StaffRepository(LegacyOptions(), imageStorage: null!),
                scopes, hasher, new SelfServeOptions { ShardKey = Shard }, TimeProvider.System);

            var request = new ProvisionRequest(accountId, $"req-{tag}", $"S2 E2E {tag}", "+66900000001", null, null,
                "ทดสอบ", "S2", $"s2-e2e-{tag}@example.invalid", hasher.Hash("s2-e2e-password"), false);
            var result = await provisioner.ProvisionAsync(request);
            provisioned = result.Data;

            result.Success.Should().BeTrue(result.Error?.MessageTh + " / " +
                (await db.Workspaces.AsNoTracking().Where(w => w.OwnerAccountId == accountId)
                    .Select(w => w.ProvisionError).FirstOrDefaultAsync()));
            provisioned!.OwnerUserName.Should().Be(WorkspaceProvisioner.OwnerUserName(provisioned.WorkspaceNo));

            await using (var sql = new SqlConnection(Connection))
            {
                var user = await sql.QuerySingleAsync<(string UserName, bool IsAdministrator, long StaffId)>(
                    "SELECT UserName, ISNULL(IsAdministrator, 0), StaffId FROM [User] WHERE Id = @Id",
                    new { Id = provisioned.OwnerUserId });
                user.Should().Be((provisioned.OwnerUserName, false, provisioned.OwnerStaffId));
            }

            var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
            (account.Status, account.UserId, account.LoginUserName)
                .Should().Be((SelfServeAccountStatus.Active, (long?)provisioned.OwnerUserId, provisioned.OwnerUserName));

            // replay ไม่สร้างอะไรเพิ่ม
            await using var replayDb = NewContext();
            var replay = await new WorkspaceProvisioner(NewRepository(replayDb), new StaffRepository(LegacyOptions(), null!),
                scopes, hasher, new SelfServeOptions { ShardKey = Shard }, TimeProvider.System).ProvisionAsync(request);
            replay.Data.Should().Be(provisioned);
        }
        finally
        {
            var workspaceId = provisioned?.WorkspaceId
                              ?? await db.Workspaces.AsNoTracking().Where(w => w.OwnerAccountId == accountId)
                                  .Select(w => (Guid?)w.Id).FirstOrDefaultAsync();
            var branchId = provisioned?.BranchId
                           ?? await db.Workspaces.AsNoTracking().Where(w => w.OwnerAccountId == accountId)
                               .Select(w => w.BranchId).FirstOrDefaultAsync();
            if (branchId is int b)
                await repository.RemoveShadowBranchAsync(Shard, b, DateTime.UtcNow);
            await DeleteTestRowsAsync(accountId, workspaceId, branchId);
        }
    }

    private static async Task DeleteTestRowsAsync(Guid accountId, Guid? workspaceId, int? branchId)
    {
        await using var sql = new SqlConnection(Connection);
        await sql.ExecuteAsync("""
            DELETE FROM sp_WorkspaceAudit WHERE WorkspaceId = @WorkspaceId;
            DELETE FROM sp_Workspace WHERE Id = @WorkspaceId OR OwnerAccountId = @AccountId;
            DELETE FROM sp_Account WHERE Id = @AccountId;
            DELETE FROM Branch WHERE Id = @BranchId AND NOT EXISTS (SELECT 1 FROM Staff WHERE BranchId = @BranchId);
            """, new { WorkspaceId = workspaceId, AccountId = accountId, BranchId = branchId });
    }

    private sealed class NoopSeeder : IBranchDefaultsSeeder
    {
        public Task EnsureAsync(string shardKey, int branchId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StubScopes(IReadOnlyDictionary<Type, object> services) : IShardScopeFactory
    {
        public Task<T> RunAsAsync<T>(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task<T>> work,
            CancellationToken ct = default) => work(new Provider(services), ct);

        public async Task RunAsAsync(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task> work,
            CancellationToken ct = default) => await work(new Provider(services), ct);

        private sealed class Provider(IReadOnlyDictionary<Type, object> map) : IServiceProvider
        {
            public object? GetService(Type serviceType) => map.GetValueOrDefault(serviceType);
        }
    }
}
