using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// Checklist สภาพรถขณะรับ (ขั้นที่ 3 ของ "รับรถ 6 ขั้น") — 1 งาน มี 1 ชุด
/// [BIZ] ล็อกเป็น read-only เมื่อ SubmittedAt ถูกตั้งค่าแล้ว (เทียบ Inspection §02-domain-model.md:262)
/// อ้างอิง: docs/01-workflow.md §3.1
/// </summary>
public class IntakeChecklist
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    public long CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SubmittedAt { get; set; }
    public long? SubmittedByUserId { get; set; }
    public string? SubmittedByUserName { get; set; }

    public bool IsLocked => SubmittedAt.HasValue;

    public List<IntakeChecklistItem> Items { get; set; } = [];
}

/// <summary>
/// รายการเดียวของ checklist — itemCode ล็อกตาม IntakeChecklistTemplate เท่านั้น (ห้าม client เพิ่มเอง)
/// </summary>
public class IntakeChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid IntakeChecklistId { get; set; }
    public IntakeChecklist? IntakeChecklist { get; set; }

    /// <summary>exterior | wheels | interior | underhood</summary>
    public string CategoryKey { get; set; } = string.Empty;

    /// <summary>เช่น ext1..ext10, whl1..whl3, int1..int4, eng1..eng3 — ดู IntakeChecklistTemplate</summary>
    public string ItemCode { get; set; } = string.Empty;

    public IntakeCheckResult Result { get; set; } = IntakeCheckResult.Pending;

    /// <summary>[BIZ] บังคับกรอกเมื่อ Result = Issue หรือ NotApplicable</summary>
    public string? Note { get; set; }

    public DateTime? UpdatedAt { get; set; }
    public long? UpdatedByUserId { get; set; }
    public string? UpdatedByUserName { get; set; }
}
