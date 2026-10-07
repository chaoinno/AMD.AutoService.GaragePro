using AMD.AutoService.GaragePro.Application.Notifications;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>แจ้งเตือน + สถานะอ่านต่อคน · คิวรีอ่านทุกตัวกรองด้วย <see cref="NotificationRules.VisibleTo"/> ชุดเดียวกัน</summary>
public interface INotificationRepository
{
    /// <summary>track อย่างเดียว ไม่ save — ผู้เรียก (service ของ action หลัก) เป็นคน save ใน transaction เดียวกัน</summary>
    void Add(Notification notification);

    /// <summary>แถวที่ยังไม่ resolve ของเรื่องนี้ (tracked — แก้แล้วผู้เรียกเป็นคน save)</summary>
    Task<IReadOnlyList<Notification>> GetOpenBySubjectAsync(
        string shardKey, int branchId, string subjectKey, CancellationToken ct = default);

    Task<IReadOnlyList<NotificationRow>> PageAsync(
        NotificationViewer viewer, DateTime since, bool unreadOnly,
        DateTime? beforeAt, Guid? beforeId, int take, CancellationToken ct = default);

    Task<int> CountUnreadAsync(NotificationViewer viewer, DateTime since, CancellationToken ct = default);

    Task<IReadOnlyList<Guid>> GetUnreadIdsAsync(NotificationViewer viewer, DateTime since, CancellationToken ct = default);

    Task<Notification?> GetVisibleAsync(
        NotificationViewer viewer, DateTime since, Guid id, CancellationToken ct = default);

    Task<NotificationRead?> GetReadAsync(Guid notificationId, long staffId, CancellationToken ct = default);

    void AddRead(NotificationRead read);
    void RemoveRead(NotificationRead read);

    /// <summary>กดอ่านซ้อนกันจากหลายแท็บแล้วชน PK ถือว่าอ่านแล้ว (ผลลัพธ์เดียวกัน) ไม่ใช่ error</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}

public sealed record NotificationRow(Notification Notification, DateTime? ReadAt);
