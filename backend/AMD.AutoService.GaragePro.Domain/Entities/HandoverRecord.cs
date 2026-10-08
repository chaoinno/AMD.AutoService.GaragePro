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
    public int? NextServiceMileage { get; set; }
    /// <summary>ระยะเวลาที่ผู้ใช้กรอก (เดือน) — เก็บไว้เพื่อคำนวณวันนัดใหม่จากวันที่ส่งมอบจริงตอนเซ็น</summary>
    public int? NextServiceMonths { get; set; }
    /// <summary>วันนัดครั้งถัดไป (วันที่ตามเวลาไทย) = วันส่งมอบ + NextServiceMonths — ก่อนเซ็นเป็นค่าพรีวิวจากวันที่บันทึก</summary>
    public DateOnly? NextServiceDueOn { get; set; }
    public DateTime? ServiceInfoUpdatedAt { get; set; }
    public string? ServiceInfoUpdatedByUserName { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public long? SubmittedByUserId { get; set; }
    public string? SubmittedByUserName { get; set; }
    public bool IsLocked => SubmittedAt.HasValue;

    public List<HandoverChecklistItem> Items { get; set; } = [];
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
