namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// ใบกำกับภาษีซื้อที่ได้รับจากผู้ขาย (docs/20 §4 · เพิ่ม 2026-11-09) — ใช้ทำรายงานภาษีซื้อ
///
/// [BIZ] Q4: บันทึกได้ทั้งจากใบรับสินค้า (GRN — ระบบเติมยอดให้แก้ได้) และบันทึกเองสำหรับค่าใช้จ่ายนอกระบบจัดซื้อ
/// Q5: เลือก "เดือนภาษีที่ใช้สิทธิ์" ได้ ตั้งแต่เดือนของวันที่ใบกำกับจนถึง 6 เดือนถัดไป (มาตรา 82/3)
/// ข้อมูลผู้ขายเก็บเป็นสำเนา ณ ตอนบันทึก · ลบไม่ได้ ยกเลิกพร้อมเหตุผลเท่านั้น (ยกเลิกแล้วบันทึกเลขเดิมใหม่ได้)
/// </summary>
public class PurchaseTaxInvoice
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }

    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    /// <summary>เลขประจำตัวผู้เสียภาษีของผู้ขาย 13 หลัก (บังคับ — รายงานภาษีซื้อต้องมี)</summary>
    public string SupplierTaxId { get; set; } = "";
    /// <summary>"00000" = สำนักงานใหญ่ · เลข 5 หลักอื่น = สาขาของผู้ขาย</summary>
    public string SupplierBranchNo { get; set; } = "00000";

    public string InvoiceNo { get; set; } = "";
    /// <summary>วันที่ตามใบกำกับ (ปฏิทินไทย)</summary>
    public DateOnly InvoiceDate { get; set; }
    /// <summary>เดือนภาษีที่ใช้สิทธิ์ เก็บเป็น yyyyMM (ค.ศ.) — ใช้กรองรายงาน</summary>
    public int ClaimPeriod { get; set; }

    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }

    public Guid? PurchaseOrderId { get; set; }
    public string? PurchaseOrderNumber { get; set; }
    public Guid? GoodsReceiptId { get; set; }
    public string? GoodsReceiptNumber { get; set; }
    public string? Note { get; set; }

    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public long RecordedByUserId { get; set; }
    public string RecordedByName { get; set; } = "";
    public DateTime RecordedAt { get; set; }

    public DateTime? VoidedAt { get; set; }
    public string? VoidedByName { get; set; }
    public string? VoidReason { get; set; }
}
