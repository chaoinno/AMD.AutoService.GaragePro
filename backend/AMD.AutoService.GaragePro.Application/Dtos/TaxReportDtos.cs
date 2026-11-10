namespace AMD.AutoService.GaragePro.Application.Dtos;

/// <summary>ผู้ประกอบการ (อู่) ที่หัวรายงานภาษี — BranchNo "00000" = สำนักงานใหญ่ · TaxId null = ข้อมูลสาขาไม่ใช่เลข 13 หลัก</summary>
public sealed record TaxReportPartyDto(string Name, string? Address, string? TaxId, string BranchNo);

/// <summary>
/// แถวรายงานภาษีขาย · Kind: taxInvoice · creditNote (อ้าง IV- = ส่วนที่ 1 · อ้าง RC- = ส่วนที่ 2) · receipt · sale
/// ใบลดหนี้เป็นยอดติดลบ · ReferenceDocumentNo = บิลที่ใบลดหนี้อ้าง
/// </summary>
public sealed record OutputVatRowDto(
    int Sequence,
    string Kind,
    Guid Id,
    Guid? JobId,
    string DocumentNo,
    DateTime IssuedAt,
    string BuyerName,
    string? BuyerTaxId,
    string? BuyerBranchNo,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    string? ReferenceDocumentNo);

/// <summary>
/// รายงานภาษีขายของเดือน (docs/20 §3 · Q1): Rows = รายการตามกฎหมาย (IV- และใบลดหนี้ที่อ้าง IV-)
/// Uninvoiced = รับเงินที่คิด VAT แต่ยังไม่ออกใบกำกับ (RC- ของจ๊อบที่ไม่มี IV- · SL- · ใบลดหนี้ที่อ้าง RC-) แยกไว้ให้บัญชีเห็นยอดครบ
/// </summary>
public sealed record OutputVatReportDto(
    int Year,
    int Month,
    TaxReportPartyDto? Seller,
    IReadOnlyList<OutputVatRowDto> Rows,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    IReadOnlyList<OutputVatRowDto> Uninvoiced,
    decimal UninvoicedNetAmount,
    decimal UninvoicedVatAmount,
    decimal UninvoicedTotalAmount);

public sealed record PurchaseTaxInvoiceDto(
    Guid Id,
    Guid? SupplierId,
    string SupplierName,
    string SupplierTaxId,
    string SupplierBranchNo,
    string InvoiceNo,
    DateOnly InvoiceDate,
    int ClaimYear,
    int ClaimMonth,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    Guid? PurchaseOrderId,
    string? PurchaseOrderNumber,
    Guid? GoodsReceiptId,
    string? GoodsReceiptNumber,
    string? Note,
    string RecordedByName,
    DateTime RecordedAt,
    bool IsVoided,
    DateTime? VoidedAt,
    string? VoidedByName,
    string? VoidReason);

/// <summary>ใบรับสินค้าของ PO ที่มี VAT ที่ยังไม่บันทึกใบกำกับภาษีซื้อ — ตัวช่วยเตือน ไม่ใช่ส่วนของรายงาน</summary>
public sealed record PendingGoodsReceiptDto(
    Guid GoodsReceiptId,
    string Number,
    DateTime ReceivedAt,
    string PurchaseOrderNumber,
    string? SupplierName,
    string? DeliveryNumber,
    decimal NetAmount,
    decimal VatAmount);

/// <summary>รายงานภาษีซื้อของเดือนที่ใช้สิทธิ์ (docs/20 §4) — Rows = ใบที่ใช้อยู่ · Voided แยกไว้ดูย้อนหลัง</summary>
public sealed record InputVatReportDto(
    int Year,
    int Month,
    TaxReportPartyDto? Buyer,
    IReadOnlyList<PurchaseTaxInvoiceDto> Rows,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    IReadOnlyList<PurchaseTaxInvoiceDto> Voided,
    IReadOnlyList<PendingGoodsReceiptDto> PendingGoodsReceipts);

/// <summary>ยอดเติมจากใบรับสินค้า (แก้ได้ก่อนบันทึก) — VAT = ปัดยอดก่อนภาษี × อัตราของ PO ครั้งเดียว (สูตรเดียวกับเอกสารจัดซื้อ)</summary>
public sealed record PurchaseTaxInvoicePrefillDto(
    Guid GoodsReceiptId,
    string GoodsReceiptNumber,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    Guid? SupplierId,
    string? SupplierName,
    string? SupplierTaxId,
    string? DeliveryNumber,
    DateOnly ReceivedDate,
    bool HasVat,
    decimal VatRate,
    decimal NetAmount,
    decimal VatAmount);

public sealed record RecordPurchaseTaxInvoiceRequest(
    Guid RequestId,
    Guid? SupplierId,
    string? SupplierName,
    string? SupplierTaxId,
    string? SupplierBranchNo,
    string? InvoiceNo,
    DateOnly? InvoiceDate,
    int? ClaimYear,
    int? ClaimMonth,
    decimal NetAmount,
    decimal VatAmount,
    Guid? GoodsReceiptId,
    string? Note);

public sealed record VoidPurchaseTaxInvoiceRequest(string? Reason);
