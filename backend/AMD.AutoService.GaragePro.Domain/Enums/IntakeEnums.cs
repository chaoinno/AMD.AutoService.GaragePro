namespace AMD.AutoService.GaragePro.Domain.Enums;

/// <summary>
/// ผลตรวจแต่ละข้อของ checklist สภาพรถขณะรับ (ขั้นที่ 3 ของ "รับรถ 6 ขั้น")
/// ต่างจาก InspectionResult (ok/watch/fix/na) ของช่างเทคนิค — หน้าร้านบันทึกแค่
/// "ปกติ" หรือ "พบสภาพที่ต้องบันทึกไว้" เพื่อกันข้อพิพาทตอนส่งมอบ ไม่ใช่การวินิจฉัยซ่อม
/// </summary>
public enum IntakeCheckResult
{
    /// <summary>ยังไม่ได้ตรวจ — ค่าเริ่มต้น</summary>
    Pending = 0,
    Ok = 1,
    Issue = 2,

    /// <summary>ไม่เกี่ยวข้องกับรถคันนี้ (เช่น ไม่มีกล้องหน้ารถติดมา) — [BIZ] ต้องมี Note เสมอ</summary>
    NotApplicable = 3
}
