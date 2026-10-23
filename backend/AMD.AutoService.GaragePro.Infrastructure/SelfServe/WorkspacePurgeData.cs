using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// [SECURITY] ตรวจว่าค่าตั้งชี้ฐาน GarageSelfServe จริง ก่อนอนุญาตให้ลบอะไร — ฟังก์ชันล้วนบน connection string
/// · <c>LegacyShards:ConnectionStrings:{sp1}</c> (Dapper ลบแถวรูปร่าง Garage) ต้องเป็นฐาน/เครื่องเดียวกับ <c>SelfServe:ConnectionString</c>
/// · ฐาน self-serve ต้องไม่ใช่ฐานเดียวกับ GarageService (<c>ConnectionStrings:ServiceDb</c>) หรือ Garage ของ shard อื่น
/// ถ้าตั้งผิดแม้ตัวเดียว คำสั่ง DELETE … WHERE BranchId = N จะไปโดนสาขาจริงของลูกค้าที่บังเอิญมี Id เดียวกัน
/// </summary>
public static class SelfServeDatabaseGuard
{
    public static string? Check(
        string? selfServeConnection,
        string? selfServeShardLegacyConnection,
        string? serviceDbConnection,
        IEnumerable<KeyValuePair<string, string>> otherLegacyShards)
    {
        if (Describe(selfServeConnection) is not { } self)
            return "ยังไม่ได้ตั้งค่า SelfServe:ConnectionString";
        if (Describe(selfServeShardLegacyConnection) is not { } shadow)
            return "ยังไม่ได้ตั้งค่า LegacyShards:ConnectionStrings ของ shard self-serve";
        if (shadow != self)
            return "LegacyShards:ConnectionStrings ของ shard self-serve ชี้คนละฐานกับ SelfServe:ConnectionString";
        if (Describe(serviceDbConnection) is { } service && service == self)
            return "SelfServe:ConnectionString ชี้ฐานเดียวกับ ConnectionStrings:ServiceDb (GarageService)";
        foreach (var (name, cs) in otherLegacyShards)
            if (Describe(cs) is { } other && other == self)
                return $"ฐาน self-serve เป็นฐานเดียวกับ LegacyShards:ConnectionStrings:{name}";
        return null;
    }

