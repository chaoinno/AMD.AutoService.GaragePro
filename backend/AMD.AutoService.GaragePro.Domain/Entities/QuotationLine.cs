using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// บรรทัดในใบเสนอราคา — ลูกค้าอนุมัติ/ไม่อนุมัติ "รายบรรทัด"
/// [BIZ] เฉพาะบรรทัดที่ Approved เท่านั้นที่เข้าสู่การซ่อมและการเรียกเก็บเงิน
/// </summary>
public class QuotationLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }

    /// <summary>ลำดับการแสดงผลในเอกสาร</summary>
    public int Sequence { get; set; }

    public string CatalogCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public LineType Type { get; set; }
    /// <summary>[UI] แยกกลุ่ม "ลูกค้าขอ" vs "ช่างแนะนำ" บนหน้าอนุมัติ</summary>
    public LineSource Source { get; set; }

    /// <summary>บรรทัดนี้มาจากข้อบกพร่องข้อไหนในใบตรวจเช็ค (ถ้ามี)</summary>
    public Guid? InspectionItemId { get; set; }

    public decimal Quantity { get; set; } = 1;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    /// <summary>ต้นทุน — [BIZ] strip ออกที่ serializer ตาม role ไม่ใช่ซ่อนที่ client</summary>
    public decimal UnitCost { get; set; }

    /// <summary>ส่วนลดเป็นเปอร์เซ็นต์ 0–100 · [ASSUME] เกิน 10% ต้องผู้จัดการอนุมัติ</summary>
    public decimal DiscountPercent { get; set; }
    public PromotionKind Promotion { get; set; } = PromotionKind.None;

    public long? AssignedTechnicianId { get; set; }
    public string? AssignedTechnicianName { get; set; }
    public string? Note { get; set; }

    /// <summary>ชั่วโมงมาตรฐาน (เฉพาะ Type = Labor)</summary>
    public decimal? StandardHours { get; set; }

    // ---- ผลการตัดสินใจของลูกค้า ----
    public LineApprovalStatus ApprovalStatus { get; set; } = LineApprovalStatus.Pending;
    /// <summary>[BIZ] ไม่อนุมัติต้องเลือกเหตุผล</summary>
    public string? RejectReason { get; set; }
    public DateTime? DecidedAt { get; set; }

    // ---- ยอดที่คำนวณแล้ว (เก็บ snapshot ไว้เพื่อ report/พิมพ์ย้อนหลัง) ----
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal PromotionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal CostAmount { get; set; }
    public decimal MarginAmount { get; set; }

    public Quotation? Quotation { get; set; }
}

/// <summary>โปรโมชัน — ค่าตรงกับ prototype (GaragePro Quotation.dc.html)</summary>
public enum PromotionKind
{
    None = 0,           // ไม่มีโปรโมชัน
    LoyalCustomer = 1,  // ลูกค้าประจำ −5%
    BrakeSet = 2,       // โปรเบรกครบชุด −300
    InsurancePartner = 3 // ประกันคู่สัญญา −10%  (ต้องผู้จัดการอนุมัติ)
}
