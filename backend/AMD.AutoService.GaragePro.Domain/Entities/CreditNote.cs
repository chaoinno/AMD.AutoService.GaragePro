using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>บิลที่ใบลดหนี้อ้างถึง — docs/20 §2.1</summary>
public enum CreditNoteSourceType
{
    /// <summary>ใบกำกับภาษีเต็มรูป IV- → ใบลดหนี้ทางภาษี (ม.86/10)</summary>
    TaxInvoice = 1,

    /// <summary>ใบเสร็จงานซ่อม RC- ของจ๊อบที่ไม่ได้ออกใบกำกับ → ใบลดหนี้ที่ไม่ใช่เอกสารทางภาษี</summary>
    Receipt = 2
}

/// <summary>เหตุผลตายตัว 5 แบบ (กรณีลดหนี้ตามมาตรา 82/10) — Other ต้องมีข้อความเสมอ</summary>
public enum CreditNoteReason
{
    DefectOrMismatch = 1,
    ReturnedGoods = 2,
    ServiceNotAsAgreed = 3,
    Overcharged = 4,
    Other = 5
}

/// <summary>
/// ใบลดหนี้ CN-{yy}-NNNN (เพิ่ม 2026-11-09 · weekly meeting 2026-11-06 · docs/20)
/// [BIZ] ออกได้เฉพาะผู้จัดการ · ต้องอ้างบรรทัดของบิล (IV- หรือ RC-) · ยกเลิก/แก้ไม่ได้ ·
/// ผู้ขาย/ผู้ซื้อ/บรรทัดเก็บเป็นสำเนา ณ ตอนออก (เหมือนใบกำกับ) · ไม่แตะ Payment/Receipt/สต็อกเดิม
/// </summary>
public class CreditNote
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public string DocumentNo { get; set; } = "";
    /// <summary>สำเนาจากจ๊อบ — รายงานไม่ต้อง join ข้ามตาราง</summary>
    public string JobNo { get; set; } = "";
    public string VehicleRegistration { get; set; } = "";

    public CreditNoteSourceType SourceType { get; set; }
    public Guid ReceiptId { get; set; }
    public Guid? TaxInvoiceId { get; set; }
    /// <summary>เลขบิลที่อ้าง (IV-… หรือ RC-…) และวันที่ของบิลนั้น — ช่องบังคับของใบลดหนี้</summary>
    public string SourceDocumentNo { get; set; } = "";
    public DateTime SourceIssuedAt { get; set; }

    public CreditNoteReason ReasonCode { get; set; }
    public string? ReasonText { get; set; }

    public string SellerName { get; set; } = "";
    public string? SellerAddress { get; set; }
    public string? SellerTaxId { get; set; }
    public string? SellerBranchNo { get; set; }
    public string BuyerName { get; set; } = "";
    public string? BuyerAddress { get; set; }
    public string? BuyerTaxId { get; set; }
    public string? BuyerBranchNo { get; set; }

    /// <summary>มูลค่า (ก่อน VAT) ตามบิลเดิม · มูลค่าที่ถูกต้อง (หลังหักใบลดหนี้ทุกใบรวมใบนี้) · ผลต่าง = ยอดของใบนี้</summary>
    public decimal OriginalNetAmount { get; set; }
    public decimal CorrectNetAmount { get; set; }
    public decimal DiffNetAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public long IssuedByUserId { get; set; }
    public string IssuedByName { get; set; } = "";
    public DateTime IssuedAt { get; set; }

    public List<CreditNoteLine> Lines { get; set; } = [];
    public List<CreditNoteRefund> Refunds { get; set; } = [];
}

public class CreditNoteLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreditNoteId { get; set; }
    public int Sequence { get; set; }
    /// <summary>TaxInvoiceLine.Id (อ้าง IV) หรือ QuotationLine.Id (อ้าง RC)</summary>
    public Guid SourceLineId { get; set; }
    public string QuotationCode { get; set; } = "";
    public string Description { get; set; } = "";
    public string Unit { get; set; } = "";
    public decimal OriginalQuantity { get; set; }
    public decimal OriginalNetAmount { get; set; }
    /// <summary>จำนวนที่ลด (คืนสินค้า) — 0 = ลดเป็นยอดเงินอย่างเดียว (ลดราคา)</summary>
    public decimal Quantity { get; set; }
    /// <summary>ยอดที่ลด ก่อน VAT</summary>
    public decimal Amount { get; set; }
}

/// <summary>คืนเงินตามใบลดหนี้ — คืนได้หลายครั้ง รวมไม่เกินยอดใบลดหนี้ · ลบไม่ได้ (หลักฐานเงินออก)</summary>
public class CreditNoteRefund
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CreditNoteId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public Guid RequestId { get; set; }
    public long RefundedByUserId { get; set; }
    public string RefundedByName { get; set; } = "";
    public DateTime RefundedAt { get; set; }
}

public class CreditNoteNumberCounter
{
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public int Year { get; set; }
    public int LastSequence { get; set; }
}

/// <summary>
/// ค่าตั้งภาษีของสาขา (docs/20 Q6) — Garage DB เดิมไม่มี "สำนักงานใหญ่/สาขาที่" ของผู้ขาย
/// ไม่มีแถว = สำนักงานใหญ่ (00000) · ผู้จัดการแก้ได้
/// </summary>
public class BranchTaxProfile
{
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    /// <summary>"00000" = สำนักงานใหญ่ · เลข 5 หลักอื่น = สาขาที่</summary>
    public string SellerBranchNo { get; set; } = "00000";
    public string UpdatedByName { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
