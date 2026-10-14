using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ข้อมูลของ spadmin (S5) — sp_* ผ่าน <see cref="SelfServeDbContext"/> · แถว Branch ของ shard sp1 อ่านด้วย Dapper
/// (ทางเดียวกับ WorkspacePlanReader/SelfServeLifecycleRepository) และ<b>เขียนผ่าน connection ของ SelfServeDbContext</b>
/// ในฐาน GarageSelfServe เดียวกัน — Branch + sp_Workspace + sp_WorkspaceAudit จึงอยู่ใน transaction เดียว
/// · ห้าม BeginTransactionAsync ตรงๆ (EnableRetryOnFailure) → ห่อด้วย execution strategy แบบ StarterDataRepository
/// · [SECURITY] ก่อนเขียนทุกครั้งตรวจ <see cref="SelfServeDatabaseGuard"/> (ค่าตั้งชี้ GarageSelfServe จริง ไม่ใช่ Garage/GarageService)
/// </summary>
/// <remarks><paramref name="configuration"/> ส่งเข้ามาตอนลงทะเบียน (แบบ WorkspacePurgeData — host อย่าง CLI อาจไม่ได้ลงทะเบียน IConfiguration)</remarks>
public sealed class SelfServeAdminRepository(
    SelfServeDbContext db,
    IWorkspacePlanReader planReader,
    IConfiguration configuration,
    IOptions<LegacyShardOptions> legacyOptions,
    IOptions<SelfServeOptions> selfServeOptions) : ISelfServeAdminRepository
{
    private readonly LegacyShardOptions _legacy = legacyOptions.Value;
    private readonly SelfServeOptions _options = selfServeOptions.Value;

    /// <summary>
    /// เลื่อน EndPackageDate (เวลาไทย) · PackageDaysAmout ตามให้ Start + Days = End (ข้อมูลแบบ legacy ไม่ขัดกันเอง — ตัวตัดสินจริงคือ
    /// EndPackageDate เท่านั้น ดู PlanPolicy) · ไม่มีกำหนด (NULL) คงจำนวนวันเดิมไว้
    /// </summary>
    internal const string UpdateBranchEndSql = """
        UPDATE dbo.Branch
        SET EndPackageDate = @EndPackageDate,
            PackageDaysAmout = CASE WHEN @EndPackageDate IS NULL OR StartPackageDate IS NULL THEN PackageDaysAmout
                                    ELSE DATEDIFF(day, StartPackageDate, @EndPackageDate) END,
            LastUpdated = GETDATE()
        WHERE Id = @BranchId
        """;

    public async Task<IReadOnlyList<AdminWorkspaceRow>> ListAsync(WorkspaceStatus? status, WorkspacePlanKind? plan,
        CancellationToken ct = default)
    {
        var query = db.Workspaces.AsNoTracking();
        if (status is { } s) query = query.Where(w => w.Status == s);
        if (plan is { } p) query = query.Where(w => w.Plan == p);

        var rows = await (
                from w in query
                join a in db.Accounts.AsNoTracking() on w.OwnerAccountId equals a.Id into owners
                from a in owners.DefaultIfEmpty()
                orderby w.WorkspaceNo
                select new { Workspace = w, Email = a == null ? null : a.Email })
            .ToListAsync(ct);
        if (rows.Count == 0) return [];

        var ends = await ReadBranchEndsAsync(ct);
        return rows.Select(r =>
        {
            var w = r.Workspace;
            var branchEnd = w.BranchId is { } b && ends.TryGetValue(b, out var e) ? e : null;
            var endsAt = branchEnd ?? (w.Plan is WorkspacePlanKind.Trial or WorkspacePlanKind.Suspended ? w.TrialEndsAt : null);
            return new AdminWorkspaceRow(w.Id, w.WorkspaceNo, w.BranchId, w.Name, w.Phone, w.Plan, w.Status, endsAt,
                w.CreatedAt, r.Email, w.PurgeAfter, w.ConvertedToBranchId);
        }).ToList();
    }

    public async Task<AdminWorkspaceSnapshot?> GetAsync(int workspaceNo, int auditTail, CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.AsNoTracking().FirstOrDefaultAsync(w => w.WorkspaceNo == workspaceNo, ct);
        if (workspace is null) return null;

        // [SECURITY] เลือกเฉพาะคอลัมน์ที่แสดงได้ — ไม่มี hash รหัสผ่าน/token ออกจากฐานเลย
        var owner = workspace.OwnerAccountId is not { } ownerId
            ? null
            : await db.Accounts.AsNoTracking()
                .Where(a => a.Id == ownerId)
                .Select(a => new AdminAccountInfo(a.Id, a.Email, a.NormalizedEmail, a.Provider, a.Status, a.LoginUserName,
                    a.UserId, a.EmailVerifiedAt, a.CreatedAt))
                .FirstOrDefaultAsync(ct);

        var branch = workspace.BranchId is { } branchId
            ? (await planReader.ReadAsync(_options.ShardKey, branchId, ct)).Branch
            : null;

        var audit = auditTail <= 0
            ? []
            : (await db.WorkspaceAudits.AsNoTracking()
                .Where(a => a.WorkspaceId == workspace.Id)
                .OrderByDescending(a => a.Id)
                .Take(auditTail)
                .Select(a => new AdminAuditEntry(a.CreatedAt, a.Action, a.Actor, a.Note, a.PayloadJson))
                .ToListAsync(ct)).AsEnumerable().Reverse().ToList();

        return new AdminWorkspaceSnapshot(workspace, owner, branch, audit);
    }

    public string? CheckWritableDatabase()
    {
        var others = _legacy.ConnectionStrings
            .Where(kv => !string.Equals(kv.Key, _options.ShardKey, StringComparison.OrdinalIgnoreCase));
        return SelfServeDatabaseGuard.Check(
            _options.ConnectionString,
            _legacy.ConnectionStrings.GetValueOrDefault(_options.ShardKey),
            configuration.GetConnectionString(ServiceDbConnectionResolver.ServiceDbConnectionName),
            others);
    }

    public async Task ApplyPackageChangeAsync(WorkspacePackageChange change, CancellationToken ct = default)
    {
        if (CheckWritableDatabase() is { } mismatch)
            throw new InvalidOperationException($"ไม่เขียนฐาน: {mismatch}");

        await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == change.WorkspaceId, ct)
                            ?? throw new InvalidOperationException($"ไม่พบ workspace {change.WorkspaceId}");
            if (workspace.BranchId != change.BranchId)
                throw new InvalidOperationException(
                    $"สาขาของ workspace เปลี่ยนไประหว่างทำงาน ({workspace.BranchId} ≠ {change.BranchId}) ยกเลิก");

            if (change.SetBranchEnd)
            {
                var local = change.EndsAtUtc is { } end ? BranchPackageDates.ToBranchLocal(end) : (DateTime?)null;
                var rows = await db.Database.ExecuteSqlRawAsync(UpdateBranchEndSql,
                    [
                        new SqlParameter("@EndPackageDate", System.Data.SqlDbType.DateTime) { Value = (object?)local ?? DBNull.Value },
                        new SqlParameter("@BranchId", change.BranchId)
                    ], ct);
                if (rows != 1)
                    throw new InvalidOperationException($"ไม่พบแถว Branch {change.BranchId} ในฐาน self-serve ยกเลิก");

                workspace.TrialEndsAt = change.EndsAtUtc;   // สำเนาต้องตรงกับ Branch เสมอ (Workspace.cs)
            }

            workspace.Plan = change.Plan;
            workspace.Status = change.Status;
            if (change.ClearPurgeAfter) workspace.PurgeAfter = null;
            workspace.UpdatedAt = change.Now;

            db.WorkspaceAudits.Add(new WorkspaceAudit
            {
                WorkspaceId = change.WorkspaceId,
                Action = change.AuditAction,
                Actor = Limit(change.Actor, 200) ?? string.Empty,
                Note = Limit(change.Note, 1000),
                PayloadJson = change.PayloadJson,
                CreatedAt = change.Now
            });

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
    }

    private async Task<Dictionary<int, DateTime?>> ReadBranchEndsAsync(CancellationToken ct)
    {
        if (!_legacy.ConnectionStrings.TryGetValue(_options.ShardKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{_options.ShardKey}' ตั้งค่า {LegacyShardOptions.SectionName}:ConnectionStrings:{_options.ShardKey}");

        await using var connection = new SqlConnection(cs);
        var rows = await connection.QueryAsync<(int Id, DateTime? EndPackageDate)>(
            new CommandDefinition(SelfServeLifecycleRepository.BranchEndDatesSql, cancellationToken: ct));
        return rows.ToDictionary(r => r.Id, r => BranchPackageDates.ToUtc(r.EndPackageDate));
    }

    private static string? Limit(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
