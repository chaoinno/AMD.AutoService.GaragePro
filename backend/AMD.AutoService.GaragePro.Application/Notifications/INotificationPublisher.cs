using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Notifications;

/// <summary>เนื้อหาแจ้งเตือนที่ยังไม่ระบุผู้รับ — ผู้กระทำ/สาขา/เวลา publisher เติมจาก ICurrentUser เอง</summary>
public sealed record NotificationDraft(
    string Kind,
    string TitleTh,
    string? BodyTh,
    string EntityType,
    Guid? EntityId,
    Guid? JobId = null,
    string? LinkHint = null,
    string? SubjectKey = null);

/// <summary>
/// สร้างแจ้งเตือนจาก service ของ action หลัก — **ห้าม SaveChanges เอง** (แบบเดียวกับ IWorkIntervalHook)
/// ทุก method แค่ track entity ใน ServiceDbContext ที่แชร์กันต่อ request แล้วผู้เรียก save ครั้งเดียว
/// แจ้งเตือนจึงไม่มีทางหายขณะที่ action สำเร็จ และไม่มีทางเกิดขณะที่ action ล้ม
/// · ไม่แจ้งผู้กระทำเอง · อ่าน Garage DB ไม่ได้ตอนหาผู้รับ = ข้ามคนนั้น ไม่ทำให้ action หลักล้ม
/// </summary>
public interface INotificationPublisher
{
    Task ToStaffAsync(NotificationDraft draft, IEnumerable<long> staffIds, CancellationToken ct = default);

    /// <summary>ผู้รับเก็บไว้เป็น User.Id (CreatedBy ของเอกสาร) — แปลงเป็น Staff.Id ผ่าน legacy ก่อน</summary>
    Task ToUserAsync(NotificationDraft draft, long userId, CancellationToken ct = default);

    Task ToRolesAsync(NotificationDraft draft, IEnumerable<UserRole> roles, CancellationToken ct = default);

    /// <summary>ปิดทุกแถวที่ยังเปิดของเรื่องนี้เป็น "ดำเนินการแล้วโดย {ผู้ใช้ปัจจุบัน}"</summary>
    Task ResolveAsync(string subjectKey, CancellationToken ct = default);

    Task<bool> HasOpenAsync(string subjectKey, CancellationToken ct = default);
}

public static class NotificationSubjects
{
    public static string PurchasePending(string kind, Guid documentId) => $"{kind}:{documentId:N}:pending";
    public static string QuotationAllRejected(Guid quotationId) => $"QT:{quotationId:N}:all_rejected";
}
