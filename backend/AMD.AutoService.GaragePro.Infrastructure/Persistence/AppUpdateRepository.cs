using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.AppUpdates;
using AMD.AutoService.GaragePro.Application.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class AppUpdateRepository(AppUpdatesDbContext db, TimeProvider clock, ILogger<AppUpdateRepository> logger) : IAppUpdateRepository
{
    public async Task<AppUpdatePolicy> ReadAsync(string platform, CancellationToken ct)
    {
        try
        {
            var row = await db.Policies.AsNoTracking().SingleOrDefaultAsync(x => x.Platform == platform, ct);
            AccessUpdateMetrics.PolicyReads.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
            return row is null ? AppVersions.Default(platform) : new(platform, row.Enabled, row.LatestVersion,
                row.MinimumVersion, row.Message, row.Revision, AppVersions.StoreUrl(platform));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AccessUpdateMetrics.PolicyReads.Add(1, new KeyValuePair<string, object?>("outcome", "error"));
            logger.LogError(ex, "App update policy read failed for {Platform}", platform);
            throw;
        }
    }

    public async Task<bool> SaveAsync(AppUpdatePolicy before, AppUpdatePolicy after, ICurrentUser actor, CancellationToken ct)
    {
        var row = new AppUpdatePolicyRow { Platform = after.Platform, Enabled = after.Enabled,
            LatestVersion = after.LatestVersion, MinimumVersion = after.MinimumVersion, Message = after.Message, Revision = after.Revision };
        if (before.Revision == 0) db.Policies.Add(row);
        else
        {
            db.Policies.Attach(row);
            db.Entry(row).State = EntityState.Modified;
            db.Entry(row).Property(x => x.Revision).OriginalValue = before.Revision;
        }
        db.History.Add(new AppUpdateAuditRow { Platform = after.Platform, Revision = after.Revision,
            EditedByShard = actor.ShardKey, EditedByUserId = actor.UserId, EditedByName = actor.UserName,
            EditedAt = clock.GetUtcNow().UtcDateTime, BeforeJson = JsonSerializer.Serialize(before), AfterJson = JsonSerializer.Serialize(after) });
        try
        {
            // SaveChanges เดียว = transaction เดียว: นโยบายกับประวัติ commit พร้อมกันหรือไม่ commit ทั้งคู่
            // revision เป็น concurrency token — ผู้ดูแลสองคนบันทึกจาก revision เดียวกัน คนหลังได้ conflict ไม่ใช่ทับเงียบๆ
            await db.SaveChangesAsync(ct);
            logger.LogInformation("App update policy {Platform} revision {Revision} edited by {Shard}/{UserId}", after.Platform, after.Revision, actor.ShardKey, actor.UserId);
            return true;
        }
        catch (DbUpdateConcurrencyException) { db.ChangeTracker.Clear(); return false; }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        { db.ChangeTracker.Clear(); return false; }
    }

    public async Task<IReadOnlyList<AppUpdateHistory>> HistoryAsync(string platform, CancellationToken ct)
    {
        var rows = await db.History.AsNoTracking().Where(x => x.Platform == platform).OrderByDescending(x => x.Revision).Take(50).ToListAsync(ct);
        return rows.Select(x => new AppUpdateHistory(x.Id, x.Platform, x.Revision, x.EditedByShard, x.EditedByUserId,
            x.EditedByName, DateTime.SpecifyKind(x.EditedAt, DateTimeKind.Utc),
            JsonSerializer.Deserialize<AppUpdatePolicy>(x.BeforeJson)!, JsonSerializer.Deserialize<AppUpdatePolicy>(x.AfterJson)!)).ToList();
    }
}
