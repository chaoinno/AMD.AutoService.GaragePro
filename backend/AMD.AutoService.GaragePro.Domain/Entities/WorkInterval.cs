using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// ช่วงเวลาที่ช่างลงมือทำงานหนึ่งช่วง — แหล่งข้อมูลดิบของรายงานประเมินประสิทธิภาพช่าง
/// (docs/09-technician-time-tracking.md §4) ไม่ผูกกับเงินหรือคอมมิชชัน (ตัดสินใจ 2026-09-21)
///
/// [BIZ] invariant หลักคือ "หนึ่งช่วงเปิดต่อ<b>ช่าง</b>" ไม่ใช่ต่อจ๊อบ — บังคับด้วย filtered unique index
/// `UX_WorkInterval_OneOpenPerTech` ที่ WorkIntervalConfiguration ไม่ใช่แค่ที่ service
/// ผลคือช่างหลายคนรุมคันเดียวพร้อมกันได้โดยธรรมชาติ ตามที่ผู้ใช้ยืนยัน (docs/09 §5.3)
/// ถ้าเปิดซ้อนกันได้เมื่อไหร่ ชั่วโมงจะถูกนับซ้ำและทุกเมตริกใน §5 พังพร้อมกัน
///
/// แก้ไขย้อนหลังได้ (ต่างจากฉบับแรกที่วางเป็น append-only เพราะจะผูกเงิน) แต่ <b>ลบจริงไม่ได้</b> —
/// ใช้ VoidedAt แทน เพราะถ้าลบแถวได้จะแยกไม่ออกว่าคาบที่หายไปคือ "ไม่เคยมี" หรือ "ถูกลบ"
/// แล้วคำนวณ "ความครบของข้อมูล" (docs/09 §2.4) ไม่ได้
/// </summary>
public class WorkInterval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }

    public Guid JobId { get; set; }

    /// <summary>เผื่ออนาคตถ้ายกระดับไปจับเวลารายบรรทัดใบเสนอราคา — v1 เป็น null เสมอ (docs/09 §11)</summary>
    public Guid? RepairTaskId { get; set; }

    /// <summary>
    /// legacy Staff.Id ไม่ใช่ User.Id — ต้องตรงกับ QuotationLine.AssignedTechnicianId
    /// ไม่งั้นตัวหารของเมตริกประสิทธิภาพ join ไม่ติด
    /// </summary>
    public long TechnicianStaffId { get; set; }
    public string TechnicianName { get; set; } = "";

    public WorkIntervalKind Kind { get; set; }
    public DateTime StartedAt { get; set; }

    /// <summary>null = กำลังเปิดอยู่ — คอลัมน์นี้คือ filter ของ unique index</summary>
    public DateTime? EndedAt { get; set; }
    public WorkEndReason? EndReason { get; set; }

    /// <summary>กะที่ช่างเปิดอยู่ตอนเริ่ม — ใช้หาเพดานตัดคาบที่ลืมปิด · null เมื่อเปิดจากเว็บซึ่งไม่มีกะ</summary>
    public Guid? ShiftSessionId { get; set; }

    /// <summary>
    /// [BIZ] คาบนี้เป็นการกลับมาแก้งานหลังจ๊อบเข้า QC แล้ว — ติดธงตอนเปิดคาบ ไม่ใช่คำนวณตอนอ่าน
    /// เป็นเมตริกคุณภาพหลักที่วัดเป็น "ชั่วโมง" ได้โดยไม่ต้องมีสถานะ "QC ไม่ผ่าน" (docs/09 §5.5)
    /// ทางเลือกคือย้อนอ่าน ActivityEvent.PayloadJson ซึ่งไม่มี index และต้องหา event แรกที่เข้า Qc
    /// ไม่ใช่ล่าสุด (จ๊อบเข้า-ออก qc ได้หลายรอบ) — เปราะและแพงกว่ามาก
    /// </summary>
    public bool IsRework { get; set; }

    /// <summary>
    /// ระบบปิดให้เองเพราะลืมกดหยุด — ไม่เข้าการคำนวณเมตริกใดๆ แต่ทำให้ "ความครบของข้อมูล" ลดลงจริง
    /// ปลดธงนี้เมื่อมีคนแก้เวลาให้ถูกต้องทีหลัง (docs/09 §5.6)
    /// </summary>
    public bool IsAutoCapped { get; set; }

    /// <summary>
    /// คาบเดิมที่ถูกปิดไปพร้อมกับการเปิดคาบนี้ — เก็บไว้เพื่อให้คำขอที่ถูก replay ด้วย RequestId เดิม
    /// คืนค่า "หยุดเวลาคันเดิมแล้ว" ตัวเดิมได้ ไม่งั้นข้อความยืนยันในแอปจะหายหรือผิดตอนกดลองใหม่
    /// </summary>
    public Guid? ClosedPreviousIntervalId { get; set; }

    /// <summary>กันกดรัวๆ ไม่ให้ได้หลายช่วง — unique ระดับ global แบบเดียวกับ Payment (invariant #8)</summary>
    public Guid RequestId { get; set; }

    public long CreatedByUserId { get; set; }
    public long? EndedByUserId { get; set; }

    /// <summary>null = ไม่เคยถูกแก้ · ผู้ที่แก้ต้องเป็น Lead/Manager และแก้ได้ภายในกรอบเวลาที่ตั้งไว้</summary>
    public long? EditedByUserId { get; set; }
    public DateTime? EditedAt { get; set; }
    public string? EditReason { get; set; }

    /// <summary>ยกเลิกคาบที่ผิดชัดเจน — soft delete เท่านั้น ดูเหตุผลที่หัวคลาส</summary>
    public DateTime? VoidedAt { get; set; }
    public long? VoidedByUserId { get; set; }
    public string? VoidReason { get; set; }

    public bool IsOpen => EndedAt is null;
    public TimeSpan? Duration => EndedAt is null ? null : EndedAt.Value - StartedAt;
}
