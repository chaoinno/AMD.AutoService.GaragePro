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

public interface IPosService
{
    Task<Result<PaymentSummaryDto>> GetSummaryAsync(Guid jobId, CancellationToken ct = default);

    Task<Result<PaymentDto>> RecordPaymentAsync(
        Guid jobId, RecordPaymentRequest request, CancellationToken ct = default);

    Task<Result<bool>> RemovePaymentAsync(
        Guid jobId, Guid paymentId, string? reason, CancellationToken ct = default);

    Task<Result<PaymentReceiptDto>> IssueReceiptAsync(Guid jobId, CancellationToken ct = default);

    Task<Result<PaymentSummaryDto>> SetVatIncludedAsync(
        Guid jobId, bool included, CancellationToken ct = default);
}

/// <summary>
/// ชำระเงิน/ออกใบเสร็จ — MVP: บันทึกยอดเดียวต่อครั้ง ไม่มี split/EDC/QR gateway จริง ไม่มีใบกำกับภาษี/reprint/void
/// (docs/01-workflow.md §3.9 ตัดขอบเขตแล้ว) ยอดคงเหลือคำนวณตรงจาก ApprovedTotals ของใบเสนอราคาล่าสุด — ข้าม
/// ขั้น reconciliation [BIZ] RequestId+RequestHash กันบันทึกซ้ำ (invariant #8), เฉพาะ Cashier/Office/Manager
/// เท่านั้นที่ใช้โมดูลนี้ได้ (ตรงกับ role ที่อนุญาต transition Ready→Completed ใน JobStateMachine อยู่แล้ว)
/// </summary>
public sealed class PosService(
    IPosRepository repo,
    IJobRepository jobs,
    IQuotationRepository quotations,
    IReceiptNumberGenerator receiptNumbers,
    ICurrentUser user,
    TimeProvider clock) : IPosService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static string Hash<T>(T input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));

    public async Task<Result<PaymentSummaryDto>> GetSummaryAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<PaymentSummaryDto>.Fail(jobResult.Error!);

        var summary = await BuildSummaryAsync(jobResult.Data!, ct);
        if (summary is null)
            return Result<PaymentSummaryDto>.Fail(
                "POS_NO_QUOTATION", "งานนี้ยังไม่มีใบเสนอราคา — ไม่สามารถคำนวณยอดชำระได้");

        return Result<PaymentSummaryDto>.Ok(summary);
    }

    public async Task<Result<PaymentSummaryDto>> SetVatIncludedAsync(
        Guid jobId, bool included, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<PaymentSummaryDto>.Fail(jobResult.Error!);
        var job = jobResult.Data!;

        if ((await repo.GetPaymentsByJobAsync(jobId, ct)).Count > 0 || await repo.GetReceiptByJobAsync(jobId, ct) is not null)
            return Result<PaymentSummaryDto>.Fail(
                "POS_VAT_LOCKED", "เริ่มบันทึกการชำระเงินหรือออกใบเสร็จไปแล้ว — เปลี่ยนการคิด VAT ไม่ได้อีก");

        job.VatIncluded = included;
        await repo.AddEventAsync(new ActivityEvent
        {
            JobId = jobId,
            EntityId = jobId,
            EntityType = nameof(Job),
            EventType = "payment.vat.toggled",
            DescriptionTh = included ? "เปลี่ยนเป็นคิด VAT" : "เปลี่ยนเป็นไม่คิด VAT",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await jobs.SaveChangesAsync(ct);

        var summary = await BuildSummaryAsync(job, ct);
        if (summary is null)
            return Result<PaymentSummaryDto>.Fail(
                "POS_NO_QUOTATION", "งานนี้ยังไม่มีใบเสนอราคา — ไม่สามารถคำนวณยอดชำระได้");

        return Result<PaymentSummaryDto>.Ok(summary);
    }

    public async Task<Result<PaymentDto>> RecordPaymentAsync(
        Guid jobId, RecordPaymentRequest request, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<PaymentDto>.Fail(jobResult.Error!);

        var method = PosMapper.ParseMethodToken(request.Method);
        if (method is null)
            return Result<PaymentDto>.Fail(
                "POS_VALIDATION", "ช่องทางชำระเงินไม่ถูกต้อง", nameof(request.Method));

        if (request.Amount <= 0)
            return Result<PaymentDto>.Fail("POS_VALIDATION", "จำนวนเงินต้องมากกว่าศูนย์", nameof(request.Amount));

        if (request.RequestId == Guid.Empty)
            return Result<PaymentDto>.Fail("POS_VALIDATION", "ไม่พบ RequestId", nameof(request.RequestId));

        if (await repo.GetReceiptByJobAsync(jobId, ct) is not null)
            return Result<PaymentDto>.Fail("POS_RECEIPT_ISSUED", "งานนี้ออกใบเสร็จไปแล้ว — บันทึกชำระเงินเพิ่มไม่ได้");

        var hash = Hash(request);
        var existing = await repo.GetPaymentByRequestIdAsync(request.RequestId, ct);
        if (existing is not null)
        {
            if (existing.RequestHash != hash)
                return Result<PaymentDto>.Fail(
                    "POS_CONFLICT", "RequestId นี้เคยใช้บันทึกข้อมูลอื่นไปแล้ว — กรุณาลองใหม่");

            return Result<PaymentDto>.Ok(PosMapper.ToDto(existing));
        }

        var payment = new Payment
        {
            JobId = jobId,
            LegacyShardKey = user.ShardKey,
            LegacyBranchId = user.BranchId,
            Method = method.Value,
            Amount = request.Amount,
            Reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim(),
            RequestId = request.RequestId,
            RequestHash = hash,
            ReceivedByUserId = user.UserId,
            ReceivedByName = user.UserName,
            ReceivedAt = Now
        };

        await repo.AddPaymentAsync(payment, ct);
        await repo.AddEventAsync(new ActivityEvent
        {
            JobId = jobId,
            EntityId = payment.Id,
            EntityType = nameof(Payment),
            EventType = "payment.recorded",
            DescriptionTh = $"บันทึกชำระเงิน {payment.Amount:N2} บาท ({PosMapper.ToMethodToken(method.Value)})",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await repo.SaveChangesAsync(ct);

        return Result<PaymentDto>.Ok(PosMapper.ToDto(payment));
    }

    public async Task<Result<bool>> RemovePaymentAsync(
        Guid jobId, Guid paymentId, string? reason, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<bool>.Fail(jobResult.Error!);

        if (string.IsNullOrWhiteSpace(reason))
            return Result<bool>.Fail("POS_VALIDATION", "กรุณาระบุเหตุผลที่ลบรายการชำระเงินนี้", nameof(reason));

        if (await repo.GetReceiptByJobAsync(jobId, ct) is not null)
            return Result<bool>.Fail("POS_RECEIPT_ISSUED", "งานนี้ออกใบเสร็จไปแล้ว — ลบรายการชำระเงินไม่ได้");

        var payment = await repo.GetPaymentAsync(jobId, paymentId, ct);
        if (payment is null)
            return Result<bool>.Fail("POS_PAYMENT_NOT_FOUND", "ไม่พบรายการชำระเงินนี้");

        await repo.RemovePaymentAsync(payment, ct);
        await repo.AddEventAsync(new ActivityEvent
        {
            JobId = jobId,
            EntityId = payment.Id,
            EntityType = nameof(Payment),
            EventType = "payment.removed",
            DescriptionTh = $"ลบรายการชำระเงิน {payment.Amount:N2} บาท — เหตุผล: {reason.Trim()}",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await repo.SaveChangesAsync(ct);

        return Result<bool>.Ok(true);
    }

    public async Task<Result<PaymentReceiptDto>> IssueReceiptAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<PaymentReceiptDto>.Fail(jobResult.Error!);

        var existing = await repo.GetReceiptByJobAsync(jobId, ct);
        if (existing is not null)
            return Result<PaymentReceiptDto>.Ok(PosMapper.ToDto(existing));

        var summary = await BuildSummaryAsync(jobResult.Data!, ct);
        if (summary is null)
            return Result<PaymentReceiptDto>.Fail(
                "POS_NO_QUOTATION", "งานนี้ยังไม่มีใบเสนอราคา — ไม่สามารถออกใบเสร็จได้");

        if (!summary.BalanceSettled)
            return Result<PaymentReceiptDto>.Fail(
                "POS_BALANCE_NOT_SETTLED", "ยอดคงเหลือยังไม่เป็นศูนย์ — บันทึกชำระเงินให้ครบก่อนออกใบเสร็จ");

        var docNo = await receiptNumbers.NextAsync(user.ShardKey, user.BranchId, Now.AddHours(7), ct);

        var receipt = new Receipt
        {
            JobId = jobId,
            DocumentNo = docNo,
            NetAmount = summary.NetAmount,
            VatAmount = summary.VatAmount,
            TotalAmount = summary.GrandTotal,
            IssuedByUserId = user.UserId,
            IssuedByName = user.UserName,
            IssuedAt = Now
        };

        await repo.AddReceiptAsync(receipt, ct);
        await repo.AddEventAsync(new ActivityEvent
        {
            JobId = jobId,
            EntityId = receipt.Id,
            EntityType = nameof(Receipt),
            EventType = "payment.receipt.issued",
            DescriptionTh = $"ออกใบเสร็จ {docNo} ยอดรวม {receipt.TotalAmount:N2} บาท",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await repo.SaveChangesAsync(ct);

        return Result<PaymentReceiptDto>.Ok(PosMapper.ToDto(receipt));
    }

    private async Task<PaymentSummaryDto?> BuildSummaryAsync(Job job, CancellationToken ct)
    {
        var quotation = await quotations.GetLatestForJobAsync(job.Id, ct);
        if (quotation is null) return null;

        QuotationCalculator.ApplyQuotationTotals(quotation);
        var approved = QuotationCalculator.CalculateApprovedTotals(quotation, job.VatIncluded);

        var payments = await repo.GetPaymentsByJobAsync(job.Id, ct);
        var paid = payments.Sum(p => p.Amount);
        var remaining = Math.Max(0m, Math.Round(approved.GrandTotal - paid, 2, MidpointRounding.AwayFromZero));
        var receipt = await repo.GetReceiptByJobAsync(job.Id, ct);

        return new PaymentSummaryDto(
            job.Id,
            approved.NetAmount,
            approved.VatAmount,
            approved.GrandTotal,
            paid,
            remaining,
            BalanceSettled: Math.Round(approved.GrandTotal - paid, 2, MidpointRounding.AwayFromZero) <= 0m,
            VatIncluded: job.VatIncluded,
            VatLocked: payments.Count > 0 || receipt is not null,
            payments.Select(PosMapper.ToDto).ToList(),
            receipt is null ? null : PosMapper.ToDto(receipt));
    }

    private async Task<Result<Job>> ValidateAsync(Guid jobId, CancellationToken ct)
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
