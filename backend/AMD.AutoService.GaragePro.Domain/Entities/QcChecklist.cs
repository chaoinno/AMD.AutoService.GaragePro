using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// เช็คลิสต์ตรวจสอบคุณภาพ (QC) — รายการมาจากบรรทัดที่ลูกค้าอนุมัติในใบเสนอราคาปัจจุบันของ job นี้โดยตรง
/// (ไม่ใช่ template ตายตัวแบบ IntakeChecklist) เพราะ QC ต้องตรวจงานที่ซ่อมจริงของ job นั้น ไม่ใช่หัวข้อทั่วไป
/// [BIZ] ไม่มี process ตีกลับ (คำขอผู้ใช้ 2026-09-09) — ผ่านอย่างเดียว ถ้ายังไม่ผ่านไปแจ้งช่างแก้นอกระบบ
/// แล้วย้อนกลับมาติ๊กผ่านทีหลัง
/// </summary>
public class QcChecklist
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public long CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    // ---- ผลทดลองขับ — เงื่อนไขคงที่ของ QC ทุกงาน ไม่ผูกกับบรรทัดซ่อมรายการใดรายการหนึ่ง ----
    public decimal? TestDriveKm { get; set; }
    public string? TestDriveNote { get; set; }
    public DateTime? TestDriveRecordedAt { get; set; }
    public long? TestDriveRecordedByUserId { get; set; }
    public string? TestDriveRecordedByUserName { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public long? SubmittedByUserId { get; set; }
    public string? SubmittedByUserName { get; set; }
    public bool IsLocked => SubmittedAt.HasValue;

    public List<QcChecklistItem> Items { get; set; } = [];
}

public class QcChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QcChecklistId { get; set; }
    public QcChecklist? QcChecklist { get; set; }

    /// <summary>บรรทัดในใบเสนอราคาที่ใช้สร้างรายการนี้ — เก็บไว้อ้างอิงเท่านั้น ไม่ใช่ live FK
    /// (snapshot โค้ด/ชื่อ/ประเภทด้านล่างกันปัญหาถ้าใบเสนอราคาถูกแก้ทีหลัง — เหมือนหลักการ version-first เดิม)</summary>
    public Guid QuotationLineId { get; set; }

    public string CatalogCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LineType Type { get; set; }

    public QcItemResult Result { get; set; } = QcItemResult.Pending;
    public string? Note { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedByUserId { get; set; }
    public string? UpdatedByUserName { get; set; }
}
