using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Pos;

public interface ITaxInvoiceService
{
    Task<Result<TaxInvoiceStateDto>> GetStateAsync(Guid jobId, CancellationToken ct = default);

    Task<Result<TaxInvoiceDto>> IssueAsync(Guid jobId, IssueTaxInvoiceRequest request, CancellationToken ct = default);
}

/// <summary>
/// ใบกำกับภาษีเต็มรูป (IV-) ของงานซ่อม — เพิ่ม 2026-10-02 ตามคำขอผู้ใช้ (OQ#6 เลือกตามที่ต้นแบบสมมติ: แยกเลขจากใบเสร็จ)
///
/// [BIZ] ลำดับ: จ่ายครบ → ออกใบเสร็จ → ออกใบกำกับภาษี · ยอดเงินคัดจากใบเสร็จ (ยอดที่เก็บเงินจริง) ไม่คำนวณใหม่
/// ส่วนรายการคัดจากบรรทัดที่อนุมัติของทุกใบเสนอราคาที่ใช้อยู่ — ถ้ารวมแล้วไม่ตรงกับใบเสร็จจะปฏิเสธ ไม่ออกเอกสารภาษีที่ยอดขัดกันเอง
/// [ASSUME] วันที่ใบกำกับ = วันที่กดออก (อาจช้ากว่าวันรับเงินในใบเสร็จ) — ฝ่ายบัญชีต้องยืนยันว่ารับได้ตามจุดความรับผิดของภาษีบริการ
/// สิทธิ์เดียวกับ PosService (Cashier/Office/Manager)
/// </summary>
public sealed class TaxInvoiceService(
    ITaxInvoiceRepository repo,
    IPosRepository pos,
    IJobRepository jobs,
    IQuotationRepository quotations,
    ILegacyReader legacy,
    ICustomerVehicleService customers,
    ICurrentUser user,
    TimeProvider clock) : ITaxInvoiceService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<TaxInvoiceStateDto>> GetStateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobResult = await PosAccess.ValidateAsync(jobs, user, jobId, ct);
        if (!jobResult.Success) return Result<TaxInvoiceStateDto>.Fail(jobResult.Error!);
        var job = jobResult.Data!;

        var receipt = await pos.GetReceiptByJobAsync(jobId, ct);
        var existing = await repo.GetByJobAsync(jobId, ct);
        if (existing is not null)
            return Result<TaxInvoiceStateDto>.Ok(new TaxInvoiceStateDto(
                TaxInvoiceMapper.ToDto(existing, receipt?.DocumentNo ?? ""),
                Seller: null,
                Prefill: new IssueTaxInvoiceRequest(existing.BuyerName, existing.BuyerAddress, existing.BuyerTaxId, existing.BuyerBranchNo),
                CanIssue: false,
                BlockedReasonTh: null));

        var seller = await SellerAsync(ct);
        var blocked = BlockedReason(job, receipt, seller);

        return Result<TaxInvoiceStateDto>.Ok(new TaxInvoiceStateDto(
            Issued: null,
            Seller: seller,
            Prefill: await PrefillAsync(job, ct),
            CanIssue: blocked is null,
            BlockedReasonTh: blocked));
    }

    public async Task<Result<TaxInvoiceDto>> IssueAsync(
        Guid jobId, IssueTaxInvoiceRequest request, CancellationToken ct = default)
    {
        var jobResult = await PosAccess.ValidateAsync(jobs, user, jobId, ct);
        if (!jobResult.Success) return Result<TaxInvoiceDto>.Fail(jobResult.Error!);
        var job = jobResult.Data!;

        var receipt = await pos.GetReceiptByJobAsync(jobId, ct);

        // ออกได้ใบเดียว — เรียกซ้ำ (เช่น retry หลังเน็ตหลุด) คืนใบเดิม ไม่ออกเลขใหม่ และไม่แก้ข้อมูลผู้ซื้อของใบที่ออกไปแล้ว
        var existing = await repo.GetByJobAsync(jobId, ct);
        if (existing is not null)
            return Result<TaxInvoiceDto>.Ok(TaxInvoiceMapper.ToDto(existing, receipt?.DocumentNo ?? ""));

        var seller = await SellerAsync(ct);
        var blocked = BlockedReason(job, receipt, seller);
        if (blocked is not null)
            return Result<TaxInvoiceDto>.Fail(BlockedCode(job, receipt), blocked);

        var buyer = NormalizeBuyer(request);
        var invalid = ValidateBuyer(buyer);
        if (invalid is not null) return Result<TaxInvoiceDto>.Fail(invalid);

        var active = await quotations.GetActiveForJobAsync(jobId, ct);
        foreach (var q in active) QuotationCalculator.ApplyQuotationTotals(q);
        var contributing = active.Where(q => q.Lines.Any(l => l.ApprovalStatus == LineApprovalStatus.Approved)).ToList();

        var lines = contributing
            .SelectMany(q => q.Lines
                .Where(l => l.ApprovalStatus == LineApprovalStatus.Approved)
                .OrderBy(l => l.Sequence)
                .Select(l => (Quotation: q, Line: l)))
            .Select((x, i) => new TaxInvoiceLine
            {
                Sequence = i + 1,
                QuotationCode = x.Quotation.Code,
                Description = x.Line.Name,
                Quantity = x.Line.Quantity,
                Unit = x.Line.Unit,
                UnitPrice = x.Line.UnitPrice,
                DiscountAmount = x.Line.DiscountAmount + x.Line.PromotionAmount,
                NetAmount = x.Line.NetAmount
            })
            .ToList();

        // [BIZ] เอกสารภาษีต้องให้ผลรวมรายการตรงกับยอดที่เก็บเงินจริง — ใบเสนอราคาถูกล็อกหลังออกใบเสร็จแล้ว
        // (QUOTE_RECEIPT_ISSUED) จึงไม่ควรเกิด ถ้าเกิดแปลว่าข้อมูลผิดปกติ ต้องหยุดไม่ใช่ออกเอกสารที่ขัดกันเอง
        if (lines.Count == 0 || lines.Sum(l => l.NetAmount) != receipt!.NetAmount)
            return Result<TaxInvoiceDto>.Fail(
                "TAX_INVOICE_TOTAL_MISMATCH",
                "รายการที่อนุมัติไม่ตรงกับยอดในใบเสร็จ — ออกใบกำกับภาษีไม่ได้ กรุณาแจ้งผู้ดูแลระบบ");

        var docNo = await repo.NextDocumentNoAsync(user.ShardKey, user.BranchId, Now.AddHours(7), ct);
        var invoice = new TaxInvoice
        {
            JobId = jobId,
            ReceiptId = receipt.Id,
            LegacyShardKey = user.ShardKey,
            LegacyBranchId = user.BranchId,
            DocumentNo = docNo,
            SellerName = seller!.Name,
            SellerAddress = seller.Address,
            SellerTaxId = seller.TaxId!,
            SellerPhone = seller.Phone,
            BuyerName = buyer.BuyerName,
            BuyerAddress = buyer.BuyerAddress,
            BuyerTaxId = buyer.BuyerTaxId,
            BuyerBranchNo = buyer.BuyerBranchNo,
            VatRate = contributing[0].VatRate,
            NetAmount = receipt.NetAmount,
            VatAmount = receipt.VatAmount,
            TotalAmount = receipt.TotalAmount,
            IssuedByUserId = user.UserId,
            IssuedByName = user.UserName,
            IssuedAt = Now,
            Lines = lines
        };
        foreach (var line in lines) line.TaxInvoiceId = invoice.Id;

        await repo.AddAsync(invoice, ct);
        await repo.AddEventAsync(new ActivityEvent
        {
            JobId = jobId,
            EntityId = invoice.Id,
            EntityType = nameof(TaxInvoice),
            EventType = "payment.tax_invoice.issued",
            DescriptionTh = $"ออกใบกำกับภาษี {docNo} (อ้างอิงใบเสร็จ {receipt.DocumentNo}) ในนาม {invoice.BuyerName} " +
                            $"ยอดรวม {invoice.TotalAmount:N2} บาท",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await repo.SaveChangesAsync(ct);

        return Result<TaxInvoiceDto>.Ok(TaxInvoiceMapper.ToDto(invoice, receipt.DocumentNo));
    }

    // ---------- helper ----------

    private static string? BlockedReason(Job job, Receipt? receipt, TaxInvoicePartyDto? seller) =>
        receipt is null
            ? "ต้องรับชำระเงินให้ครบและออกใบเสร็จก่อน จึงจะออกใบกำกับภาษีได้"
            : !job.VatIncluded || receipt.VatAmount <= 0m
                ? "งานนี้ไม่ได้คิดภาษีมูลค่าเพิ่ม — ออกใบกำกับภาษีไม่ได้"
                : seller?.TaxId is null
                    ? "สาขานี้ยังไม่มีเลขประจำตัวผู้เสียภาษี 13 หลักในข้อมูลสาขาของ GaragePro — ให้ผู้ดูแลระบบเพิ่มก่อนออกใบกำกับภาษี"
                    : null;

    private static string BlockedCode(Job job, Receipt? receipt) =>
        receipt is null
            ? "TAX_INVOICE_RECEIPT_REQUIRED"
            : !job.VatIncluded || receipt.VatAmount <= 0m
                ? "TAX_INVOICE_NO_VAT"
                : "TAX_INVOICE_SELLER_TAX_ID_MISSING";

    /// <summary>ข้อมูลผู้ขายจาก Branch เดิม — TaxId เป็น null ถ้าไม่ใช่เลข 13 หลัก (ห้ามพิมพ์เลขที่ไม่ครบลงเอกสารภาษี)</summary>
    private async Task<TaxInvoicePartyDto?> SellerAsync(CancellationToken ct)
    {
        var branch = await legacy.GetBranchAsync(user.ShardKey, user.BranchId, ct);
        if (branch is null) return null;
        var taxId = DigitsOnly(branch.TaxId);
        return new TaxInvoicePartyDto(
            branch.Name,
            string.IsNullOrWhiteSpace(branch.Address) ? null : branch.Address.Trim(),
            taxId?.Length == 13 ? taxId : null,
            string.IsNullOrWhiteSpace(branch.Phone) ? null : branch.Phone.Trim(),
            null);
    }

    /// <summary>เติมจากข้อมูลลูกค้า — ดึงไม่ได้ (นอกสโคป/ถูกลบ) ใช้ชื่อที่ snapshot ไว้ในจ๊อบแทน ให้แคชเชียร์กรอกที่อยู่เอง</summary>
    private async Task<IssueTaxInvoiceRequest> PrefillAsync(Job job, CancellationToken ct)
    {
        var result = await customers.GetCustomerAsync(job.CustomerId, ct);
        if (!result.Success || result.Data is null)
            return new IssueTaxInvoiceRequest(job.CustomerName, "", null, null);

        var c = result.Data;
        var name = $"{c.FirstName} {c.LastName}".Trim();
        var idCard = DigitsOnly(c.IdCard);
        return new IssueTaxInvoiceRequest(
            string.IsNullOrWhiteSpace(name) ? job.CustomerName : name,
            FormatAddress(c),
            idCard?.Length == 13 ? idCard : null,
            null);
    }

    public static string FormatAddress(CustomerDetailDto c)
    {
        // กรุงเทพฯ ใช้ แขวง/เขต ส่วนจังหวัดอื่นใช้ ตำบล/อำเภอ
        var bangkok = c.ProvinceName?.Contains("กรุงเทพ") == true;
        var parts = new[]
        {
            c.Address1, c.Address2,
            Prefixed(bangkok ? "แขวง" : "ต.", c.DistrictName),
            Prefixed(bangkok ? "เขต" : "อ.", c.AmphureName),
            bangkok ? c.ProvinceName : Prefixed("จ.", c.ProvinceName),
            c.ZipCode
        };
        return string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
    }

    private static string? Prefixed(string prefix, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : $"{prefix}{value.Trim()}";

    private static string? DigitsOnly(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }

    private static IssueTaxInvoiceRequest NormalizeBuyer(IssueTaxInvoiceRequest r) => new(
        (r.BuyerName ?? "").Trim(),
        (r.BuyerAddress ?? "").Trim(),
        DigitsOnly(r.BuyerTaxId),
        DigitsOnly(r.BuyerBranchNo));

    private static ApiError? ValidateBuyer(IssueTaxInvoiceRequest b)
    {
        if (b.BuyerName.Length == 0)
            return new("TAX_INVOICE_VALIDATION", "กรุณาระบุชื่อผู้ซื้อ", "buyerName");
        if (b.BuyerName.Length > 200)
            return new("TAX_INVOICE_VALIDATION", "ชื่อผู้ซื้อยาวเกิน 200 ตัวอักษร", "buyerName");
        // [BIZ] ใบกำกับภาษีเต็มรูปต้องมีที่อยู่ผู้ซื้อเสมอ (ป.รัษฎากร ม.86/4)
        if (b.BuyerAddress.Length == 0)
            return new("TAX_INVOICE_VALIDATION", "กรุณาระบุที่อยู่ผู้ซื้อ", "buyerAddress");
        if (b.BuyerAddress.Length > 500)
            return new("TAX_INVOICE_VALIDATION", "ที่อยู่ผู้ซื้อยาวเกิน 500 ตัวอักษร", "buyerAddress");
        if (b.BuyerTaxId is not null && b.BuyerTaxId.Length != 13)
            return new("TAX_INVOICE_VALIDATION", "เลขประจำตัวผู้เสียภาษีต้องเป็นตัวเลข 13 หลัก", "buyerTaxId");
        if (b.BuyerBranchNo is not null && b.BuyerBranchNo.Length != 5)
            return new("TAX_INVOICE_VALIDATION", "เลขที่สาขาต้องเป็นตัวเลข 5 หลัก (สำนักงานใหญ่ = 00000)", "buyerBranchNo");
        if (b.BuyerBranchNo is not null && b.BuyerTaxId is null)
            return new("TAX_INVOICE_VALIDATION", "ระบุสาขาของผู้ซื้อแล้วต้องระบุเลขประจำตัวผู้เสียภาษีด้วย", "buyerTaxId");
        return null;
    }
}

/// <summary>สิทธิ์ร่วมของโมดูลรับเงิน (ใบเสร็จ/ใบกำกับภาษี) — แหล่งเดียว ไม่ให้สองที่ตัดสินต่างกัน</summary>
internal static class PosAccess
{
    public static async Task<Result<Job>> ValidateAsync(
        IJobRepository jobs, ICurrentUser user, Guid jobId, CancellationToken ct)
    {
        if (user.Role is not (UserRole.Cashier or UserRole.Office or UserRole.Manager))
            return Result<Job>.Fail("POS_FORBIDDEN", "เฉพาะแคชเชียร์ ธุรการ หรือผู้จัดการเท่านั้นที่ใช้หน้านี้ได้");

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return Result<Job>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<Job>.Fail("JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        return Result<Job>.Ok(job);
    }
}