    /// <summary>ชื่อฐานจาก connection string (ไม่เปิด connection) — ว่าง = ไม่ระบุ/อ่านไม่ได้</summary>
    public static string? DatabaseName(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var catalog = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            return string.IsNullOrWhiteSpace(catalog) ? null : catalog.Trim();
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static (string Server, string Database)? Describe(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        try
        {
            var b = new SqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(b.InitialCatalog)) return null;
            var server = b.DataSource.Trim().ToLowerInvariant().Replace("tcp:", string.Empty).Replace(",1433", string.Empty);
            return (server, b.InitialCatalog.Trim().ToLowerInvariant());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}

/// <summary>
/// สิ่งที่เหลือของอู่หลังลบ (ฟังก์ชันล้วน — เทสต์ได้โดยไม่มีฐาน)
/// · sp_Workspace: Status=Purged · ล้างชื่อ/เบอร์ (Name เป็น NOT NULL จึงเหลือค่าว่าง) · คง WorkspaceNo/วันที่ให้ audit อ้างถึงได้
/// · sp_Account: Status=Purged · ล้างอีเมล/รหัสผ่าน/Google subject/token/IP — [BIZ] <b>คง NormalizedEmail ไว้ตลอดกาล</b>
///   อีเมลเดิม (รวม gmail ที่ใส่จุด/+tag) จึงสมัครใหม่ไม่ได้ (docs/12 §6–7)
/// </summary>
public static class WorkspaceTombstone
{
    public static void Apply(Workspace workspace, SelfServeAccount? owner, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        workspace.Status = WorkspaceStatus.Purged;
        workspace.PurgedAt = now;
        workspace.UpdatedAt = now;
        workspace.Name = string.Empty;
        workspace.Phone = null;

        if (owner is null) return;
        owner.Status = SelfServeAccountStatus.Purged;
        owner.Email = null;
        owner.PasswordHash = null;
        owner.GoogleSubject = null;
        owner.SetupTokenHash = null;
        owner.SetupTokenExpiresAt = null;
        owner.LoginUserName = null;
        owner.UserId = null;
        owner.CreatedIp = null;
        owner.CreatedUserAgent = null;
        owner.UpdatedAt = now;
    }
}

/// <summary>
/// I/O จริงของการลบอู่ — svc_* ผ่าน ServiceDbContext ของ scope sp1 · แถวรูปร่าง Garage ผ่าน Dapper · sp_* ผ่าน SelfServeDbContext
/// · guard ชั้นที่สอง: ตรวจชื่อฐานของ connection ที่จะใช้ลบจริงทุกเฟส (ไม่เชื่อแค่ค่าตั้งที่ตรวจตอนเริ่ม)
/// </summary>
/// <remarks><paramref name="configuration"/> ส่งเข้ามาตอนลงทะเบียน (ไม่ resolve จาก DI — host อย่าง CLI/เทสต์อาจไม่ได้ลงทะเบียน IConfiguration)</remarks>
public sealed class WorkspacePurgeData(
    SelfServeDbContext db,
    IConfiguration configuration,
    IOptions<LegacyShardOptions> legacyOptions,
    IOptions<SelfServeOptions> selfServeOptions) : IWorkspacePurgeData
{
    private readonly LegacyShardOptions _legacy = legacyOptions.Value;
    private readonly SelfServeOptions _options = selfServeOptions.Value;

    internal const string BranchUserIdsSql = """
        SELECT u.Id FROM [User] u WITH (READUNCOMMITTED)
        JOIN Staff s WITH (READUNCOMMITTED) ON s.Id = u.StaffId
        WHERE s.BranchId = @BranchId
        """;

    /// <summary>
    /// ชุดแถวของสาขาลงตารางชั่วคราว (กติกาการมองเห็นเดียวกับรายการลูกค้า/รถ — BranchDataScope) แล้วตัดลูกค้า/รถที่สาขาอื่น
    /// อ้างถึงด้วยออก ([SECURITY] ไม่ควรเกิดใน sp1 แต่ถ้าเกิด แถวที่เหลือจะทำให้ FK ล้มทั้ง transaction แทนการลบข้อมูลของอู่อื่น)
    /// </summary>
    /// <remarks>
    /// ต้องรันก่อน <see cref="CollectShadowRowsSql"/> ด้วยคำสั่ง<b>ที่ไม่มีพารามิเตอร์</b> — คำสั่งที่มีพารามิเตอร์ถูกส่งเป็น
    /// sp_executesql ซึ่งเป็น scope ของตัวเอง ตาราง #temp ที่สร้างข้างในหายทันทีที่คำสั่งจบ คำสั่งถัดไปจึงได้
    /// "Invalid object name '#PurgeStaff'" (พบตอนรันกับฐานจริงครั้งแรก) · สร้างที่ระดับ session แล้ว sp_executesql มองเห็นได้
    /// </remarks>
    internal const string CreateShadowTempTablesSql = """
        DROP TABLE IF EXISTS #PurgeStaff, #PurgeUser, #PurgeCustomer, #PurgeCar;
        CREATE TABLE #PurgeStaff (Id bigint NOT NULL PRIMARY KEY);
        CREATE TABLE #PurgeUser (Id bigint NOT NULL PRIMARY KEY);
        CREATE TABLE #PurgeCustomer (Id bigint NOT NULL PRIMARY KEY);
        CREATE TABLE #PurgeCar (Id bigint NOT NULL PRIMARY KEY);
        """;

    internal static readonly string CollectShadowRowsSql = $"""
        INSERT #PurgeStaff (Id) SELECT s.Id FROM Staff s WHERE s.BranchId = @BranchId;
        INSERT #PurgeUser (Id) SELECT u.Id FROM [User] u JOIN #PurgeStaff s ON s.Id = u.StaffId;
        INSERT #PurgeCustomer (Id)
            SELECT DISTINCT scoped.Id FROM ({BranchDataScope.CustomerIds}) scoped WHERE scoped.Id IS NOT NULL;
        INSERT #PurgeCar (Id)
            SELECT DISTINCT scoped.Id FROM ({BranchDataScope.VehicleIds}) scoped WHERE scoped.Id IS NOT NULL;

        DELETE c FROM #PurgeCustomer c
        WHERE EXISTS (SELECT 1 FROM PJCarPickUp p WHERE p.CustomerId = c.Id AND ISNULL(p.BranchId, -1) <> @BranchId)
           OR EXISTS (SELECT 1 FROM CarCustomer cc JOIN [User] u ON u.Id = cc.UpdatedBy JOIN Staff st ON st.Id = u.StaffId
                      WHERE cc.CustomerId = c.Id AND ISNULL(st.BranchId, -1) <> @BranchId)
           OR EXISTS (SELECT 1 FROM Customer x JOIN [User] u ON u.Id = x.LastUserId JOIN Staff st ON st.Id = u.StaffId
                      WHERE x.Id = c.Id AND ISNULL(st.BranchId, -1) <> @BranchId);
        DELETE v FROM #PurgeCar v
        WHERE EXISTS (SELECT 1 FROM PJCarPickUp p WHERE p.CarId = v.Id AND ISNULL(p.BranchId, -1) <> @BranchId)
           OR EXISTS (SELECT 1 FROM CarCustomer cc JOIN [User] u ON u.Id = cc.UpdatedBy JOIN Staff st ON st.Id = u.StaffId
                      WHERE cc.CarId = v.Id AND ISNULL(st.BranchId, -1) <> @BranchId)
           OR EXISTS (SELECT 1 FROM Car x JOIN [User] u ON u.Id = x.UpdatedBy JOIN Staff st ON st.Id = u.StaffId
                      WHERE x.Id = v.Id AND ISNULL(st.BranchId, -1) <> @BranchId);
        """;

    internal const string StaffPicturesSql = """
        SELECT s.Picture FROM Staff s JOIN #PurgeStaff p ON p.Id = s.Id WHERE NULLIF(s.Picture, '') IS NOT NULL
        """;

    internal const string CarImagesSql = """
        SELECT c.ImageUrl FROM Car c JOIN #PurgeCar p ON p.Id = c.Id WHERE NULLIF(c.ImageUrl, '') IS NOT NULL
        """;

    /// <summary>ลบลูกก่อนแม่ตาม FK ของ deploy/selfserve/02-shadow-ddl.generated.sql — คีย์ = ชื่อในรายงาน</summary>
    internal static readonly IReadOnlyList<(string Table, string Sql)> ShadowDeletes =
    [
        ("dbo.CarCustomer", "DELETE FROM CarCustomer WHERE CarId IN (SELECT Id FROM #PurgeCar) OR CustomerId IN (SELECT Id FROM #PurgeCustomer)"),
        ("dbo.PJCarPickUp", "DELETE FROM PJCarPickUp WHERE BranchId = @BranchId OR CarId IN (SELECT Id FROM #PurgeCar) OR CustomerId IN (SELECT Id FROM #PurgeCustomer)"),
        ("dbo.Car", "DELETE FROM Car WHERE Id IN (SELECT Id FROM #PurgeCar)"),
        ("dbo.Customer", "DELETE FROM Customer WHERE Id IN (SELECT Id FROM #PurgeCustomer)"),
        // แถวของอู่อื่นที่ยังอ้างผู้ใช้ของสาขานี้ (FK → [User]) — ตัดลิงก์ผู้บันทึกทิ้งแทนการลบแถวนั้น
        ("dbo.CarCustomer:detach", "UPDATE CarCustomer SET UpdatedBy = NULL WHERE UpdatedBy IN (SELECT Id FROM #PurgeUser)"),
        ("dbo.Car:detach", "UPDATE Car SET UpdatedBy = NULL WHERE UpdatedBy IN (SELECT Id FROM #PurgeUser)"),
        ("dbo.Customer:detach", "UPDATE Customer SET LastUserId = NULL WHERE LastUserId IN (SELECT Id FROM #PurgeUser)"),
        ("dbo.ChangeLog", "DELETE FROM ChangeLog WHERE UserIdChanged IN (SELECT Id FROM #PurgeUser)"),
        ("dbo.StaffRepairTypeNPoint", "DELETE FROM StaffRepairTypeNPoint WHERE StaffId IN (SELECT Id FROM #PurgeStaff)"),
        ("dbo.StaffSectorPosition", "DELETE FROM StaffSectorPosition WHERE StaffId IN (SELECT Id FROM #PurgeStaff)"),
        ("dbo.User", "DELETE FROM [User] WHERE Id IN (SELECT Id FROM #PurgeUser) OR StaffId IN (SELECT Id FROM #PurgeStaff)"),
        ("dbo.Staff", "DELETE FROM Staff WHERE Id IN (SELECT Id FROM #PurgeStaff)"),
        ("dbo.Branch", "DELETE FROM Branch WHERE Id = @BranchId")
    ];

    public string? CheckTargetDatabase(string shardKey)
    {
        var others = _legacy.ConnectionStrings
            .Where(kv => !string.Equals(kv.Key, shardKey, StringComparison.OrdinalIgnoreCase));
        return SelfServeDatabaseGuard.Check(
            _options.ConnectionString,
            _legacy.ConnectionStrings.GetValueOrDefault(shardKey),
            configuration.GetConnectionString(ServiceDbConnectionResolver.ServiceDbConnectionName),
            others);
    }

    public Task<PurgeTarget?> FindAsync(Guid workspaceId, CancellationToken ct = default) =>
        db.Workspaces.AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => new PurgeTarget(w.Id, w.WorkspaceNo, w.BranchId, w.Plan, w.Status, w.OwnerAccountId,
                db.PackagePayments.Any(p => p.WorkspaceId == w.Id && p.Status == PackagePaymentStatus.Pending)))
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<string>> ReadPackageSlipPathsAsync(Guid workspaceId, CancellationToken ct = default) =>
        await db.PackagePayments.AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId && p.SlipPath != "")
            .Select(p => p.SlipPath)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<long>> ReadBranchUserIdsAsync(string shardKey, int branchId,
        CancellationToken ct = default)
    {
        await using var connection = await OpenVerifiedAsync(shardKey, ct);
        return (await connection.QueryAsync<long>(
            new CommandDefinition(BranchUserIdsSql, new { BranchId = branchId }, cancellationToken: ct))).ToList();
    }

