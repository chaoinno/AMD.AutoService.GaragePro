namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// ใบกำกับภาษีเต็มรูปของงานซ่อม — เลข IV-{yy}-{seq:D4} แยกชุดจากใบเสร็จ RC- (OQ#6 ตามที่ต้นแบบสมมติไว้ ยืนยันกับผู้ใช้ 2026-10-02)
///
/// [BIZ] ออกได้หลังออกใบเสร็จแล้วเท่านั้น (จ่ายครบ ⟹ ยอดไม่เปลี่ยนอีก เพราะ QUOTE_RECEIPT_ISSUED ล็อกใบเสนอราคาของจ๊อบ)
/// และงานต้องคิด VAT · 1 job = 1 ใบ (ไม่มี void/ออกใหม่ — OQ#7 ยังไม่ตอบ)
/// ข้อมูลผู้ขาย/ผู้ซื้อ/รายการ **เก็บเป็นสำเนา ณ ตอนออก** — เป็นเอกสารภาษี ห้ามเปลี่ยนตามข้อมูลหลักที่แก้ทีหลัง
/// และไม่เขียนข้อมูลผู้ซื้อกลับไปที่ลูกค้าใน Garage legacy (read-only)
/// </summary>
public class TaxInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public Guid ReceiptId { get; set; }
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public string DocumentNo { get; set; } = "";

    public string SellerName { get; set; } = "";
    public string? SellerAddress { get; set; }
    public string SellerTaxId { get; set; } = "";
    public string? SellerPhone { get; set; }

    public string BuyerName { get; set; } = "";
    public string BuyerAddress { get; set; } = "";
    /// <summary>เลขประจำตัวผู้เสียภาษี 13 หลัก — บุคคลธรรมดาที่ไม่ได้จด VAT เว้นว่างได้</summary>
    public string? BuyerTaxId { get; set; }
    /// <summary>"00000" = สำนักงานใหญ่ · เลข 5 หลักอื่น = สาขา · null = ไม่ระบุ (บุคคลธรรมดา)</summary>
    public string? BuyerBranchNo { get; set; }

    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }

    public long IssuedByUserId { get; set; }
    public string IssuedByName { get; set; } = "";
    public DateTime IssuedAt { get; set; }

    public List<TaxInvoiceLine> Lines { get; set; } = [];
}

/// <summary>รายการสินค้า/บริการบนใบกำกับภาษี — สำเนาบรรทัดที่ลูกค้าอนุมัติจากทุกใบเสนอราคาที่ใช้อยู่</summary>
public class TaxInvoiceLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaxInvoiceId { get; set; }
    public int Sequence { get; set; }
    public string QuotationCode { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    /// <summary>ส่วนลด + โปรโมชันของบรรทัด รวมกัน</summary>
    public decimal DiscountAmount { get; set; }
    public decimal NetAmount { get; set; }
}

/// <summary>ตัวนับเลขใบกำกับภาษี IV-{yy}-{seq:D4} ต่อ (ชาร์ด, สาขา, ปี) — แยกจาก ReceiptNumberCounter ให้เลขเรียงต่อเนื่องไม่ข้าม</summary>
public class TaxInvoiceNumberCounter
{
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public int Year { get; set; }
    public int LastSequence { get; set; }
}
