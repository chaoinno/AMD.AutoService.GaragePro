namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// ยืนยันส่งมอบรถ — [ASSUME] stopgap ชั่วคราวบนเว็บ (Office/Manager/Cashier ยืนยันแทนลูกค้า)
/// เพราะ /handover/:jobId บนมือถือจริงยังไม่ได้ออกแบบ (docs/01-workflow.md §11 [GAP·สูง], Phase 8)
/// เติมของที่ docs/02-domain-model.md วางเป็น placeholder ไว้เฉยๆ — รายการเช็คลิสต์เป็น const list คงที่
/// ไม่ใช่ template เหมือน IntakeChecklist เพราะเป็นของชั่วคราว รอแทนที่ด้วย flow มือถือจริงในอนาคต
/// </summary>
public class HandoverRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public long CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = "";
    public DateTime CreatedAt { get; set; }

    public string? SignatureImagePath { get; set; }

    // [BIZ] เพิ่ม 2026-10-08 — เลขไมล์ตอนส่งมอบ + นัดเข้ารับบริการครั้งถัดไป (บังคับก่อนเซ็นส่งมอบ)
    // พิมพ์ลงใบส่งมอบรถ และเป็นแหล่งข้อมูลของรายงานรถใกล้ครบรอบบริการ (/reports/service-due)
    public int? MileageAtHandover { get; set; }

    // [เลิกใช้ 2026-10-25] นัดครั้งถัดไปเปลี่ยนเป็นหลายรายการใน ServiceItems (svc_HandoverServiceItem) — 3 คอลัมน์นี้
    // คงไว้ไม่ drop (แบบ Quotation.DepositAmount) migration AddHandoverServiceItems คัดค่าเดิมไปเป็นรายการ "เช็คระยะ"
    // แล้ว · โค้ดใหม่ไม่เขียน ไม่อ่านอีก (HandoverDto เติมฟิลด์เดิมจากรายการที่ครบเร็วที่สุดให้แอปรุ่นเก่า)
    public int? NextServiceMileage { get; set; }
    public int? NextServiceMonths { get; set; }
    public DateOnly? NextServiceDueOn { get; set; }
    public DateTime? ServiceInfoUpdatedAt { get; set; }
    public string? ServiceInfoUpdatedByUserName { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public long? SubmittedByUserId { get; set; }
    public string? SubmittedByUserName { get; set; }
    public bool IsLocked => SubmittedAt.HasValue;

    public List<HandoverChecklistItem> Items { get; set; } = [];

    /// <summary>[BIZ] นัดเข้ารับบริการครั้งถัดไปได้หลายรายการ (น้ำมันเครื่อง/กรอง/เบรก…) — ต้องมีอย่างน้อย 1 รายการก่อนเซ็นส่งมอบ</summary>
    public List<HandoverServiceItem> ServiceItems { get; set; } = [];
}

/// <summary>
/// หนึ่งรายการนัดเข้ารับบริการครั้งถัดไป — ชื่อเก็บเป็นสำเนา ณ ตอนบันทึก (TemplateId อ้างอิงเฉยๆ ไม่มี FK
/// ผู้จัดการแก้ชื่อ/ปิดรายการตั้งต้นทีหลังต้องไม่ย้อนไปเปลี่ยนใบส่งมอบที่ออกแล้ว)
/// </summary>
public class HandoverServiceItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HandoverRecordId { get; set; }
    public HandoverRecord? HandoverRecord { get; set; }

    public Guid? TemplateId { get; set; }
    public string Name { get; set; } = "";
    public int NextServiceMileage { get; set; }
    /// <summary>จำนวนเดือนที่ผู้ใช้กรอก — เก็บไว้คำนวณวันนัดใหม่จากวันส่งมอบจริงตอนเซ็น</summary>
    public int NextServiceMonths { get; set; }
    /// <summary>วันนัด (วันที่ตามเวลาไทย) = วันส่งมอบ + NextServiceMonths — ก่อนเซ็นเป็นค่าพรีวิวจากวันที่บันทึก</summary>
    public DateOnly NextServiceDueOn { get; set; }
    public int SortOrder { get; set; }
}

public class HandoverChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid HandoverRecordId { get; set; }
    public HandoverRecord? HandoverRecord { get; set; }

    public string ItemCode { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>[BIZ] ต้องมี Note เสมอเมื่อ IsReturned = false (มิเรอร์กฎ Note บังคับของ Intake invariant #12)</summary>
    public bool IsReturned { get; set; }
    public string? Note { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedByUserId { get; set; }
    public string? UpdatedByUserName { get; set; }
}
