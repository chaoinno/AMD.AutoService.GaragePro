using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Pos;

public interface ITaxReportService
{
    Task<Result<OutputVatReportDto>> OutputVatAsync(int? year, int? month, CancellationToken ct = default);
    Task<Result<InputVatReportDto>> InputVatAsync(int? year, int? month, CancellationToken ct = default);
    Task<Result<PurchaseTaxInvoicePrefillDto>> PrefillFromGoodsReceiptAsync(Guid goodsReceiptId, CancellationToken ct = default);
    Task<Result<PurchaseTaxInvoiceDto>> RecordPurchaseInvoiceAsync(RecordPurchaseTaxInvoiceRequest request, CancellationToken ct = default);
    Task<Result<PurchaseTaxInvoiceDto>> VoidPurchaseInvoiceAsync(Guid id, VoidPurchaseTaxInvoiceRequest request, CancellationToken ct = default);
}

/// <summary>
/// รายงานภาษีขาย/ภาษีซื้อ + บันทึกใบกำกับภาษีซื้อ (docs/20 §3–4 · เพิ่ม 2026-11-09)
///
/// [BIZ] Q7: ทุกอย่างในนี้ = ผู้จัดการ/ธุรการ · เดือนภาษีตามวันที่เอกสาร "เวลาไทย"
/// Q1: ภาษีขายแยกสองส่วน — ส่วนที่ 1 รายการตามกฎหมาย (IV- บวก · ใบลดหนี้ที่อ้าง IV- ลบ)
/// ส่วนที่ 2 รับเงินที่คิด VAT แต่ยังไม่ออกใบกำกับ (RC- ของจ๊อบที่ไม่มี IV- · SL- · ใบลดหนี้ที่อ้าง RC- ลบ)
/// RC- ของจ๊อบที่ออก IV- ภายหลัง (แม้คนละเดือน) ไม่นับในส่วนที่ 2 เพราะ IV- ถูกนับในเดือนที่ออกแล้ว — กันนับซ้ำ
/// Q4/Q5: ใบกำกับภาษีซื้อบันทึกจากใบรับสินค้าหรือบันทึกเอง · เลือกเดือนที่ใช้สิทธิ์ได้ เดือนของใบกำกับ..+6 เดือน (ม.82/3)
/// </summary>
public sealed class TaxReportService(
    ITaxReportRepository repo,
    ICreditNoteRepository creditNotes,
    ILegacyReader legacy,
    ICurrentUser user,
    TimeProvider clock) : ITaxReportService
{
    /// <summary>ใช้สิทธิ์ภาษีซื้อได้ในเดือนของใบกำกับ หรือภายใน 6 เดือนถัดไป</summary>
    public const int ClaimWindowMonths = 6;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateOnly TodayThai => DateOnly.FromDateTime(Now.AddHours(7));
    private bool CanUse => user.Role is UserRole.Manager or UserRole.Office;

    // ---------- ภาษีขาย ----------

    public async Task<Result<OutputVatReportDto>> OutputVatAsync(int? year, int? month, CancellationToken ct = default)
    {
        if (!CanUse) return Forbidden<OutputVatReportDto>("เฉพาะผู้จัดการหรือธุรการเท่านั้นที่ดูรายงานภาษีขายได้");
        if (ResolvePeriod(year, month) is not { } period) return InvalidPeriod<OutputVatReportDto>();
        var (fromUtc, toUtc) = MonthRangeUtc(period.Year, period.Month);

        var invoices = await repo.TaxInvoicesAsync(user.ShardKey, user.BranchId, fromUtc, toUtc, ct);
        var notes = await repo.CreditNotesAsync(user.ShardKey, user.BranchId, fromUtc, toUtc, ct);
        var receipts = await repo.UninvoicedVatReceiptsAsync(user.ShardKey, user.BranchId, fromUtc, toUtc, ct);
        var sales = await repo.VatSalesAsync(user.ShardKey, user.BranchId, fromUtc, toUtc, ct);

        var legal = invoices
            .Select(i => Row("taxInvoice", i.Id, i.JobId, i.DocumentNo, i.IssuedAt, i.BuyerName, i.BuyerTaxId,
                i.BuyerBranchNo, i.NetAmount, i.VatAmount, i.TotalAmount, null))
            .Concat(notes.Where(n => n.SourceType == CreditNoteSourceType.TaxInvoice).Select(CreditRow));
        var uninvoiced = receipts
            .Select(r => Row("receipt", r.ReceiptId, r.JobId, r.DocumentNo, r.IssuedAt, r.CustomerName, null, null,
                r.NetAmount, r.VatAmount, r.TotalAmount, r.JobNo))
            .Concat(sales.Select(s => Row("sale", s.SaleId, null, s.DocumentNo, s.IssuedAt,
                string.IsNullOrWhiteSpace(s.CustomerName) ? "ลูกค้าทั่วไป" : s.CustomerName!, null, null,
                s.NetAmount, s.VatAmount, s.TotalAmount, null)))
            .Concat(notes.Where(n => n.SourceType == CreditNoteSourceType.Receipt).Select(CreditRow));

        var rows = Sequence(legal);
        var others = Sequence(uninvoiced);
        return Result<OutputVatReportDto>.Ok(new OutputVatReportDto(
            period.Year, period.Month, await SellerAsync(ct),
            rows, rows.Sum(r => r.NetAmount), rows.Sum(r => r.VatAmount), rows.Sum(r => r.TotalAmount),
            others, others.Sum(r => r.NetAmount), others.Sum(r => r.VatAmount), others.Sum(r => r.TotalAmount)));
    }

    private static OutputVatRowDto CreditRow(CreditNote n) =>
        Row("creditNote", n.Id, n.JobId, n.DocumentNo, n.IssuedAt, n.BuyerName, n.BuyerTaxId, n.BuyerBranchNo,
            -n.DiffNetAmount, -n.VatAmount, -n.TotalAmount, n.SourceDocumentNo);

    private static OutputVatRowDto Row(string kind, Guid id, Guid? jobId, string docNo, DateTime at, string buyer,
        string? taxId, string? branchNo, decimal net, decimal vat, decimal total, string? reference) =>
        new(0, kind, id, jobId, docNo, at, buyer, taxId, branchNo, net, vat, total, reference);

    private static List<OutputVatRowDto> Sequence(IEnumerable<OutputVatRowDto> rows) =>
        rows.OrderBy(r => r.IssuedAt).ThenBy(r => r.DocumentNo, StringComparer.Ordinal)
            .Select((r, i) => r with { Sequence = i + 1 })
            .ToList();

    // ---------- ภาษีซื้อ ----------

    public async Task<Result<InputVatReportDto>> InputVatAsync(int? year, int? month, CancellationToken ct = default)
    {
        if (!CanUse) return Forbidden<InputVatReportDto>("เฉพาะผู้จัดการหรือธุรการเท่านั้นที่ดูรายงานภาษีซื้อได้");
        if (ResolvePeriod(year, month) is not { } period) return InvalidPeriod<InputVatReportDto>();

        var all = await repo.PurchaseInvoicesAsync(user.ShardKey, user.BranchId, period.Year * 100 + period.Month, ct);
        var rows = all.Where(x => x.VoidedAt is null)
            .OrderBy(x => x.InvoiceDate).ThenBy(x => x.SupplierName).ThenBy(x => x.InvoiceNo, StringComparer.Ordinal)
            .Select(ToDto).ToList();
        var voided = all.Where(x => x.VoidedAt is not null).OrderBy(x => x.VoidedAt).Select(ToDto).ToList();

        // ตัวช่วยเตือน: ใบรับสินค้าของ PO ที่มี VAT ตั้งแต่ 6 เดือนก่อนจนจบเดือนนี้ (ยังใช้สิทธิ์ได้) ที่ยังไม่บันทึกใบกำกับ
        var (_, toUtc) = MonthRangeUtc(period.Year, period.Month);
        var windowStart = new DateOnly(period.Year, period.Month, 1).AddMonths(-ClaimWindowMonths);
        var (fromUtc, _) = MonthRangeUtc(windowStart.Year, windowStart.Month);
        var pending = (await repo.VatGoodsReceiptsWithoutInvoiceAsync(user.ShardKey, user.BranchId, fromUtc, toUtc, ct))
            .OrderBy(g => g.ReceivedAt)
            .Select(g => new PendingGoodsReceiptDto(g.GoodsReceiptId, g.Number, g.ReceivedAt, g.PurchaseOrderNumber,
                g.SupplierName, Blank(g.DeliveryNumber), Round(g.NetAmount), Round(g.NetAmount * g.VatRate)))
            .ToList();

        return Result<InputVatReportDto>.Ok(new InputVatReportDto(
            period.Year, period.Month, await SellerAsync(ct),
            rows, rows.Sum(r => r.NetAmount), rows.Sum(r => r.VatAmount), rows.Sum(r => r.TotalAmount),
            voided, pending));
    }

    public async Task<Result<PurchaseTaxInvoicePrefillDto>> PrefillFromGoodsReceiptAsync(
        Guid goodsReceiptId, CancellationToken ct = default)
    {
        if (!CanUse) return Forbidden<PurchaseTaxInvoicePrefillDto>();
        var g = await repo.GetGoodsReceiptAsync(user.ShardKey, user.BranchId, goodsReceiptId, ct);
        if (g is null)
            return Result<PurchaseTaxInvoicePrefillDto>.Fail("GOODS_RECEIPT_NOT_FOUND", "ไม่พบใบรับสินค้านี้ในสาขา");

        var supplier = g.SupplierId is { } sid ? await repo.GetSupplierAsync(user.ShardKey, user.BranchId, sid, ct) : null;
        var taxId = Digits(supplier?.TaxId);
        var net = Round(g.NetAmount);
        return Result<PurchaseTaxInvoicePrefillDto>.Ok(new PurchaseTaxInvoicePrefillDto(
            g.GoodsReceiptId, g.Number, g.PurchaseOrderId, g.PurchaseOrderNumber,
            g.SupplierId, supplier?.Name ?? g.SupplierName, taxId.Length == 13 ? taxId : null,
            Blank(g.DeliveryNumber), DateOnly.FromDateTime(g.ReceivedAt.AddHours(7)),
            g.HasVat, g.VatRate, net, g.HasVat ? Round(net * g.VatRate) : 0m));
    }

    public async Task<Result<PurchaseTaxInvoiceDto>> RecordPurchaseInvoiceAsync(
        RecordPurchaseTaxInvoiceRequest request, CancellationToken ct = default)
    {
        if (!CanUse) return Forbidden<PurchaseTaxInvoiceDto>();
        if (request.RequestId == Guid.Empty) return Invalid<PurchaseTaxInvoiceDto>("ไม่มีรหัสคำขอ", "requestId");

        var hash = Hash(request);
        var existing = await repo.GetPurchaseInvoiceByRequestIdAsync(request.RequestId, ct);
        if (existing is not null)
            return existing.RequestHash == hash && existing.LegacyShardKey == user.ShardKey && existing.LegacyBranchId == user.BranchId
                ? Result<PurchaseTaxInvoiceDto>.Ok(ToDto(existing))
                : Result<PurchaseTaxInvoiceDto>.Fail("PURCHASE_TAX_INVOICE_CONFLICT",
                    "รหัสคำขอนี้ถูกใช้กับข้อมูลอื่นแล้ว กรุณาเปิดหน้าต่างบันทึกใหม่");

        Supplier? supplier = null;
        if (request.SupplierId is { } supplierId)
        {
            supplier = await repo.GetSupplierAsync(user.ShardKey, user.BranchId, supplierId, ct);
            if (supplier is null)
                return Result<PurchaseTaxInvoiceDto>.Fail("SUPPLIER_NOT_FOUND", "ไม่พบซัพพลายเออร์นี้ในสาขา", "supplierId");
        }

        GoodsReceiptVatRow? receipt = null;
        if (request.GoodsReceiptId is { } grId)
        {
            receipt = await repo.GetGoodsReceiptAsync(user.ShardKey, user.BranchId, grId, ct);
            if (receipt is null)
                return Result<PurchaseTaxInvoiceDto>.Fail("GOODS_RECEIPT_NOT_FOUND", "ไม่พบใบรับสินค้านี้ในสาขา", "goodsReceiptId");
        }

        var name = (request.SupplierName ?? "").Trim();
        if (name.Length == 0) name = supplier?.Name.Trim() ?? "";
        if (name.Length == 0) return Invalid<PurchaseTaxInvoiceDto>("กรุณาระบุชื่อผู้ขาย", "supplierName");
        if (name.Length > 200) return Invalid<PurchaseTaxInvoiceDto>("ชื่อผู้ขายยาวเกิน 200 ตัวอักษร", "supplierName");

        var taxId = Digits(string.IsNullOrWhiteSpace(request.SupplierTaxId) ? supplier?.TaxId : request.SupplierTaxId);
        if (taxId.Length != 13)
            return Invalid<PurchaseTaxInvoiceDto>("เลขประจำตัวผู้เสียภาษีของผู้ขายต้องเป็นตัวเลข 13 หลัก", "supplierTaxId");

        var branchNo = string.IsNullOrWhiteSpace(request.SupplierBranchNo) ? CreditNoteService.HeadOfficeBranchNo : request.SupplierBranchNo.Trim();
        if (branchNo.Length != 5 || !branchNo.All(char.IsAsciiDigit))
            return Invalid<PurchaseTaxInvoiceDto>("สาขาของผู้ขายต้องเป็นตัวเลข 5 หลัก (สำนักงานใหญ่ = 00000)", "supplierBranchNo");

        var invoiceNo = (request.InvoiceNo ?? "").Trim();
        if (invoiceNo.Length == 0) return Invalid<PurchaseTaxInvoiceDto>("กรุณาระบุเลขที่ใบกำกับภาษี", "invoiceNo");
        if (invoiceNo.Length > 60) return Invalid<PurchaseTaxInvoiceDto>("เลขที่ใบกำกับภาษียาวเกิน 60 ตัวอักษร", "invoiceNo");

        if (request.InvoiceDate is not { } invoiceDate)
            return Invalid<PurchaseTaxInvoiceDto>("กรุณาระบุวันที่ตามใบกำกับภาษี", "invoiceDate");
        if (invoiceDate > TodayThai)
            return Invalid<PurchaseTaxInvoiceDto>("วันที่ใบกำกับภาษีต้องไม่เป็นวันในอนาคต", "invoiceDate");

        var claimYear = request.ClaimYear ?? invoiceDate.Year;
        var claimMonth = request.ClaimMonth ?? invoiceDate.Month;
        if (claimMonth is < 1 or > 12 || claimYear is < 2000 or > 2100)
            return Invalid<PurchaseTaxInvoiceDto>("เดือนภาษีที่ใช้สิทธิ์ไม่ถูกต้อง", "claimMonth");
        var monthsAfter = (claimYear * 12 + claimMonth) - (invoiceDate.Year * 12 + invoiceDate.Month);
        if (monthsAfter < 0)
            return Invalid<PurchaseTaxInvoiceDto>("เดือนที่ใช้สิทธิ์ต้องไม่ก่อนเดือนของใบกำกับภาษี", "claimMonth");
        if (monthsAfter > ClaimWindowMonths)
            return Invalid<PurchaseTaxInvoiceDto>(
                $"ใช้สิทธิ์ภาษีซื้อได้ไม่เกิน {ClaimWindowMonths} เดือนถัดจากเดือนของใบกำกับภาษี", "claimMonth");

        if (request.NetAmount <= 0m || decimal.Round(request.NetAmount, 2) != request.NetAmount)
            return Invalid<PurchaseTaxInvoiceDto>("มูลค่าก่อนภาษีต้องมากกว่า 0 และมีทศนิยมไม่เกิน 2 ตำแหน่ง", "netAmount");
        if (request.VatAmount <= 0m || decimal.Round(request.VatAmount, 2) != request.VatAmount)
            return Invalid<PurchaseTaxInvoiceDto>("ภาษีมูลค่าเพิ่มต้องมากกว่า 0 และมีทศนิยมไม่เกิน 2 ตำแหน่ง", "vatAmount");
        if (request.VatAmount >= request.NetAmount)
            return Invalid<PurchaseTaxInvoiceDto>("ภาษีมูลค่าเพิ่มต้องน้อยกว่ามูลค่าก่อนภาษี", "vatAmount");

        var note = (request.Note ?? "").Trim();
        if (note.Length > 500) return Invalid<PurchaseTaxInvoiceDto>("หมายเหตุยาวเกิน 500 ตัวอักษร", "note");

        var duplicate = await repo.FindActivePurchaseInvoiceAsync(user.ShardKey, user.BranchId, taxId, invoiceNo, ct);
        if (duplicate is not null)
            return Result<PurchaseTaxInvoiceDto>.Fail("PURCHASE_TAX_INVOICE_DUPLICATE",
                $"ใบกำกับภาษีเลขที่ {invoiceNo} ของผู้ขายรายนี้บันทึกไว้แล้ว (ใช้สิทธิ์เดือน {ThaiMonth(duplicate.ClaimPeriod)})",
                "invoiceNo");

        var invoice = new PurchaseTaxInvoice
        {
            LegacyShardKey = user.ShardKey,
            LegacyBranchId = user.BranchId,
            SupplierId = supplier?.Id,
            SupplierName = name,
            SupplierTaxId = taxId,
            SupplierBranchNo = branchNo,
            InvoiceNo = invoiceNo,
            InvoiceDate = invoiceDate,
            ClaimPeriod = claimYear * 100 + claimMonth,
            NetAmount = request.NetAmount,
            VatAmount = request.VatAmount,
            PurchaseOrderId = receipt?.PurchaseOrderId,
            PurchaseOrderNumber = receipt?.PurchaseOrderNumber,
            GoodsReceiptId = receipt?.GoodsReceiptId,
            GoodsReceiptNumber = receipt?.Number,
            Note = note.Length == 0 ? null : note,
            RequestId = request.RequestId,
            RequestHash = hash,
            RecordedByUserId = user.UserId,
            RecordedByName = user.UserName,
            RecordedAt = Now
        };
        await repo.AddPurchaseInvoiceAsync(invoice, ct);
        await repo.AddEventAsync(Event(invoice, "purchase_tax_invoice.recorded",
            $"บันทึกใบกำกับภาษีซื้อ {invoice.InvoiceNo} ของ {invoice.SupplierName} ใช้สิทธิ์เดือน {ThaiMonth(invoice.ClaimPeriod)}"), ct);
        await repo.SaveChangesAsync(ct);
        return Result<PurchaseTaxInvoiceDto>.Ok(ToDto(invoice));
    }

    public async Task<Result<PurchaseTaxInvoiceDto>> VoidPurchaseInvoiceAsync(
        Guid id, VoidPurchaseTaxInvoiceRequest request, CancellationToken ct = default)
    {
        if (!CanUse) return Forbidden<PurchaseTaxInvoiceDto>();
        var invoice = await repo.GetPurchaseInvoiceAsync(user.ShardKey, user.BranchId, id, ct);
        if (invoice is null)
            return Result<PurchaseTaxInvoiceDto>.Fail("PURCHASE_TAX_INVOICE_NOT_FOUND", "ไม่พบใบกำกับภาษีซื้อนี้ในสาขา");
        if (invoice.VoidedAt is not null)
            return Result<PurchaseTaxInvoiceDto>.Fail("PURCHASE_TAX_INVOICE_VOIDED", "ใบกำกับภาษีซื้อนี้ถูกยกเลิกไปแล้ว");

        var reason = (request.Reason ?? "").Trim();
        if (reason.Length == 0) return Invalid<PurchaseTaxInvoiceDto>("กรุณาระบุเหตุผลที่ยกเลิก", "reason");
        if (reason.Length > 500) return Invalid<PurchaseTaxInvoiceDto>("เหตุผลยาวเกิน 500 ตัวอักษร", "reason");

        invoice.VoidedAt = Now;
        invoice.VoidedByName = user.UserName;
        invoice.VoidReason = reason;
        await repo.AddEventAsync(Event(invoice, "purchase_tax_invoice.voided",
            $"ยกเลิกใบกำกับภาษีซื้อ {invoice.InvoiceNo} ของ {invoice.SupplierName}: {reason}"), ct);
        await repo.SaveChangesAsync(ct);
        return Result<PurchaseTaxInvoiceDto>.Ok(ToDto(invoice));
    }

    // ---------- helper ----------

    private ActivityEvent Event(PurchaseTaxInvoice invoice, string type, string description) => new()
    {
        EntityType = nameof(PurchaseTaxInvoice),
        EntityId = invoice.Id,
        EventType = type,
        DescriptionTh = description,
        PayloadJson = JsonSerializer.Serialize(new
        {
            invoice.InvoiceNo, invoice.SupplierTaxId, invoice.ClaimPeriod, invoice.NetAmount, invoice.VatAmount,
            invoice.GoodsReceiptNumber, invoice.VoidReason
        }),
        PerformedByUserId = user.UserId,
        PerformedByName = user.UserName,
        Source = user.Source,
        OccurredAt = Now
    };

    /// <summary>ไม่ส่งปี/เดือน = เดือนปัจจุบันตามเวลาไทย</summary>
    private (int Year, int Month)? ResolvePeriod(int? year, int? month)
    {
        var today = TodayThai;
        var y = year ?? today.Year;
        var m = month ?? today.Month;
        if (m is < 1 or > 12 || y is < 2000 or > 2100) return null;
        return (y, m);
    }

    /// <summary>เดือนตามปฏิทินไทย → ช่วง UTC [00:00 วันแรก, 00:00 วันแรกของเดือนถัดไป)</summary>
    private static (DateTime FromUtc, DateTime ToUtc) MonthRangeUtc(int year, int month)
    {
        var first = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (first.AddHours(-7), first.AddMonths(1).AddHours(-7));
    }

    private async Task<TaxReportPartyDto?> SellerAsync(CancellationToken ct)
    {
        var branch = await legacy.GetBranchAsync(user.ShardKey, user.BranchId, ct);
        if (branch is null) return null;
        var taxId = Digits(branch.TaxId);
        var profile = await creditNotes.GetProfileAsync(user.ShardKey, user.BranchId, ct);
        return new TaxReportPartyDto(
            branch.Name,
            Blank(branch.Address),
            taxId.Length == 13 ? taxId : null,
            profile?.SellerBranchNo ?? CreditNoteService.HeadOfficeBranchNo);
    }

    private static PurchaseTaxInvoiceDto ToDto(PurchaseTaxInvoice x) => new(
        x.Id, x.SupplierId, x.SupplierName, x.SupplierTaxId, x.SupplierBranchNo, x.InvoiceNo, x.InvoiceDate,
        x.ClaimPeriod / 100, x.ClaimPeriod % 100, x.NetAmount, x.VatAmount, x.NetAmount + x.VatAmount,
        x.PurchaseOrderId, x.PurchaseOrderNumber, x.GoodsReceiptId, x.GoodsReceiptNumber, x.Note,
        x.RecordedByName, x.RecordedAt, x.VoidedAt is not null, x.VoidedAt, x.VoidedByName, x.VoidReason);

    private static readonly string[] ThaiMonths =
        ["มกราคม", "กุมภาพันธ์", "มีนาคม", "เมษายน", "พฤษภาคม", "มิถุนายน",
         "กรกฎาคม", "สิงหาคม", "กันยายน", "ตุลาคม", "พฤศจิกายน", "ธันวาคม"];

    private static string ThaiMonth(int period) => $"{ThaiMonths[period % 100 - 1]} {period / 100 + 543}";

    private static string Digits(string? s) => new((s ?? "").Where(char.IsAsciiDigit).ToArray());
    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Hash<T>(T input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));

    private static Result<T> Forbidden<T>(string message = "เฉพาะผู้จัดการหรือธุรการเท่านั้นที่บันทึกใบกำกับภาษีซื้อได้") =>
        Result<T>.Fail("TAX_REPORT_FORBIDDEN", message);

    private static Result<T> InvalidPeriod<T>() =>
        Result<T>.Fail("TAX_REPORT_VALIDATION", "เดือนภาษีไม่ถูกต้อง", "month");

    private static Result<T> Invalid<T>(string message, string field) =>
        Result<T>.Fail("PURCHASE_TAX_INVOICE_VALIDATION", message, field);
}
