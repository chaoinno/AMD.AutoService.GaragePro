namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// รายการนัดเข้ารับบริการตั้งต้นของสาขา (เช่น "น้ำมันเครื่อง 10,000 กม. / 6 เดือน") — ผู้จัดการตั้งไว้ล่วงหน้า
/// ตอนส่งมอบรถพนักงานเลือกจากรายการนี้แล้วแก้ไมล์/เดือนต่อคันได้ หรือพิมพ์รายการเองก็ได้
/// [BIZ] สร้าง/แก้/เปิดปิด = ผู้จัดการเท่านั้น (แบบเทมเพลตใบเสนอราคา) · ไม่มีการลบ — ปิดใช้งานแทน
/// เพื่อให้ "ยังไม่มีรายการเลย" = สาขาที่ยังไม่เคยเปิดใช้ ใช้ตัดสินใจ seed ชุดตั้งต้นครั้งเดียว
/// </summary>
public class ServiceReminderTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }

    public string Name { get; set; } = string.Empty;
    /// <summary>ไมล์ที่นัดห่างจากไมล์ตอนส่งมอบ (กม.) — เป็นค่าตั้งต้นให้หน้าจอบวกเอง แก้ต่อคันได้</summary>
    public int DefaultMileageInterval { get; set; }
    public int DefaultMonths { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
