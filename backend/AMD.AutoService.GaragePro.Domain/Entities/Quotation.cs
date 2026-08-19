using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// ใบเสนอราคา — version-first ตั้งแต่ต้น
/// [BIZ] ออกเวอร์ชันใหม่ → เวอร์ชันเดิมเป็น Superseded และการอนุมัติเดิมเป็นโมฆะ
/// อ้างอิง: docs/01-workflow.md §3.3 · docs/02-domain-model.md
/// </summary>
public class Quotation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>เลขที่เอกสาร เช่น QT-7042-01 (เลขงาน + เวอร์ชัน 2 หลัก)</summary>
    public string Code { get; set; } = string.Empty;

    public int Version { get; set; } = 1;
    public QuotationStatus Status { get; set; } = QuotationStatus.Draft;

    // ---- ผูกกลับ legacy: ต้องเป็น composite เสมอ เพราะ Id ไม่ unique ข้าม shard ----
    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }
    public long LegacyJobId { get; set; }

    // ---- snapshot ข้อมูลหัวเอกสาร ณ เวลาที่ออก (ห้ามพึ่ง legacy ตอนพิมพ์ย้อนหลัง) ----
    public string JobNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string? CustomerTaxId { get; set; }
    public string? CustomerAddress { get; set; }
    public string VehicleRegistration { get; set; } = string.Empty;
    public string? VehicleModel { get; set; }
    public string? VehicleVin { get; set; }
    public int? VehicleMileage { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public string? BranchAddress { get; set; }
    public string? BranchTaxId { get; set; }
    public string? BranchPhone { get; set; }

    // ---- เงิน (คำนวณจาก line เสมอ เก็บไว้เพื่อ query/report) ----
    public decimal GrossAmount { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal PromotionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; } = 0.07m;
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal DepositAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public decimal TotalCost { get; set; }

    // ---- versioning ----
    public Guid? SupersedesQuotationId { get; set; }
    public Guid? SupersededByQuotationId { get; set; }
    /// <summary>เหตุผลที่ออกเวอร์ชันใหม่ — [UI] ต้องแสดงบนหัวเอกสารฉบับแก้ไข</summary>
    public string? RevisionReason { get; set; }

    // ---- concurrent edit ([UI] ขอสิทธิ์แก้คนเดียว) ----
    public long? LockedByUserId { get; set; }
    public string? LockedByUserName { get; set; }
    public DateTime? LockedAt { get; set; }

    public DateTime? ValidUntil { get; set; }
    public DateTime? SentAt { get; set; }

    public long CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long? LastUpdatedByUserId { get; set; }
    public DateTime? LastUpdatedAt { get; set; }

    /// <summary>optimistic concurrency — 409 + QUOTE_VERSION_STALE</summary>
    public byte[]? RowVersion { get; set; }

    public List<QuotationLine> Lines { get; set; } = [];
    public QuotationApproval? Approval { get; set; }

    public bool IsEditable => Status is QuotationStatus.Draft;

    /// <summary>หมดอายุแล้วหรือยัง — [UI] แสดงป้าย "หมดอายุ" บนหัวเอกสาร</summary>
    public bool IsExpired(DateTime nowUtc) =>
        ValidUntil.HasValue && nowUtc > ValidUntil.Value && Status is QuotationStatus.Sent;
}
