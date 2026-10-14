using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ตาราง sp_* ของข้อมูลตัวอย่าง (S3): อ่าน workspace + เจ้าของ · ledger sp_SampleEntity · สถานะ SampleDataLoadedAt/SampleDataError
/// · ลงทะเบียนเฉพาะเมื่อตั้ง SelfServe:ConnectionString (ใช้ <see cref="SelfServeDbContext"/>)
/// · เจ้าของอู่ (ชื่อ/Staff.Id) อ่านจาก [User]/Staff ในฐานเดียวกัน (GarageSelfServe) ผ่าน connection ของ context นี้ —
///   ไม่พึ่ง LegacyShards:ConnectionStrings:sp1 และไม่ผูก ICurrentUser
/// </summary>
public sealed class StarterDataStore(
    SelfServeDbContext db,
    IMemoryCache cache,
    IOptions<SelfServeOptions> options,
    TimeProvider clock,
    ILogger<StarterDataStore> logger) : IStarterDataStore
{
    /// <summary>ตรงกับความยาวคอลัมน์ sp_Workspace.SampleDataError</summary>
    private const int ErrorMaxLength = 2000;

    internal const string OwnerSql = """
        SELECT TOP 1 u.Id AS UserId, ISNULL(u.UserName, N'') AS UserName, u.StaffId,
               LTRIM(RTRIM(ISNULL(s.FirstName, N'') + N' ' + ISNULL(s.LastName, N''))) AS DisplayName
        FROM [User] u WITH (READUNCOMMITTED)
        LEFT JOIN Staff s WITH (READUNCOMMITTED) ON s.Id = u.StaffId
        WHERE u.Id = @UserId
        """;

    public async Task<StarterWorkspace?> GetWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => new { w.Id, w.WorkspaceNo, w.BranchId, w.ProvinceId, w.OwnerAccountId, w.Status, w.SampleDataLoadedAt })
            .FirstOrDefaultAsync(ct);
        if (workspace is null || workspace.BranchId is not { } branchId || workspace.OwnerAccountId is null ||
            workspace.Status == WorkspaceStatus.Purged)
            return null;

        var account = await db.Accounts.AsNoTracking()
            .Where(a => a.Id == workspace.OwnerAccountId)
            .Select(a => new { a.UserId, a.LoginUserName })
            .FirstOrDefaultAsync(ct);
        if (account?.UserId is not { } userId) return null;

        var owner = await db.Database.GetDbConnection().QueryFirstOrDefaultAsync<OwnerRow>(
            new CommandDefinition(OwnerSql, new { UserId = userId }, cancellationToken: ct));
        if (owner is null) return null;

        // StaffService ของ sp1 (B5) อ่าน WorkspaceNo ผ่าน IWorkspacePolicy ซึ่ง cache ต่อสาขา 60 วิ — ถ้าเคยถูกอ่านก่อนแถว
        // sp_Workspace มี BranchId (ระหว่าง provisioning) จะได้ผล "ไม่มี workspace" ค้างอยู่ แล้วสร้างช่างตัวอย่างไม่ได้
        WorkspacePolicy.Invalidate(cache, options.Value, branchId);

        var userName = string.IsNullOrWhiteSpace(owner.UserName) ? account.LoginUserName ?? string.Empty : owner.UserName;
        return new StarterWorkspace(
            workspace.Id, workspace.WorkspaceNo, branchId, workspace.ProvinceId,
            owner.UserId, userName,
            string.IsNullOrWhiteSpace(owner.DisplayName) ? userName : owner.DisplayName,
            owner.StaffId, workspace.SampleDataLoadedAt);
    }

    public async Task<IReadOnlyList<StarterLedgerEntry>> GetLedgerAsync(Guid workspaceId, CancellationToken ct = default) =>
        await db.SampleEntities.AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId)
            .OrderBy(e => e.Id)
            .Select(e => new StarterLedgerEntry(e.Id, e.EntityType, e.EntityId))
            .ToListAsync(ct);

    public async Task RecordAsync(Guid workspaceId, string entityType, string entityId, CancellationToken ct = default)
    {
        // unique (WorkspaceId, EntityType, EntityId) — ซ้ำ = ข้าม (รอบที่ถูกตัดกลางทางแล้วกลับมาทำต่อ)
        if (await db.SampleEntities.AnyAsync(
                e => e.WorkspaceId == workspaceId && e.EntityType == entityType && e.EntityId == entityId, ct))
            return;

        db.SampleEntities.Add(new SampleEntity
        {
            WorkspaceId = workspaceId,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = clock.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task RemoveAsync(Guid workspaceId, IReadOnlyCollection<long> ledgerIds, CancellationToken ct = default)
    {
        if (ledgerIds.Count == 0) return;
        var ids = ledgerIds.ToList();
        await db.SampleEntities
            .Where(e => e.WorkspaceId == workspaceId && ids.Contains(e.Id))
            .ExecuteDeleteAsync(ct);
    }

    public async Task SetSampleStateAsync(Guid workspaceId, DateTime? loadedAt, string? error, CancellationToken ct = default)
    {
        var trimmed = error is { Length: > ErrorMaxLength } ? error[..ErrorMaxLength] : error;
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Workspaces
            .Where(w => w.Id == workspaceId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.SampleDataLoadedAt, loadedAt)
                .SetProperty(w => w.SampleDataError, trimmed)
                .SetProperty(w => w.UpdatedAt, now), ct);

        // ข้อความไม่มีความลับ (ตัวสร้างเลือกเฉพาะข้อความไทยของ service/ชนิด exception) — log ไว้ให้ทีมงานไล่ปัญหาได้
        if (trimmed is not null)
            logger.LogWarning("Sample data for workspace {WorkspaceId} finished with errors: {Error}", workspaceId, trimmed);
    }

    private sealed class OwnerRow
    {
        public long UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public long? StaffId { get; set; }
        public string DisplayName { get; set; } = string.Empty;
    }
}