    public async Task<IReadOnlyList<string>> ReadAttachmentPathsAsync(IServiceProvider scope, ServicePurgeScope purgeScope,
        CancellationToken ct = default) =>
        await ServicePurgePlan.AttachmentPathsAsync(VerifiedServiceContext(scope, purgeScope), purgeScope, ct);

    public async Task<ServicePurgeResult> PurgeServiceDataAsync(IServiceProvider scope, ServicePurgeScope purgeScope,
        CancellationToken ct = default)
    {
        var service = VerifiedServiceContext(scope, purgeScope);
        service.Database.SetCommandTimeout(TimeSpan.FromMinutes(2));
        var paths = await ServicePurgePlan.AttachmentPathsAsync(service, purgeScope, ct);

        var deleted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var step in ServicePurgePlan.Steps)
            deleted[step.Table] = await step.Run(service, purgeScope, ct);

        return new ServicePurgeResult(paths, deleted);
    }

    /// <summary>
    /// guard 2: scope ต้องเป็นผู้ใช้ของ shard self-serve และ ServiceDbContext ต้องชี้ฐานเดียวกับ SelfServe:ConnectionString
    /// (ใช้ทั้งตอนอ่าน path ไฟล์ — ไฟล์ถูกลบตาม path นี้ — และตอนลบแถว)
    /// </summary>
    private ServiceDbContext VerifiedServiceContext(IServiceProvider scope, ServicePurgeScope purgeScope)
    {
        var user = scope.GetRequiredService<ICurrentUser>();
        if (!ServiceDbConnectionResolver.IsSelfServeShard(user.ShardKey, _options)
            || !ServiceDbConnectionResolver.IsSelfServeShard(purgeScope.ShardKey, _options))
            throw new InvalidOperationException("ปฏิเสธการลบ svc_*: scope ไม่ใช่ shard ของอู่ทดลองใช้");

        var service = scope.GetRequiredService<ServiceDbContext>();
        var expected = SelfServeDatabaseGuard.DatabaseName(_options.ConnectionString);
        var actual = SelfServeDatabaseGuard.DatabaseName(service.Database.GetConnectionString());
        if (expected is null || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("ปฏิเสธการลบ svc_*: ServiceDbContext ไม่ได้ชี้ฐาน GarageSelfServe");
        return service;
    }

    public async Task<ShadowPurgeResult> PurgeShadowDataAsync(string shardKey, int branchId,
        CancellationToken ct = default)
    {
        await using var connection = await OpenVerifiedAsync(shardKey, ct);
        await using var tx = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        try
        {
            var args = new { BranchId = branchId };
            await connection.ExecuteAsync(new CommandDefinition(CreateShadowTempTablesSql, null, tx,
                cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition(CollectShadowRowsSql, args, tx,
                commandTimeout: 120, cancellationToken: ct));
            var pictures = (await connection.QueryAsync<string>(new CommandDefinition(StaffPicturesSql, null, tx,
                cancellationToken: ct))).ToList();
            var carImages = (await connection.QueryAsync<string>(new CommandDefinition(CarImagesSql, null, tx,
                cancellationToken: ct))).ToList();

            var deleted = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (table, sql) in ShadowDeletes)
                deleted[table] = await connection.ExecuteAsync(new CommandDefinition(sql, args, tx,
                    commandTimeout: 120, cancellationToken: ct));

            await tx.CommitAsync(ct);
            return new ShadowPurgeResult(deleted, pictures, carImages);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task MarkPurgedAsync(PurgeTarget target, string actor, DateTime now, string payloadJson,
        CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.FirstAsync(w => w.Id == target.WorkspaceId, ct);
        var account = target.OwnerAccountId is { } accountId
            ? await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct)
            : null;

        WorkspaceTombstone.Apply(workspace, account, now);
        if (account is not null)
            db.EmailOtps.RemoveRange(await db.EmailOtps.Where(o => o.Email == account.NormalizedEmail).ToListAsync(ct));

        db.SampleEntities.RemoveRange(
            await db.SampleEntities.Where(s => s.WorkspaceId == target.WorkspaceId).ToListAsync(ct));

        // docs/17 กฎข้อ 12: รายการรอตรวจของอู่ที่ลบแล้วไม่มีวันถูกอนุมัติ · หมายเหตุเป็นข้อความอิสระของลูกค้า ล้างทิ้ง
        foreach (var payment in await db.PackagePayments.Where(p => p.WorkspaceId == target.WorkspaceId).ToListAsync(ct))
        {
            payment.Note = null;
            if (payment.Status != PackagePaymentStatus.Pending) continue;
            payment.Status = PackagePaymentStatus.Cancelled;
            payment.CancelledAt = now;
            payment.CancelledBy = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Length > 200 ? actor[..200] : actor;
        }
        db.WorkspaceAudits.Add(new WorkspaceAudit
        {
            WorkspaceId = target.WorkspaceId,
            Action = "purged",
            Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Length > 200 ? actor[..200] : actor,
            PayloadJson = payloadJson,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task AddAuditAsync(Guid workspaceId, string action, string actor, DateTime now, string payloadJson,
        CancellationToken ct = default)
    {
        db.WorkspaceAudits.Add(new WorkspaceAudit
        {
            WorkspaceId = workspaceId,
            Action = action,
            Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor.Length > 200 ? actor[..200] : actor,
            PayloadJson = payloadJson,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>เปิด connection ของ shard แล้วยืนยันด้วย DB_NAME() ว่าเป็นฐาน GarageSelfServe จริง (guard 2 ฝั่ง Dapper)</summary>
    private async Task<SqlConnection> OpenVerifiedAsync(string shardKey, CancellationToken ct)
    {
        if (!ServiceDbConnectionResolver.IsSelfServeShard(shardKey, _options))
            throw new InvalidOperationException("ปฏิเสธการลบ: shard ไม่ใช่อู่ทดลองใช้");
        if (!_legacy.ConnectionStrings.TryGetValue(_options.ShardKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{_options.ShardKey}' ตั้งค่า {LegacyShardOptions.SectionName}:ConnectionStrings:{_options.ShardKey}");

        var connection = new SqlConnection(cs);
        try
        {
            await connection.OpenAsync(ct);
            var actual = await connection.ExecuteScalarAsync<string>(
                new CommandDefinition("SELECT DB_NAME()", cancellationToken: ct));
            var expected = SelfServeDatabaseGuard.DatabaseName(_options.ConnectionString);
            if (expected is null || !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("ปฏิเสธการลบ: connection ของ shard self-serve ไม่ได้ชี้ฐาน GarageSelfServe");
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
