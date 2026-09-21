using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// บริบทของผู้ใช้ในคำขอปัจจุบัน — shard + สาขา + บทบาท
/// [BIZ] Legacy Id ไม่ unique ข้าม shard จึงต้องพก ShardKey ไปทุกที่ (docs/05 §1)
/// </summary>
public interface ICurrentUser
{
    long UserId { get; }
    string UserName { get; }
    UserRole Role { get; }
    string ShardKey { get; }
    int BranchId { get; }
    EventSource Source { get; }
    bool IsAdministrator { get; }

    /// <summary>รอบกะปัจจุบัน — null เมื่อยังไม่ได้เลือกสาขา/กะ (token ขั้นแรก)</summary>
    Guid? SessionId { get; }

    /// <summary>
    /// legacy Staff.Id ของผู้ใช้ — คนละค่ากับ <see cref="UserId"/> ซึ่งเป็น User.Id
    /// ต้องใช้ค่านี้เมื่ออ้างถึง "ช่าง" เพราะ QuotationLine.AssignedTechnicianId เก็บ Staff.Id
    ///
    /// default เป็น null เพื่อไม่ให้ stub ในชุดทดสอบทุกไฟล์ต้องแก้ตาม และเพื่อให้ token ที่ออกไป
    /// ก่อนมี claim นี้ยังใช้งานได้ — ผู้เรียกต้องเผื่อกรณี null เสมอ (ดู WorkTimeService.CurrentStaffIdAsync)
    /// </summary>
    long? StaffId => null;

    /// <summary>[BIZ] ต้นทุน/กำไร/คอมมิชชัน เห็นได้เฉพาะผู้จัดการ</summary>
    bool CanSeeCost => Role == UserRole.Manager;
}
