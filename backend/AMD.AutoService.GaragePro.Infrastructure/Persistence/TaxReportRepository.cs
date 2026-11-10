using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class TaxReportRepository(ServiceDbContext db) : ITaxReportRepository
{
    public async Task<IReadOnlyList<TaxInvoice>> TaxInvoicesAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await db.TaxInvoices.AsNoTracking()
            .Where(x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId
                        && x.IssuedAt >= fromUtc && x.IssuedAt < toUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CreditNote>> CreditNotesAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await db.CreditNotes.AsNoTracking()
            .Where(x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId
                        && x.IssuedAt >= fromUtc && x.IssuedAt < toUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UninvoicedReceiptRow>> UninvoicedVatReceiptsAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await (from r in db.Receipts.AsNoTracking()
               join j in db.Jobs.AsNoTracking() on r.JobId equals j.Id
               where r.LegacyShardKey == shardKey && r.LegacyBranchId == branchId
                     && r.IssuedAt >= fromUtc && r.IssuedAt < toUtc
                     && r.VatAmount > 0m
                     && !db.TaxInvoices.Any(t => t.JobId == r.JobId)
               select new UninvoicedReceiptRow(r.Id, r.JobId, r.DocumentNo, r.IssuedAt, j.JobNo, j.CustomerName,
                   r.NetAmount, r.VatAmount, r.TotalAmount))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<VatSaleRow>> VatSalesAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await db.Sales.AsNoTracking()
            .Where(s => s.LegacyShardKey == shardKey && s.LegacyBranchId == branchId
                        && s.Status == SaleStatus.Completed && s.VoidedAt == null
                        && s.CompletedAt >= fromUtc && s.CompletedAt < toUtc
                        && s.VatAmount > 0m)
            .Select(s => new VatSaleRow(s.Id, s.ReceiptNo ?? "", s.CompletedAt!.Value, s.CustomerName,
                s.NetAmount, s.VatAmount, s.TotalAmount))
            .ToListAsync(ct);

    public async Task<IReadOnlyList<PurchaseTaxInvoice>> PurchaseInvoicesAsync(
        string shardKey, int branchId, int claimPeriod, CancellationToken ct = default) =>
        await db.PurchaseTaxInvoices.AsNoTracking()
            .Where(x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId && x.ClaimPeriod == claimPeriod)
            .ToListAsync(ct);

    public Task<PurchaseTaxInvoice?> GetPurchaseInvoiceAsync(
        string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
        db.PurchaseTaxInvoices.FirstOrDefaultAsync(
            x => x.Id == id && x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId, ct);

    public Task<PurchaseTaxInvoice?> GetPurchaseInvoiceByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
        db.PurchaseTaxInvoices.FirstOrDefaultAsync(x => x.RequestId == requestId, ct);

    public Task<PurchaseTaxInvoice?> FindActivePurchaseInvoiceAsync(
        string shardKey, int branchId, string supplierTaxId, string invoiceNo, CancellationToken ct = default) =>
        db.PurchaseTaxInvoices.AsNoTracking().FirstOrDefaultAsync(
            x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId
                 && x.SupplierTaxId == supplierTaxId && x.InvoiceNo == invoiceNo && x.VoidedAt == null, ct);

    public Task<Supplier?> GetSupplierAsync(string shardKey, int branchId, Guid supplierId, CancellationToken ct = default) =>
        db.Suppliers.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == supplierId && x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId, ct);

    public async Task<GoodsReceiptVatRow?> GetGoodsReceiptAsync(
        string shardKey, int branchId, Guid goodsReceiptId, CancellationToken ct = default) =>
        (await Project(Receipts(shardKey, branchId).Where(x => x.g.Id == goodsReceiptId)).ToListAsync(ct))
        .FirstOrDefault();

    public async Task<IReadOnlyList<GoodsReceiptVatRow>> VatGoodsReceiptsWithoutInvoiceAsync(
        string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
        await Project(Receipts(shardKey, branchId)
                .Where(x => x.po.HasVat && x.g.ReceivedAt >= fromUtc && x.g.ReceivedAt < toUtc
                            && !db.PurchaseTaxInvoices.Any(p => p.GoodsReceiptId == x.g.Id && p.VoidedAt == null)))
            .ToListAsync(ct);

    // คลาส init (ไม่ใช่ positional record) — EF แปล member access หลัง projection แบบ member-init ได้ แต่ผ่าน constructor ไม่ได้
    private sealed class ReceiptPair
    {
        public GoodsReceipt g { get; init; } = null!;
        public PurchaseDocument po { get; init; } = null!;
    }

    private IQueryable<ReceiptPair> Receipts(string shardKey, int branchId) =>
        from g in db.Set<GoodsReceipt>().AsNoTracking()
        join po in db.Set<PurchaseDocument>().AsNoTracking() on g.PurchaseOrderId equals po.Id
        where g.LegacyShardKey == shardKey && g.LegacyBranchId == branchId
        select new ReceiptPair { g = g, po = po };

    /// <summary>
    /// VAT ของ PO ใช้ค่าปัจจุบันของเอกสาร (HasVat/VatRate) — ค่าเดียวกับที่หน้า PO แสดง
    /// ยอดก่อนภาษีรวมของชำรุดด้วย เพราะผู้ขายออกใบกำกับตามที่ส่งมา (ผู้ใช้แก้ยอดได้ก่อนบันทึก)
    /// </summary>
    private IQueryable<GoodsReceiptVatRow> Project(IQueryable<ReceiptPair> q) =>
        q.Select(x => new GoodsReceiptVatRow(
            x.g.Id, x.g.Number, x.g.ReceivedAt, x.g.DeliveryNumber,
            x.po.Id, x.po.Number, x.po.SupplierId, x.po.SupplierName,
            x.po.HasVat, x.po.VatRate,
            db.Set<GoodsReceiptLine>().Where(l => l.GoodsReceiptId == x.g.Id)
                .Sum(l => (l.GoodQuantity + l.DamagedQuantity) * l.UnitCost)));

    public async Task AddPurchaseInvoiceAsync(PurchaseTaxInvoice invoice, CancellationToken ct = default) =>
        await db.PurchaseTaxInvoices.AddAsync(invoice, ct);

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
