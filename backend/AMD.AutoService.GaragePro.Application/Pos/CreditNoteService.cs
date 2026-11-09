using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Pos;

public interface ICreditNoteService
{
    Task<Result<CreditNoteStateDto>> GetStateAsync(Guid jobId, CancellationToken ct = default);
    Task<Result<CreditNoteDto>> IssueAsync(Guid jobId, IssueCreditNoteRequest request, CancellationToken ct = default);
    Task<Result<CreditNoteDto>> RecordRefundAsync(Guid creditNoteId, RecordCreditNoteRefundRequest request, CancellationToken ct = default);
    Task<Result<CreditNoteReportDto>> ReportAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default);
    Task<Result<BranchTaxProfileDto>> GetProfileAsync(CancellationToken ct = default);
    Task<Result<BranchTaxProfileDto>> UpdateProfileAsync(UpdateBranchTaxProfileRequest request, CancellationToken ct = default);
}

/// <summary>
/// ใบลดหนี้ CN- + คืนเงิน + รายงาน + ค่าตั้งภาษีของสาขา (เพิ่ม 2026-11-09 · docs/20 §2)
///
/// [BIZ] อ้างบิลได้สองแบบ: จ๊อบที่มีใบกำกับ IV- ต้องอ้าง IV- เท่านั้น (ใบลดหนี้ทางภาษี) · จ๊อบที่มีแค่ใบเสร็จ RC- อ้าง RC-
/// (ไม่ใช่เอกสารทางภาษี) — แหล่งบรรทัดและยอดคงเหลือคำนวณที่ <see cref="BuildSourceAsync"/> ที่เดียว ใช้ทั้งหน้าจอและตอนออก
/// ออก/คืนเงิน = ผู้จัดการเท่านั้น · ดูสถานะ = สิทธิ์เดียวกับหน้ารับเงิน · ยกเลิก/แก้ไม่ได้ · ไม่แตะ Payment/Receipt/สต็อก
/// </summary>
public sealed class CreditNoteService(
    ICreditNoteRepository repo,
    ITaxInvoiceRepository taxInvoices,
    IPosRepository pos,
    IJobRepository jobs,
    IQuotationRepository quotations,
    ILegacyReader legacy,
    ICurrentUser user,
    TimeProvider clock) : ICreditNoteService
{
    public const string HeadOfficeBranchNo = "00000";
    public const int ReportMaxDays = 366;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool IsManager => user.Role == UserRole.Manager;

    private sealed record SourceLine(Guid Id, string QuotationCode, string Description, string Unit, decimal Quantity, decimal NetAmount);

    private sealed record Source(
        CreditNoteSourceType Type,
        Receipt Receipt,
        TaxInvoice? Invoice,
        string DocumentNo,
        DateTime IssuedAt,
        decimal VatRate,
        decimal OriginalNet,
        IReadOnlyList<SourceLine> Lines);

    // ---------- state ----------

    public async Task<Result<CreditNoteStateDto>> GetStateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobResult = await PosAccess.ValidateAsync(jobs, user, jobId, ct);
        if (!jobResult.Success) return Result<CreditNoteStateDto>.Fail(jobResult.Error!);

        var notes = await repo.GetByJobAsync(jobId, ct);
        var source = await BuildSourceAsync(jobResult.Data!, ct);
        var issued = notes.OrderBy(n => n.IssuedAt).Select(CreditNoteMapper.ToDto).ToList();

        if (source is null)
            return Result<CreditNoteStateDto>.Ok(new CreditNoteStateDto(
                null, null, null, 0m, 0m, 0m, 0m, [], issued, false,
                "ต้องรับชำระเงินและออกใบเสร็จก่อน จึงจะออกใบลดหนี้ได้", IsManager));

        var plan = BuildPlan(source, notes);
        var credited = plan.Sum(l => l.CreditedAmount);
        var remaining = source.OriginalNet - credited;
        var blocked = !IsManager
            ? "เฉพาะผู้จัดการเท่านั้นที่ออกใบลดหนี้ได้"
            : remaining <= 0m ? "ลดหนี้ครบยอดของบิลนี้แล้ว" : null;

        return Result<CreditNoteStateDto>.Ok(new CreditNoteStateDto(
            CreditNoteMapper.SourceToken(source.Type), source.DocumentNo, source.IssuedAt, source.VatRate,
            source.OriginalNet, credited, remaining, plan, issued, blocked is null, blocked, IsManager));
    }

    // ---------- issue ----------

    public async Task<Result<CreditNoteDto>> IssueAsync(
        Guid jobId, IssueCreditNoteRequest request, CancellationToken ct = default)
    {
        var jobResult = await PosAccess.ValidateAsync(jobs, user, jobId, ct);
        if (!jobResult.Success) return Result<CreditNoteDto>.Fail(jobResult.Error!);
        if (!IsManager) return Forbidden<CreditNoteDto>();
        var job = jobResult.Data!;

        if (request.RequestId == Guid.Empty)
            return Invalid<CreditNoteDto>("ไม่พบ RequestId", "requestId");

        // กดซ้ำ/retry หลังเน็ตหลุด — คืนใบเดิม ไม่ออกเลขใหม่
        var hash = Hash(request);
        var replay = await repo.GetByRequestIdAsync(request.RequestId, ct);
        if (replay is not null)
            return replay.RequestHash == hash && replay.JobId == jobId
                ? Result<CreditNoteDto>.Ok(CreditNoteMapper.ToDto(replay))
                : Result<CreditNoteDto>.Fail("CREDIT_NOTE_CONFLICT", "RequestId นี้เคยใช้ออกใบลดหนี้อื่นไปแล้ว กรุณาลองใหม่");

        var source = await BuildSourceAsync(job, ct);
        if (source is null)
            return Result<CreditNoteDto>.Fail("CREDIT_NOTE_SOURCE_REQUIRED",
                "ต้องรับชำระเงินและออกใบเสร็จก่อน จึงจะออกใบลดหนี้ได้");

        var reason = CreditNoteMapper.ParseReason(request.Reason);
        if (reason is null)
            return Result<CreditNoteDto>.Fail("CREDIT_NOTE_REASON_REQUIRED", "กรุณาเลือกเหตุผลการลดหนี้", "reason");
        var reasonText = string.IsNullOrWhiteSpace(request.ReasonText) ? null : request.ReasonText.Trim();
        if (reason == CreditNoteReason.Other && reasonText is null)
            return Result<CreditNoteDto>.Fail("CREDIT_NOTE_REASON_REQUIRED", "เลือก \"เหตุอื่น\" ต้องระบุรายละเอียด", "reasonText");
        if (reasonText?.Length > 500)
            return Invalid<CreditNoteDto>("รายละเอียดเหตุผลยาวเกิน 500 ตัวอักษร", "reasonText");

        if (request.Lines is null || request.Lines.Count == 0)
            return Result<CreditNoteDto>.Fail("CREDIT_NOTE_LINES_REQUIRED", "ต้องเลือกรายการจากบิลอย่างน้อย 1 รายการ", "lines");
        if (request.Lines.Select(l => l.SourceLineId).Distinct().Count() != request.Lines.Count)
            return Invalid<CreditNoteDto>("เลือกรายการเดียวกันซ้ำ", "lines");

        var existing = await repo.GetByJobAsync(jobId, ct);
        var plan = BuildPlan(source, existing).ToDictionary(l => l.SourceLineId);
        var lines = new List<CreditNoteLine>();
        foreach (var req in request.Lines)
        {
            if (!plan.TryGetValue(req.SourceLineId, out var p))
                return Result<CreditNoteDto>.Fail("CREDIT_NOTE_LINE_NOT_IN_SOURCE",
                    $"รายการที่เลือกไม่อยู่ในบิล {source.DocumentNo}", "lines");

            var built = BuildLine(req, p);
            if (!built.Success) return Result<CreditNoteDto>.Fail(built.Error!);
            lines.Add(built.Data!);
        }

        // จะเกิดได้แค่ลดเป็นจำนวนแล้วปัดเศษเหลือ 0.00 (เช่น 1 ใน 1,000 ชิ้นของรายการ 1 บาท)
        if (lines.Any(l => l.Amount <= 0m))
            return Invalid<CreditNoteDto>("ยอดที่ลดของบางรายการปัดเศษแล้วเป็นศูนย์ ให้ระบุเป็นยอดเงินแทน", "lines");

        var refundRequest = request.Refund;
        var refundMethod = default(PaymentMethod);
        if (refundRequest is not null)
        {
            var refundError = ValidateRefundRequest(refundRequest, out refundMethod);
            if (refundError is not null) return Result<CreditNoteDto>.Fail(refundError);
            if (refundRequest.RequestId == request.RequestId)
                return Invalid<CreditNoteDto>("RequestId ของการคืนเงินต้องไม่ซ้ำกับของใบลดหนี้", "refund.requestId");
        }

        var diff = lines.Sum(l => l.Amount);
        var vat = Round(diff * source.VatRate);
        var total = diff + vat;
        if (refundRequest is not null && refundRequest.Amount > total)
            return Result<CreditNoteDto>.Fail("CREDIT_NOTE_REFUND_EXCEEDS",
                $"ยอดคืนเงินเกินยอดใบลดหนี้ ({total:N2} บาท)", "refund.amount");

        var alreadyCredited = plan.Values.Sum(p => p.CreditedAmount);
        var profile = await repo.GetProfileAsync(user.ShardKey, user.BranchId, ct);
        var seller = await SellerAsync(source, profile, ct);
        var docNo = await repo.NextDocumentNoAsync(user.ShardKey, user.BranchId, Now.AddHours(7), ct);

        var note = new CreditNote
        {
            JobId = jobId,
            LegacyShardKey = user.ShardKey,
            LegacyBranchId = user.BranchId,
            DocumentNo = docNo,
            JobNo = job.JobNo,
            VehicleRegistration = job.VehicleRegistration,
            SourceType = source.Type,
            ReceiptId = source.Receipt.Id,
            TaxInvoiceId = source.Invoice?.Id,
            SourceDocumentNo = source.DocumentNo,
            SourceIssuedAt = source.IssuedAt,
            ReasonCode = reason.Value,
            ReasonText = reasonText,
            SellerName = seller.Name,
            SellerAddress = seller.Address,
            SellerTaxId = seller.TaxId,
            SellerBranchNo = seller.BranchNo,
            BuyerName = source.Invoice?.BuyerName ?? job.CustomerName,
            BuyerAddress = source.Invoice?.BuyerAddress,
            BuyerTaxId = source.Invoice?.BuyerTaxId,
            BuyerBranchNo = source.Invoice?.BuyerBranchNo,
            OriginalNetAmount = source.OriginalNet,
            CorrectNetAmount = source.OriginalNet - alreadyCredited - diff,
            DiffNetAmount = diff,
            VatRate = source.VatRate,
            VatAmount = vat,
            TotalAmount = total,
            RequestId = request.RequestId,
            RequestHash = hash,
            IssuedByUserId = user.UserId,
            IssuedByName = user.UserName,
            IssuedAt = Now,
            Lines = lines
        };
        for (var i = 0; i < lines.Count; i++)
        {
            lines[i].Sequence = i + 1;
            lines[i].CreditNoteId = note.Id;
        }

        await repo.AddAsync(note, ct);
        await repo.AddEventAsync(Event(note, "credit_note.issued",
            $"ออกใบลดหนี้ {docNo} อ้างอิง {source.DocumentNo} ยอด {total:N2} บาท ({CreditNoteMapper.ReasonLabel(note.ReasonCode)})"), ct);

        if (refundRequest is not null)
        {
            var refund = NewRefund(note.Id, refundRequest, refundMethod);
            note.Refunds.Add(refund);
            await repo.AddRefundAsync(refund, ct);
            await repo.AddEventAsync(Event(note, "credit_note.refunded",
                $"คืนเงินตามใบลดหนี้ {docNo} {refund.Amount:N2} บาท ({PosMapper.ToMethodToken(refund.Method)})"), ct);
        }

        await repo.SaveChangesAsync(ct);
        return Result<CreditNoteDto>.Ok(CreditNoteMapper.ToDto(note));
    }

    // ---------- refund ----------

    public async Task<Result<CreditNoteDto>> RecordRefundAsync(
        Guid creditNoteId, RecordCreditNoteRefundRequest request, CancellationToken ct = default)
    {
        var note = await repo.GetAsync(creditNoteId, ct);
        if (note is null || note.LegacyShardKey != user.ShardKey || note.LegacyBranchId != user.BranchId)
            return Result<CreditNoteDto>.Fail("CREDIT_NOTE_NOT_FOUND", "ไม่พบใบลดหนี้นี้ในสาขาปัจจุบัน");
        if (!IsManager) return Forbidden<CreditNoteDto>("เฉพาะผู้จัดการเท่านั้นที่บันทึกการคืนเงินได้");

        // retry ด้วย RequestId เดิม = ยืนยันคำขอเดิม ไม่คืนเงินซ้ำ
        if (request.RequestId != Guid.Empty && await repo.RefundRequestExistsAsync(request.RequestId, ct))
            return Result<CreditNoteDto>.Ok(CreditNoteMapper.ToDto(note));

        var error = ValidateRefundRequest(request, out var method);
        if (error is not null) return Result<CreditNoteDto>.Fail(error);

        var remaining = note.TotalAmount - note.Refunds.Sum(r => r.Amount);
        if (request.Amount > remaining)
            return Result<CreditNoteDto>.Fail("CREDIT_NOTE_REFUND_EXCEEDS",
                remaining <= 0m ? "คืนเงินครบยอดใบลดหนี้แล้ว" : $"ยอดคืนเงินเกินยอดที่ยังค้างคืน ({remaining:N2} บาท)", "amount");

        var refund = NewRefund(note.Id, request, method);
        note.Refunds.Add(refund);
        await repo.AddRefundAsync(refund, ct);
        await repo.AddEventAsync(Event(note, "credit_note.refunded",
            $"คืนเงินตามใบลดหนี้ {note.DocumentNo} {refund.Amount:N2} บาท ({PosMapper.ToMethodToken(refund.Method)})"), ct);
        await repo.SaveChangesAsync(ct);
        return Result<CreditNoteDto>.Ok(CreditNoteMapper.ToDto(note));
    }

    // ---------- report ----------

    public async Task<Result<CreditNoteReportDto>> ReportAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        if (user.Role is not (UserRole.Manager or UserRole.Office))
            return Forbidden<CreditNoteReportDto>("เฉพาะผู้จัดการหรือธุรการเท่านั้นที่ดูรายงานใบลดหนี้ได้");

        var today = DateOnly.FromDateTime(Now.AddHours(7));
        var start = from ?? new DateOnly(today.Year, today.Month, 1);
        var end = to ?? today;
        if (end < start)
            return Invalid<CreditNoteReportDto>("วันที่สิ้นสุดต้องไม่ก่อนวันที่เริ่มต้น", "to");
        if (end.DayNumber - start.DayNumber + 1 > ReportMaxDays)
            return Invalid<CreditNoteReportDto>($"ช่วงวันที่ต้องไม่เกิน {ReportMaxDays} วัน", "to");

        // วันที่ตามเวลาไทย → ช่วง UTC [00:00 ของวันแรก, 00:00 ของวันถัดจากวันสุดท้าย)
        var fromUtc = start.ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var toUtc = end.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7);
        var notes = await repo.SearchAsync(user.ShardKey, user.BranchId, fromUtc, toUtc, ct);
        var items = notes.OrderBy(n => n.IssuedAt).ThenBy(n => n.DocumentNo).Select(CreditNoteMapper.ToDto).ToList();

        var refunded = items.Sum(i => i.RefundedAmount);
        var total = items.Sum(i => i.TotalAmount);
        return Result<CreditNoteReportDto>.Ok(new CreditNoteReportDto(
            start, end, items.Count,
            items.Sum(i => i.DiffNetAmount), items.Sum(i => i.VatAmount), total,
            refunded, total - refunded, items));
    }

    // ---------- branch tax profile ----------

    public async Task<Result<BranchTaxProfileDto>> GetProfileAsync(CancellationToken ct = default) =>
        Result<BranchTaxProfileDto>.Ok(ToDto(await repo.GetProfileAsync(user.ShardKey, user.BranchId, ct)));

    public async Task<Result<BranchTaxProfileDto>> UpdateProfileAsync(
        UpdateBranchTaxProfileRequest request, CancellationToken ct = default)
    {
        if (!IsManager) return Forbidden<BranchTaxProfileDto>("เฉพาะผู้จัดการเท่านั้นที่แก้ค่าตั้งภาษีของสาขาได้");

        var branchNo = new string((request.SellerBranchNo ?? "").Where(char.IsDigit).ToArray());
        if (branchNo.Length != 5 || (request.SellerBranchNo ?? "").Trim().Length != 5)
            return Invalid<BranchTaxProfileDto>("เลขที่สาขาต้องเป็นตัวเลข 5 หลัก (สำนักงานใหญ่ = 00000)", "sellerBranchNo");

        var profile = await repo.GetProfileAsync(user.ShardKey, user.BranchId, ct);
        if (profile is null)
        {
            profile = new BranchTaxProfile { LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId };
            await repo.AddProfileAsync(profile, ct);
        }
        var before = profile.SellerBranchNo;
        profile.SellerBranchNo = branchNo;
        profile.UpdatedByName = user.UserName;
        profile.UpdatedAt = Now;
        await repo.AddEventAsync(new ActivityEvent
        {
            EntityType = nameof(BranchTaxProfile),
            EventType = "branch.tax_profile.changed",
            DescriptionTh = $"ตั้งสถานประกอบการของสาขาสำหรับเอกสารภาษี: {BranchLabel(before)} → {BranchLabel(branchNo)}",
            PayloadJson = JsonSerializer.Serialize(new { from = before, to = branchNo, user.ShardKey, user.BranchId }),
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await repo.SaveChangesAsync(ct);
        return Result<BranchTaxProfileDto>.Ok(ToDto(profile));
    }

    public static string BranchLabel(string? branchNo) =>
        branchNo is null or HeadOfficeBranchNo ? "สำนักงานใหญ่" : $"สาขาที่ {branchNo}";

    // ---------- helper ----------

    /// <summary>
    /// บิลที่ใบลดหนี้ของจ๊อบนี้ต้องอ้าง — มีใบกำกับแล้วต้องอ้างใบกำกับ (กันลดซ้ำสองทาง) ไม่งั้นอ้างใบเสร็จ
    /// บรรทัดของ RC- = บรรทัดที่อนุมัติของทุกใบเสนอราคาที่ใช้อยู่ (ชุดเดียวกับที่ TaxInvoiceService คัดลงใบกำกับ)
    /// ใบเสนอราคาถูกล็อกหลังออกใบเสร็จ (QUOTE_RECEIPT_ISSUED) จึงคงที่
    /// </summary>
    private async Task<Source?> BuildSourceAsync(Job job, CancellationToken ct)
    {
        var receipt = await pos.GetReceiptByJobAsync(job.Id, ct);
        if (receipt is null) return null;

        var invoice = await taxInvoices.GetByJobAsync(job.Id, ct);
        if (invoice is not null)
            return new Source(CreditNoteSourceType.TaxInvoice, receipt, invoice, invoice.DocumentNo, invoice.IssuedAt,
                invoice.VatRate, invoice.NetAmount,
                invoice.Lines.OrderBy(l => l.Sequence)
                    .Select(l => new SourceLine(l.Id, l.QuotationCode, l.Description, l.Unit, l.Quantity, l.NetAmount))
                    .ToList());

        var active = await quotations.GetActiveForJobAsync(job.Id, ct);
        foreach (var q in active) QuotationCalculator.ApplyQuotationTotals(q);
        var contributing = active.Where(q => q.Lines.Any(l => l.ApprovalStatus == LineApprovalStatus.Approved)).ToList();
        var lines = contributing
            .SelectMany(q => q.Lines
                .Where(l => l.ApprovalStatus == LineApprovalStatus.Approved)
                .OrderBy(l => l.Sequence)
                .Select(l => new SourceLine(l.Id, q.Code, l.Name, l.Unit, l.Quantity, l.NetAmount)))
            .ToList();
        var rate = receipt.VatAmount > 0m && contributing.Count > 0 ? contributing[0].VatRate : 0m;
        return new Source(CreditNoteSourceType.Receipt, receipt, null, receipt.DocumentNo, receipt.IssuedAt,
            rate, receipt.NetAmount, lines);
    }

    private static List<CreditPlanLineDto> BuildPlan(Source source, IReadOnlyList<CreditNote> notes)
    {
        var credited = notes
            .Where(n => n.SourceType == source.Type)
            .SelectMany(n => n.Lines)
            .GroupBy(l => l.SourceLineId)
            .ToDictionary(g => g.Key, g => (Qty: g.Sum(l => l.Quantity), Amount: g.Sum(l => l.Amount)));

        return source.Lines.Select(l =>
        {
            var c = credited.GetValueOrDefault(l.Id);
            return new CreditPlanLineDto(l.Id, l.QuotationCode, l.Description, l.Unit, l.Quantity, l.NetAmount,
                c.Qty, c.Amount, Math.Max(0m, l.Quantity - c.Qty), Math.Max(0m, l.NetAmount - c.Amount));
        }).ToList();
    }

    private static Result<CreditNoteLine> BuildLine(IssueCreditNoteLineRequest req, CreditPlanLineDto p)
    {
        if (req.Quantity.HasValue == req.Amount.HasValue)
            return Result<CreditNoteLine>.Fail("CREDIT_NOTE_VALIDATION",
                $"รายการ \"{p.Description}\" ต้องระบุจำนวนหรือยอดเงินที่ลดอย่างใดอย่างหนึ่ง", "lines");

        decimal quantity = 0m, amount;
        if (req.Quantity is { } qty)
        {
            if (qty <= 0m || decimal.Round(qty, 3) != qty)
                return Result<CreditNoteLine>.Fail("CREDIT_NOTE_VALIDATION",
                    $"จำนวนที่ลดของ \"{p.Description}\" ต้องมากกว่าศูนย์ (ทศนิยมไม่เกิน 3 ตำแหน่ง)", "lines");
            if (qty > p.RemainingQuantity)
                return Result<CreditNoteLine>.Fail("CREDIT_NOTE_EXCEEDS_LINE",
                    $"จำนวนที่ลดของ \"{p.Description}\" เกินจำนวนคงเหลือ ({p.RemainingQuantity:0.###} {p.Unit})", "lines");
            quantity = qty;
            // ลดจำนวนที่เหลือทั้งหมด = ยอดคงเหลือทั้งหมด (ไม่ให้เศษสตางค์ค้างจากการปัด)
            amount = qty == p.RemainingQuantity
                ? p.RemainingAmount
                : Math.Min(p.RemainingAmount, Round(p.OriginalNetAmount * qty / p.OriginalQuantity));
        }
        else
        {
            amount = req.Amount!.Value;
            if (amount <= 0m || decimal.Round(amount, 2) != amount)
                return Result<CreditNoteLine>.Fail("CREDIT_NOTE_VALIDATION",
                    $"ยอดที่ลดของ \"{p.Description}\" ต้องมากกว่าศูนย์ (ทศนิยมไม่เกิน 2 ตำแหน่ง)", "lines");
        }

        if (amount > p.RemainingAmount)
            return Result<CreditNoteLine>.Fail("CREDIT_NOTE_EXCEEDS_LINE",
                $"ยอดที่ลดของ \"{p.Description}\" เกินยอดคงเหลือ ({p.RemainingAmount:N2} บาท)", "lines");

        return Result<CreditNoteLine>.Ok(new CreditNoteLine
        {
            SourceLineId = p.SourceLineId,
            QuotationCode = p.QuotationCode,
            Description = p.Description,
            Unit = p.Unit,
            OriginalQuantity = p.OriginalQuantity,
            OriginalNetAmount = p.OriginalNetAmount,
            Quantity = quantity,
            Amount = amount
        });
    }

    private sealed record Seller(string Name, string? Address, string? TaxId, string? BranchNo);

    /// <summary>อ้างใบกำกับ = ผู้ขายตามสำเนาในใบกำกับ · อ้างใบเสร็จ = ข้อมูลสาขาปัจจุบัน (เลขผู้เสียภาษีไม่ครบ 13 หลัก = ไม่พิมพ์)</summary>
    private async Task<Seller> SellerAsync(Source source, BranchTaxProfile? profile, CancellationToken ct)
    {
        var branchNo = profile?.SellerBranchNo ?? HeadOfficeBranchNo;
        if (source.Invoice is { } iv)
            return new Seller(iv.SellerName, iv.SellerAddress, iv.SellerTaxId, iv.SellerBranchNo ?? branchNo);

        var branch = await legacy.GetBranchAsync(user.ShardKey, user.BranchId, ct);
        var taxId = new string((branch?.TaxId ?? "").Where(char.IsDigit).ToArray());
        return new Seller(
            branch?.Name ?? "",
            string.IsNullOrWhiteSpace(branch?.Address) ? null : branch!.Address!.Trim(),
            taxId.Length == 13 ? taxId : null,
            branchNo);
    }

    private static ApiError? ValidateRefundRequest(RecordCreditNoteRefundRequest r, out PaymentMethod method)
    {
        method = default;
        if (r.RequestId == Guid.Empty) return new("CREDIT_NOTE_VALIDATION", "ไม่พบ RequestId ของการคืนเงิน", "refund.requestId");
        var parsed = PosMapper.ParseMethodToken(r.Method ?? "");
        if (parsed is null) return new("CREDIT_NOTE_VALIDATION", "กรุณาเลือกช่องทางคืนเงิน", "refund.method");
        if (r.Amount <= 0m || decimal.Round(r.Amount, 2) != r.Amount)
            return new("CREDIT_NOTE_VALIDATION", "ยอดคืนเงินต้องมากกว่าศูนย์ (ทศนิยมไม่เกิน 2 ตำแหน่ง)", "refund.amount");
        if (r.Reference?.Trim().Length > 200)
            return new("CREDIT_NOTE_VALIDATION", "เลขอ้างอิงยาวเกิน 200 ตัวอักษร", "refund.reference");
        method = parsed.Value;
        return null;
    }

    private CreditNoteRefund NewRefund(Guid noteId, RecordCreditNoteRefundRequest r, PaymentMethod method) => new()
    {
        CreditNoteId = noteId,
        Method = method,
        Amount = r.Amount,
        Reference = string.IsNullOrWhiteSpace(r.Reference) ? null : r.Reference.Trim(),
        RequestId = r.RequestId,
        RefundedByUserId = user.UserId,
        RefundedByName = user.UserName,
        RefundedAt = Now
    };

    private ActivityEvent Event(CreditNote note, string type, string description) => new()
    {
        JobId = note.JobId,
        EntityId = note.Id,
        EntityType = nameof(CreditNote),
        EventType = type,
        DescriptionTh = description,
        PerformedByUserId = user.UserId,
        PerformedByName = user.UserName,
        Source = user.Source,
        OccurredAt = Now
    };

    private BranchTaxProfileDto ToDto(BranchTaxProfile? p) => p is null
        ? new BranchTaxProfileDto(HeadOfficeBranchNo, true, null, null, IsManager)
        : new BranchTaxProfileDto(p.SellerBranchNo, false, p.UpdatedByName, p.UpdatedAt, IsManager);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static string Hash<T>(T input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));

    private static Result<T> Forbidden<T>(string message = "เฉพาะผู้จัดการเท่านั้นที่ออกใบลดหนี้ได้") =>
        Result<T>.Fail("CREDIT_NOTE_FORBIDDEN", message);

    private static Result<T> Invalid<T>(string message, string field) =>
        Result<T>.Fail("CREDIT_NOTE_VALIDATION", message, field);
}
