using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>ใบเสร็จงานซ่อมที่คิด VAT แต่จ๊อบยังไม่มีใบกำกับภาษี — ส่วนที่ 2 ของรายงานภาษีขาย</summary>
public sealed record UninvoicedReceiptRow(
    Guid ReceiptId, Guid JobId, string DocumentNo, DateTime IssuedAt, string JobNo, string CustomerName,
    decimal NetAmount, decimal VatAmount, decimal TotalAmount);

/// <summary>บิลขายหน้าร้านที่ชำระแล้ว (ไม่ถูกยกเลิก) และคิด VAT</summary>
public sealed record VatSaleRow(
    Guid SaleId, string DocumentNo, DateTime IssuedAt, string? CustomerName,
    decimal NetAmount, decimal VatAmount, decimal TotalAmount);

/// <summary>ใบรับสินค้าของ PO ที่มี VAT — ยอดก่อนภาษี = Σ(จำนวนรับดี + ชำรุด) × ราคาต่อหน่วยก่อนภาษีในใบรับ</summary>
public sealed record GoodsReceiptVatRow(
    Guid GoodsReceiptId, string Number, DateTime ReceivedAt, string? DeliveryNumber,
    Guid PurchaseOrderId, string PurchaseOrderNumber, Guid? SupplierId, string? SupplierName,
    bool HasVat, decimal VatRate, decimal NetAmount);

/// <summary>รายงานภาษีขาย/ภาษีซื้อ + ใบกำกับภาษีซื้อ (docs/20 §3–4) — ทุกเมธอดกรองชาร์ด/สาขาที่ผู้เรียกส่งมา</summary>
public interface ITaxReportRepository
{
    Task<IReadOnlyList<TaxInvoice>> TaxInvoicesAsync(string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<CreditNote>> CreditNotesAsync(string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<UninvoicedReceiptRow>> UninvoicedVatReceiptsAsync(string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);
    Task<IReadOnlyList<VatSaleRow>> VatSalesAsync(string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    /// <summary>ใบกำกับภาษีซื้อทั้งหมดของเดือนที่ใช้สิทธิ์ (รวมที่ยกเลิกแล้ว)</summary>
    Task<IReadOnlyList<PurchaseTaxInvoice>> PurchaseInvoicesAsync(string shardKey, int branchId, int claimPeriod, CancellationToken ct = default);
    Task<PurchaseTaxInvoice?> GetPurchaseInvoiceAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default);
    Task<PurchaseTaxInvoice?> GetPurchaseInvoiceByRequestIdAsync(Guid requestId, CancellationToken ct = default);
    /// <summary>มีใบที่ยังไม่ยกเลิกของผู้ขาย (เลขผู้เสียภาษี) + เลขที่ใบกำกับนี้ในสาขาแล้วหรือยัง</summary>
    Task<PurchaseTaxInvoice?> FindActivePurchaseInvoiceAsync(string shardKey, int branchId, string supplierTaxId, string invoiceNo, CancellationToken ct = default);

    Task<Supplier?> GetSupplierAsync(string shardKey, int branchId, Guid supplierId, CancellationToken ct = default);
    Task<GoodsReceiptVatRow?> GetGoodsReceiptAsync(string shardKey, int branchId, Guid goodsReceiptId, CancellationToken ct = default);
    /// <summary>ใบรับสินค้าของ PO ที่มี VAT รับในช่วงนี้ และยังไม่มีใบกำกับภาษีซื้อ (ที่ไม่ยกเลิก) ผูกอยู่</summary>
    Task<IReadOnlyList<GoodsReceiptVatRow>> VatGoodsReceiptsWithoutInvoiceAsync(string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default);

    Task AddPurchaseInvoiceAsync(PurchaseTaxInvoice invoice, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
