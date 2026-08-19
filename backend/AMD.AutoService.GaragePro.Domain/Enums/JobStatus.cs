namespace AMD.AutoService.GaragePro.Domain.Enums;

/// <summary>
/// 10 สถานะงานซ่อม — token ตรงกันทั้ง API / Flutter / React
/// อ้างอิง: docs/01-workflow.md §1
/// </summary>
public enum JobStatus
{
    WaitInspect = 1,   // waitinspect  รอตรวจเช็ค
    WaitQuote,         // waitquote    รอเสนอราคา
    WaitApprove,       // waitapprove  รออนุมัติ
    Approved,          // approved     อนุมัติแล้ว
    InProgress,        // inprogress   กำลังซ่อม
    WaitParts,         // waitparts    รออะไหล่
    Qc,                // qc           QC ตรวจสอบ
    Ready,             // ready        พร้อมส่งมอบ
    Completed,         // completed    เสร็จสมบูรณ์
    Cancelled          // cancelled    ยกเลิก
}
