using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Notifications;
using AMD.AutoService.GaragePro.Application.Reports;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.ServiceDue;

public interface IServiceDueNotifier
{
    /// <summary>แจ้งเตือนรถใกล้ครบรอบบริการของสาขาใน <see cref="ICurrentUser"/> — คืนจำนวนแจ้งเตือนที่สร้างใหม่</summary>
    Task<int> NotifyBranchAsync(CancellationToken ct = default);
}

/// <summary>
/// [BIZ] แจ้งหน้าร้าน (FrontDesk) + ผู้จัดการ เมื่อรถ "เหลือ ≤ 7 วันถึงวันนัดเข้ารับบริการ" (ยืนยัน 2026-10-25)
/// · ครั้งเดียวต่อรถต่อรอบนัด: SubjectKey <c>service-due:{vehicleId}:{dueOn}</c> — เคยมีแล้ว (แม้ปิดไปแล้ว) ไม่แจ้งซ้ำ จึงรันซ้ำกี่รอบก็ได้
/// · เฉพาะวันนัด ∈ [วันนี้, วันนี้+7] — **ไม่แจ้งย้อนหลัง** รถที่เลยกำหนดไปแล้วตอนเปิดใช้ครั้งแรก (ดูจากตัวเลขเมนู/รายงาน)
/// · ใช้กติกาเลือกรถเดียวกับรายงาน (<see cref="ServiceDueRows"/>): ไม่แจ้งรถที่มีจ๊อบเปิดอยู่/กลับมาเข้าอู่แล้ว
/// · ผู้กระทำคือ "ระบบ" (ScopedCurrentUser ของสาขา) — ผู้เรียกต้องเป็นคนสร้าง scope ที่ชี้ shard/สาขาให้ถูก
///   และ publisher ไม่ save เอง จึง save ที่นี่ครั้งเดียวต่อสาขา
/// </summary>
public sealed class ServiceDueNotifier(
    IReportsRepository reports,
    INotificationRepository notificationRepository,
    INotificationPublisher publisher,
    ICurrentUser user,
    TimeProvider clock) : IServiceDueNotifier
{
    /// <summary>ผู้รับ: ระบบไม่มี role "SA" — ใช้หน้าร้านแทน (ยืนยันแล้ว) · ธุรการดูรายงาน/ตัวเลขเมนูได้แต่ไม่ได้รับแจ้งเตือน</summary>
    public static readonly UserRole[] Audience = [UserRole.FrontDesk, UserRole.Manager];

    public const string EntityType = "ServiceDue";
    public const string LinkHint = "service-due";

    public async Task<int> NotifyBranchAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(7));
        var to = today.AddDays(ReportsService.ServiceDueNotifyDays);

        var rows = ServiceDueRows.Build(
            await reports.GetServiceDueAsync(user.ShardKey, user.BranchId, today, to, ct), today);
        if (rows.Count == 0) return 0;

        var keys = rows.ToDictionary(r => NotificationSubjects.ServiceDue(r.VehicleId, r.NextServiceDueOn));
        var existing = await notificationRepository.GetExistingSubjectKeysAsync(
            user.ShardKey, user.BranchId, keys.Keys.ToList(), ct);

        var created = 0;
        foreach (var (key, row) in keys.Where(k => !existing.Contains(k.Key)))
        {
            await publisher.ToRolesAsync(Build(row, key), Audience, ct);
            created++;
        }

        if (created > 0) await notificationRepository.SaveChangesAsync(ct);
        return created;
    }

    internal static NotificationDraft Build(ServiceDueItemDto row, string subjectKey)
    {
        var when = row.DaysUntilDue == 0 ? "ครบกำหนดวันนี้" : $"อีก {row.DaysUntilDue} วัน";
        // รายการที่ครบในหน้าต่างแจ้งเตือน (ไม่รวมรายการที่นัดไกลกว่า 7 วัน — ยังไม่ถึงเวลาตาม)
        var due = row.Items.Where(i => i.DaysUntilDue <= ReportsService.ServiceDueNotifyDays)
            .Select(i => $"{i.Name} ({i.NextServiceDueOn.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)})");
        var body = $"{row.CustomerName}" +
            (string.IsNullOrWhiteSpace(row.CustomerPhone) ? "" : $" · {row.CustomerPhone}") +
            $" · รายการที่ครบ: {string.Join(", ", due)}";

        return new NotificationDraft(
            NotificationKinds.ServiceDue,
            $"รถ {row.VehicleRegistration} ใกล้ครบรอบบริการ ({when})",
            body,
            EntityType, EntityId: null, JobId: null, LinkHint: LinkHint, SubjectKey: subjectKey);
    }
}
