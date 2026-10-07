using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Notifications;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class NotificationRepository(ServiceDbContext db) : INotificationRepository
{
    public void Add(Notification notification) => db.Notifications.Add(notification);

    public async Task<IReadOnlyList<Notification>> GetOpenBySubjectAsync(
        string shardKey, int branchId, string subjectKey, CancellationToken ct = default)
    {
        // ต้องรวมแถวที่เพิ่ง Add ในคำขอเดียวกันด้วย (ยังไม่ถึงฐาน) — เช่นอนุมัติ PR แล้ว PO ที่ผูกอยู่ถูก resolve ทันที
        var pending = db.ChangeTracker.Entries<Notification>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .Where(n => n.LegacyShardKey == shardKey && n.LegacyBranchId == branchId
                        && n.SubjectKey == subjectKey && n.ResolvedAt == null);

        var stored = await db.Notifications
            .Where(n => n.LegacyShardKey == shardKey && n.LegacyBranchId == branchId
                        && n.SubjectKey == subjectKey && n.ResolvedAt == null)
            .ToListAsync(ct);

        return stored.Concat(pending).Distinct().ToList();
    }

    public async Task<IReadOnlyList<NotificationRow>> PageAsync(
        NotificationViewer viewer, DateTime since, bool unreadOnly,
        DateTime? beforeAt, Guid? beforeId, int take, CancellationToken ct = default)
    {
        var query = Visible(viewer, since);
        if (unreadOnly) query = Unread(query, viewer.StaffId);
        if (beforeAt is { } at && beforeId is { } id)
            query = query.Where(n => n.CreatedAt < at || (n.CreatedAt == at && n.Id.CompareTo(id) < 0));

        var staffId = viewer.StaffId;
        var rows = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(take)
            .Select(n => new
            {
                Notification = n,
                ReadAt = db.NotificationReads
                    .Where(r => r.NotificationId == n.Id && r.StaffId == staffId)
                    .Select(r => (DateTime?)r.ReadAt)
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        return rows.Select(r => new NotificationRow(r.Notification, r.ReadAt)).ToList();
    }

    public Task<int> CountUnreadAsync(NotificationViewer viewer, DateTime since, CancellationToken ct = default) =>
        Unread(Visible(viewer, since), viewer.StaffId).CountAsync(ct);

    public async Task<IReadOnlyList<Guid>> GetUnreadIdsAsync(
        NotificationViewer viewer, DateTime since, CancellationToken ct = default) =>
        await Unread(Visible(viewer, since), viewer.StaffId).Select(n => n.Id).ToListAsync(ct);

    public Task<Notification?> GetVisibleAsync(
        NotificationViewer viewer, DateTime since, Guid id, CancellationToken ct = default) =>
        Visible(viewer, since).FirstOrDefaultAsync(n => n.Id == id, ct);

    public Task<NotificationRead?> GetReadAsync(Guid notificationId, long staffId, CancellationToken ct = default) =>
        db.NotificationReads.FirstOrDefaultAsync(r => r.NotificationId == notificationId && r.StaffId == staffId, ct);

    public void AddRead(NotificationRead read) => db.NotificationReads.Add(read);

    public void RemoveRead(NotificationRead read) => db.NotificationReads.Remove(read);

    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // อ่านพร้อมกันจากอีกแท็บ — แถวที่ชนมีอยู่แล้วคือผลลัพธ์ที่ต้องการ จึงทิ้งแถวที่ค้างแล้วจบ
            foreach (var entry in db.ChangeTracker.Entries<NotificationRead>().Where(e => e.State == EntityState.Added).ToList())
                entry.State = EntityState.Detached;
        }
        catch (DbUpdateConcurrencyException)
        {
            // ยกเลิกการอ่านซ้อนกันแล้วแถวถูกลบไปก่อน — ผลลัพธ์คือ "ยังไม่อ่าน" ตามที่ขออยู่แล้ว
            foreach (var entry in db.ChangeTracker.Entries<NotificationRead>().Where(e => e.State == EntityState.Deleted).ToList())
                entry.State = EntityState.Detached;
        }
    }

    private IQueryable<Notification> Visible(NotificationViewer viewer, DateTime since) =>
        db.Notifications.AsNoTracking().Where(NotificationRules.VisibleTo(viewer, since));

    private IQueryable<Notification> Unread(IQueryable<Notification> query, long staffId) =>
        query.Where(n => n.ResolvedAt == null
            && !db.NotificationReads.Any(r => r.NotificationId == n.Id && r.StaffId == staffId));
}
