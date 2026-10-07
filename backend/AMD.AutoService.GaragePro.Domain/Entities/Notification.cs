using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// แจ้งเตือนในระบบ (กระดิ่งบนเว็บ) — ผู้รับเป็นได้สองแบบ ห้ามปน (บังคับด้วย CHECK ที่ฐานข้อมูล):
/// · รายบุคคล: <see cref="RecipientStaffId"/> (Staff.Id — mention/ช่างเก็บเป็น StaffId และแปลง UserId→StaffId ได้ทางเดียว)
/// · กลุ่มบทบาท: <see cref="AudienceRoles"/> bitmask ของ <see cref="UserRole"/> — ระบบไม่มีทางดึงรายชื่อพนักงานตามบทบาท
///   (role คำนวณจากตำแหน่งใน legacy + override ตอน login) จึงเก็บแถวเดียวแล้วคัดตอนอ่านด้วย role ใน JWT แทน
/// สถานะอ่านแยกต่อคนอยู่ที่ <see cref="NotificationRead"/> ใช้ตารางเดียวกันทั้งสองแบบ
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegacyShardKey { get; set; } = string.Empty;
    public int LegacyBranchId { get; set; }

    /// <summary>ดู <see cref="NotificationKinds"/></summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>ข้อความพร้อมแสดง (snapshot ตอนสร้าง) — ไม่คำนวณใหม่ตอนอ่าน เพื่อไม่ต้องไล่ join ข้ามโมดูล</summary>
    public string TitleTh { get; set; } = string.Empty;
    public string? BodyTh { get; set; }

    public long? RecipientStaffId { get; set; }
    public int AudienceRoles { get; set; }

    /// <summary>ปลายทางเมื่อคลิก — client สร้าง route เองจาก JobId หรือ EntityType+EntityId</summary>
    public Guid? JobId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid? EntityId { get; set; }
    public string? LinkHint { get; set; }

    public long ActorUserId { get; set; }
    /// <summary>ใช้ตัดผู้กระทำออกจากแจ้งเตือนกลุ่มบทบาทตอนอ่าน (null = ระบุไม่ได้ → ไม่ตัด)</summary>
    public long? ActorStaffId { get; set; }
    public string ActorName { get; set; } = string.Empty;

    /// <summary>
    /// กุญแจของ "เรื่อง" ที่ต้องมีคนจัดการ เช่น <c>PO:{id}:pending</c> — เมื่อมีคนจัดการแล้วระบบตั้ง
    /// <see cref="ResolvedAt"/> ให้ทุกแถวที่ยังเปิดของเรื่องนั้น ไม่ใช่ unique key โดยตั้งใจ: ถ้าชน index
    /// จะพา action หลักที่อยู่ใน SaveChanges เดียวกัน rollback ไปด้วย
    /// </summary>
    public string? SubjectKey { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>มีคนดำเนินการเรื่องนี้แล้ว — ไม่นับเป็นยังไม่อ่านอีก แต่ยังแสดงในรายการ</summary>
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByName { get; set; }
}

public class NotificationRead
{
    public Guid NotificationId { get; set; }
    public long StaffId { get; set; }
    public DateTime ReadAt { get; set; }
}

public static class NotificationKinds
{
    public const string ChatMention = "chat.mention";
    public const string QuotationApproved = "quotation.approved";
    public const string QuotationPartial = "quotation.partial";
    public const string QuotationAllRejected = "quotation.all_rejected";
    public const string PurchasePending = "purchase.pending";
    public const string PurchaseApproved = "purchase.approved";
    public const string PurchaseReturned = "purchase.returned";
}

public static class NotificationAudience
{
    public static int Bit(UserRole role) => 1 << (int)role;

    public static int Of(IEnumerable<UserRole> roles) => roles.Aggregate(0, (mask, role) => mask | Bit(role));
}
