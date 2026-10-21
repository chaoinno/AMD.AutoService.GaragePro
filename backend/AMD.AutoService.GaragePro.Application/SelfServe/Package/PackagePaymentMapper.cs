using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>
/// entity → JSON ของ docs/17 §5.1 · [SECURITY] ตัดยอดเงิน/หมายเหตุ/สลิปที่ server เมื่อผู้ขอไม่ใช่ Manager
/// (กฎข้อ 2 · role อื่นเห็นสถานะ ไม่เห็นตัวเงิน) · ReviewNote แสดงเป็น rejectNote เฉพาะรายการที่ปฏิเสธ
/// (หมายเหตุตอนอนุมัติเป็นของทีมงาน ไม่ส่งให้ลูกค้า)
/// </summary>
public static class PackagePaymentMapper
{
    public static PackagePaymentDto ToDto(PackagePayment p, bool includeFinancials)
    {
        ArgumentNullException.ThrowIfNull(p);
        var approved = p.Status == PackagePaymentStatus.Approved;
        var rejected = p.Status == PackagePaymentStatus.Rejected;

        return new PackagePaymentDto(
            p.Id,
            PackagePaymentRules.ShortId(p.Id),
            PackagePaymentRules.StatusCode(p.Status),
            p.MonthCount,
            includeFinancials ? PackagePaymentRules.Money(p.Amount) : null,
            includeFinancials && p.ExpectedAmount is { } expected ? PackagePaymentRules.Money(expected) : null,
            p.PaidAt,
            p.SubmittedAt,
            p.SubmittedByUserName,
            includeFinancials ? p.Note : null,
            includeFinancials ? new PackageSlipDto(p.SlipContentType, p.SlipSizeBytes) : null,
            approved ? p.PeriodStartsAt : null,
            approved ? p.PeriodEndsAt : null,
            approved && p.PeriodEndsAt is { } end ? PackageRenewal.LastUsableDay(end) : null,
            p.ReviewedAt,
            rejected && p.RejectReason is { } reason ? PackagePaymentRules.RejectReasonCode(reason) : null,
            rejected && p.RejectReason is { } reasonTh ? PackagePaymentRules.RejectReasonTh(reasonTh) : null,
            rejected ? p.ReviewNote : null,
            p.CancelledAt);
    }
}
