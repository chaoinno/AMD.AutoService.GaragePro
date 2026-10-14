using System.Data;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// sp_* ผ่าน <see cref="SelfServeDbContext"/> · ตาราง "รูปร่างเหมือน Garage" ผ่าน Dapper ที่ <c>LegacyShards:ConnectionStrings[shard]</c>
/// (เปิด connection แบบเดียวกับ LegacyReader/StaffRepository)
/// · [RISK] <see cref="CreateShadowBranchAsync"/> เขียน Branch และ sp_Workspace ใน transaction เดียวบน connection ของ shard —
///   ได้เพราะทั้งสองอยู่ในฐาน GarageSelfServe เดียวกัน (คีย์ LegacyShards:ConnectionStrings:sp1 = SelfServe:ConnectionString
///   ตาม deploy/selfserve/README.md) · ถ้าวันหนึ่งแยกฐาน ต้องเปลี่ยนเป็นขั้นตอนชดเชยแทน
/// · ไม่มี BeginTransactionAsync บน EF (EnableRetryOnFailure) — ทุกการเขียน sp_* เป็น SaveChanges/ExecuteUpdate ครั้งเดียว
/// · partial: S3 เพิ่มเมธอดข้อมูลตัวอย่างในไฟล์ของตัวเองได้
/// </summary>
public sealed partial class WorkspaceRepository(
    SelfServeDbContext db,
    IOptions<LegacyShardOptions> legacyOptions,
    IMemoryCache cache,
    IOptions<SelfServeOptions> options,
    ILogger<WorkspaceRepository> logger) : IWorkspaceRepository
{
    private readonly LegacyShardOptions _legacy = legacyOptions.Value;
    private readonly SelfServeOptions _options = options.Value;

    // ---------- sp_* ----------

    public Task<SelfServeAccount?> GetAccountAsync(Guid accountId, CancellationToken ct = default) =>
        db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);

    public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken ct = default) =>
        db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct);

    public Task<Workspace?> FindBySignupRequestAsync(Guid ownerAccountId, string signupRequestId,
        CancellationToken ct = default) =>
        db.Workspaces.FirstOrDefaultAsync(
            w => w.OwnerAccountId == ownerAccountId && w.SignupRequestId == signupRequestId, ct);

    public async Task<IReadOnlyList<Workspace>> ListByOwnerAsync(Guid ownerAccountId, CancellationToken ct = default) =>
        await db.Workspaces.Where(w => w.OwnerAccountId == ownerAccountId).OrderBy(w => w.CreatedAt).ToListAsync(ct);

    public Task<bool> PhoneUsedByAnotherWorkspaceAsync(string phoneE164, Guid? exceptWorkspaceId,
        CancellationToken ct = default) =>
        db.Workspaces.AnyAsync(w => w.Phone == phoneE164 && (exceptWorkspaceId == null || w.Id != exceptWorkspaceId), ct);

    public async Task<Workspace> AddWorkspaceAsync(Workspace workspace, CancellationToken ct = default)
    {
        db.Workspaces.Add(workspace);
        try
        {
            await db.SaveChangesAsync(ct);
            return workspace;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }
                                           && workspace.OwnerAccountId is Guid owner
                                           && workspace.SignupRequestId is { } requestId)
        {
            // คำขอเดิมถูกสร้างไปแล้ว (instance อื่น/ล็อกหลุด) — ใช้แถวนั้นแทน
            db.Entry(workspace).State = EntityState.Detached;
            var existing = await FindBySignupRequestAsync(owner, requestId, ct);
            if (existing is null)
                throw;
            return existing;
        }
    }

    public async Task ReloadAsync(Workspace workspace, CancellationToken ct = default)
    {
        var entry = db.Entry(workspace);
        if (entry.State == EntityState.Detached)
            db.Workspaces.Attach(workspace);
        await db.Entry(workspace).ReloadAsync(ct);
    }

    public async Task<IReadOnlyList<Workspace>> ListStuckProvisioningAsync(DateTime untouchedSinceUtc,
        CancellationToken ct = default) =>
        await StuckProvisioningQuery(db, untouchedSinceUtc).ToListAsync(ct);

    /// <summary>
    /// "ไม่ถูกแตะตั้งแต่" = UpdatedAt (ถ้ามี) ไม่งั้น CreatedAt — เขียนเป็นสองเงื่อนไขแทน <c>??</c> เพื่อให้เทียบคอลัมน์กับพารามิเตอร์ตรงๆ
    /// (สองคอลัมน์มี value converter คนละตัว) · แยกออกมาให้เทสต์ตรวจ SQL ที่ได้ด้วย ToQueryString โดยไม่ต้องมีฐาน
    /// </summary>
    internal static IQueryable<Workspace> StuckProvisioningQuery(SelfServeDbContext db, DateTime untouchedSinceUtc) =>
        db.Workspaces
            .Where(w => w.Status == WorkspaceStatus.Provisioning
                        && ((w.UpdatedAt == null && w.CreatedAt < untouchedSinceUtc) || w.UpdatedAt < untouchedSinceUtc))
            .OrderBy(w => w.CreatedAt);

    public void AddAudit(WorkspaceAudit audit) => db.WorkspaceAudits.Add(audit);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    public async Task RecordProvisionFailureAsync(Workspace workspace, string error, Exception exception,
        CancellationToken ct = default)
    {
        logger.LogError(exception,
            "Self-serve provisioning failed for workspace {WorkspaceId} (SP{WorkspaceNo}) at step {Step}: {Error}",
            workspace.Id, workspace.WorkspaceNo, workspace.ProvisionStep, error);

        var message = error.Length > 2000 ? error[..2000] : error;
        var updatedAt = workspace.UpdatedAt ?? DateTime.UtcNow;
        workspace.ProvisionError = message;

        try
        {
            // ทิ้งทุกการแก้ไขที่ค้าง (ขั้นที่ล้มอาจแก้ entity ไปครึ่งทาง) แล้วเขียนเฉพาะ error — ไม่ใช่ SaveChanges
            db.ChangeTracker.Clear();
            await db.Workspaces.Where(w => w.Id == workspace.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(w => w.ProvisionError, message)
                    .SetProperty(w => w.UpdatedAt, updatedAt), ct);
        }
        catch (Exception recordError)
        {
            logger.LogError(recordError, "Could not record the provisioning error of workspace {WorkspaceId}", workspace.Id);
        }
    }

    public void InvalidatePlanCache(int branchId) => WorkspacePolicy.Invalidate(cache, _options, branchId);

    public async Task<IAsyncDisposable?> TryAcquireProvisioningLockAsync(string lockKey, TimeSpan timeout,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);

        // ล็อกอยู่กับ session ของ connection เฉพาะตัวนี้ (ไม่ใช่ connection ของ DbContext ที่ EF เปิด-ปิดเอง) จนกว่าจะ dispose
        var connection = new SqlConnection(db.Database.GetConnectionString());
        try
        {
            await connection.OpenAsync(ct);
            var parameters = new DynamicParameters();
            parameters.Add("Resource", lockKey);
            parameters.Add("LockMode", "Exclusive");
            parameters.Add("LockOwner", "Session");
            parameters.Add("LockTimeout", (int)Math.Max(0, timeout.TotalMilliseconds));
            parameters.Add("Result", dbType: DbType.Int32, direction: ParameterDirection.ReturnValue);

            await connection.ExecuteAsync(new CommandDefinition("sp_getapplock", parameters,
                commandType: CommandType.StoredProcedure,
                commandTimeout: (int)Math.Ceiling(timeout.TotalSeconds) + 15,
                cancellationToken: ct));

            // 0 = ได้ทันที · 1 = ได้หลังรอ · < 0 = หมดเวลา/ถูกยกเลิก/deadlock
            if (parameters.Get<int>("Result") >= 0)
                return new ApplicationLock(connection, lockKey);

            await connection.DisposeAsync();
            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class ApplicationLock(SqlConnection connection, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                // ปล่อยเองก่อนคืน connection เข้า pool — ล็อกแบบ Session ไม่ถูกปล่อยตอน Close เพราะ session ยังอยู่ใน pool
                await connection.ExecuteAsync(new CommandDefinition("sp_releaseapplock",
                    new { Resource = resource, LockOwner = "Session" }, commandType: CommandType.StoredProcedure));
            }
            catch
            {
                // connection เสียแล้ว = session จบ ล็อกหายไปพร้อมกัน — ล้าง pool ของ connection นี้กันได้ session ที่ยังถือล็อกกลับมา
                SqlConnection.ClearPool(connection);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }

    // ---------- shadow rows (Dapper) ----------

    internal const string InsertBranchSql = """
        INSERT Branch (Name, PhoneNumber1, Email, ProvinceId, Status, BranchGroupId, PinCode,
            StartPackageDate, PackageDaysAmout, EndPackageDate, CreatedDate, LastUpdated, CreatrdBy)
        OUTPUT INSERTED.Id
        VALUES (@Name, @PhoneNumber, @Email, @ProvinceId, 1, @BranchGroupId, @PinCode,
            @StartPackageDate, @PackageDays, @EndPackageDate, @CreatedDate, @CreatedDate, 0);
        """;

    internal const string LinkWorkspaceSql = """
        UPDATE sp_Workspace
        SET BranchId = @BranchId, TrialStartsAt = @TrialStartsAt, TrialEndsAt = @TrialEndsAt,
            ProvisionStep = @ProvisionStep, ProvisionError = NULL, UpdatedAt = @Now
        WHERE Id = @WorkspaceId AND BranchId IS NULL;
        """;

    /// <summary>กลุ่มสาขา "Service" — ตัวกรอง login เดิม (LegacyUserReader) รับเฉพาะกลุ่มนี้</summary>
    public const int ServiceBranchGroupId = 7;

    public async Task<int> CreateShadowBranchAsync(string shardKey, Workspace workspace, ShadowBranchSpec branch,
        int provisionStep, DateTime nowUtc, CancellationToken ct = default)
    {
        if (workspace.BranchId is not null)
            throw new InvalidOperationException($"workspace {workspace.Id} already has Branch {workspace.BranchId}");

        await using var connection = OpenShard(shardKey);
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);

        int branchId;
        try
        {
            branchId = await connection.ExecuteScalarAsync<int>(new CommandDefinition(InsertBranchSql, new
            {
                branch.Name,
                branch.PhoneNumber,
                branch.Email,
                branch.ProvinceId,
                BranchGroupId = ServiceBranchGroupId,
                branch.PinCode,
                StartPackageDate = branch.StartPackageDateLocal,
                branch.PackageDays,
                EndPackageDate = branch.EndPackageDateLocal,
                CreatedDate = branch.CreatedDateLocal
            }, tx, cancellationToken: ct));

            var linked = await connection.ExecuteAsync(new CommandDefinition(LinkWorkspaceSql, new
            {
                BranchId = branchId,
                TrialStartsAt = branch.PackageStartsAtUtc,
                TrialEndsAt = branch.PackageEndsAtUtc,
                ProvisionStep = provisionStep,
                Now = nowUtc,
                WorkspaceId = workspace.Id
            }, tx, cancellationToken: ct));

            if (linked != 1)
                throw new InvalidOperationException(
                    $"workspace {workspace.Id} was linked to a Branch concurrently (or no longer exists) — Branch insert rolled back");

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }

        // ให้ entity ที่ track อยู่เห็นค่าที่เพิ่งเขียนผ่าน Dapper (ไม่งั้น SaveChanges ถัดไปเขียนค่าเก่าทับ)
        await ReloadAsync(workspace, ct);
        return branchId;
    }

    internal const string FindShadowUserSql = """
        SELECT TOP 1 u.Id AS UserId, u.StaffId, u.UserName, CONVERT(bit, ISNULL(u.IsAdministrator, 0)) AS IsAdministrator
        FROM [User] u WITH (READUNCOMMITTED)
        JOIN Staff s WITH (READUNCOMMITTED) ON s.Id = u.StaffId
        WHERE u.UserName = @UserName AND s.BranchId = @BranchId
        ORDER BY u.Id;
        """;

    public async Task<ShadowUserRef?> FindShadowUserAsync(string shardKey, int branchId, string userName,
        CancellationToken ct = default)
    {
        await using var connection = OpenShard(shardKey);
        var row = await connection.QueryFirstOrDefaultAsync<ShadowUserRow>(new CommandDefinition(FindShadowUserSql,
            new { UserName = new DbString { Value = userName, IsAnsi = false, Length = 50 }, BranchId = branchId },
            cancellationToken: ct));
        return row is null ? null : new ShadowUserRef(row.UserId, row.StaffId, row.UserName, row.IsAdministrator);
    }

    /// <summary>
    /// ลำดับตาม FK ของ shadow DDL: ChangeLog → [User] · StaffSectorPosition/StaffRepairTypeNPoint/[User] → Staff
    /// · ChangeLog ที่อ้างพนักงาน/ตำแหน่งของสาขานี้ลบด้วย (ประวัติของแถวที่ไม่มีแล้ว)
    /// · ถ้ามีแถวอื่นอ้างผู้ใช้เหล่านี้ (Customer/Car — ไม่ควรมีเพราะอู่ยังเปิดไม่เสร็จ เข้าสู่ระบบไม่ได้) FK จะล้มทั้งก้อน → ไม่ลบอะไรเลย
    /// </summary>
    internal const string RemoveShadowBranchSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        DECLARE @Staff TABLE (Id bigint PRIMARY KEY);
        INSERT @Staff (Id) SELECT s.Id FROM Staff s WHERE s.BranchId = @BranchId;

        DECLARE @Users TABLE (Id bigint PRIMARY KEY);
        INSERT @Users (Id) SELECT u.Id FROM [User] u JOIN @Staff s ON s.Id = u.StaffId;

        DELETE c FROM ChangeLog c
        WHERE c.UserIdChanged IN (SELECT Id FROM @Users)
           OR (c.EntityName = N'Staff' AND c.PrimaryKeyValue IN (SELECT CONVERT(nvarchar(100), Id) FROM @Staff))
           OR (c.EntityName = N'StaffSectorPosition' AND c.PrimaryKeyValue IN (
                SELECT CONVERT(nvarchar(100), ssp.Id) FROM StaffSectorPosition ssp JOIN @Staff s ON s.Id = ssp.StaffId));

        DELETE ssp FROM StaffSectorPosition ssp JOIN @Staff s ON s.Id = ssp.StaffId;
        DELETE r FROM StaffRepairTypeNPoint r JOIN @Staff s ON s.Id = r.StaffId;
        DELETE u FROM [User] u JOIN @Users x ON x.Id = u.Id;
        DELETE st FROM Staff st JOIN @Staff s ON s.Id = st.Id;

        UPDATE Branch SET Status = 0, LastUpdated = @Now WHERE Id = @BranchId;

        SELECT (SELECT COUNT(1) FROM @Staff) AS StaffRemoved, (SELECT COUNT(1) FROM @Users) AS UsersRemoved;
        """;

    public async Task<ShadowCleanupResult> RemoveShadowBranchAsync(string shardKey, int branchId, DateTime nowUtc,
        CancellationToken ct = default)
    {
        await using var connection = OpenShard(shardKey);
        await connection.OpenAsync(ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            var result = await connection.QuerySingleAsync<ShadowCleanupRow>(new CommandDefinition(RemoveShadowBranchSql,
                new { BranchId = branchId, Now = BranchPackageDates.ToBranchLocal(nowUtc) }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
            return new ShadowCleanupResult(result.StaffRemoved, result.UsersRemoved);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private SqlConnection OpenShard(string shardKey)
    {
        if (!_legacy.ConnectionStrings.TryGetValue(shardKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{shardKey}' — ตั้งค่า {LegacyShardOptions.SectionName}:ConnectionStrings:{shardKey} " +
                "(ค่าเดียวกับ SelfServe:ConnectionString)");

        return new SqlConnection(cs);
    }

    private sealed class ShadowUserRow
    {
        public long UserId { get; set; }
        public long StaffId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public bool IsAdministrator { get; set; }
    }

    private sealed class ShadowCleanupRow
    {
        public int StaffRemoved { get; set; }
        public int UsersRemoved { get; set; }
    }
}
