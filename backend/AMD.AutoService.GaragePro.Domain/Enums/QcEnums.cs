namespace AMD.AutoService.GaragePro.Domain.Enums;

/// <summary>
/// ผลตรวจ QC ต่อรายการ — ไม่มีสถานะ "ไม่ผ่าน" ตามคำขอผู้ใช้ (2026-09-09): ถ้ายังไม่ผ่านให้ไปแจ้งช่างแก้นอกระบบ
/// แล้วย้อนกลับมาติ๊กผ่านทีหลัง ไม่ต้องมี process ตีกลับในระบบ
/// </summary>
public enum QcItemResult
{
    Pending = 0,
    Pass = 1
}
