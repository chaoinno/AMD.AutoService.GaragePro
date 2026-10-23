using AMD.AutoService.GaragePro.Application.SelfServe.Package;

namespace AMD.AutoService.GaragePro.Application.Internal;

// สัญญา JSON ของ /api/v1/internal/package-payments (docs/17 §8.6 ระยะ B) · GaragePro Admin อ่านตามชื่อฟิลด์เหล่านี้
// · ฟิลด์ของ PackagePayment (§5.1) อยู่ระดับบนสุดของรายการ (ไม่ซ้อนใต้ "payment") แล้วต่อด้วยข้อมูลของทีมงาน/อู่
// · ไม่ตัดยอดเงิน/หมายเหตุ/สลิปเหมือนฝั่งลูกค้า (ทีมงานต้องเห็นครบ) · ฟิลด์ null ไม่อยู่ใน JSON

/// <summary>
/// หนึ่งรายการแจ้งชำระสำหรับทีมงาน · <see cref="ReviewNote"/> = หมายเหตุของทีมงานทุกสถานะ (ลูกค้าเห็นเฉพาะตอนปฏิเสธเป็น rejectNote) ·
/// <see cref="Preview"/> เฉพาะรายการที่ยังรอตรวจและต่ออายุได้ · <see cref="CurrentEndsAt"/> = วันหมดปัจจุบันของอู่ในรายการ
/// (ผลอนุมัติ/ปฏิเสธไม่มีค่านี้ ใช้ endsFrom/endsTo ของผลแทน)
/// </summary>
public sealed record InternalPackagePaymentDto(
    string Id,
    string ShortId,
    string Status,
    int? MonthCount,
    decimal? Amount,
    decimal? ExpectedAmount,
    DateTime PaidAt,
    DateTime SubmittedAt,
    string? SubmittedBy,
    string? Note,
    PackageSlipDto? Slip,
    DateTime? PeriodStartsAt,
    DateTime? PeriodEndsAt,
    DateOnly? PeriodLastUsableDate,
    DateTime? ReviewedAt,
    string? RejectReason,
    string? RejectReasonTh,
    string? RejectNote,
    DateTime? CancelledAt,
    string? ReviewedBy,
    string? ReviewNote,
    string? CancelledBy,
    string WorkspaceNo,
    string WorkspaceName,
    int? BranchId,
    string Plan,
    string WorkspaceStatus,
    DateTime? CurrentEndsAt,
    IReadOnlyList<PackagePaymentDuplicateDto> Duplicates,
    PackageRenewalPreviewDto? Preview);

/// <summary>ผลอนุมัติ/ปฏิเสธ · <see cref="EndsFrom"/>/<see cref="EndsTo"/> เฉพาะอนุมัติ (ปฏิเสธ = ไม่อยู่ใน JSON)</summary>
public sealed record InternalPackageReviewDto(
    InternalPackagePaymentDto Payment,
    DateTime? EndsFrom,
    DateTime? EndsTo,
    bool EmailSent,
    bool InAppSaved);

/// <summary>
/// body ของ approve/reject ที่ controller อ่านจาก JSON เอง (ไม่ผ่าน model binding: ค่าผิดชนิดได้ 422 envelope ไม่ใช่ 400 ProblemDetails)
/// · ทุกค่าเป็นข้อความ (actorId ที่เป็น number ถูกแปลงเป็นข้อความ) ตรวจที่ <see cref="InternalPackagePaymentsService"/>
/// </summary>
public sealed record InternalReviewRequest(
    string? ActorName,
    string? ActorId,
    string? Until = null,
    string? Note = null,
    string? Reason = null);

public static class InternalPackagePaymentMapper
{
    public static InternalPackagePaymentDto ToDto(PackagePaymentAdminItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var p = item.Payment;
        return new InternalPackagePaymentDto(
            p.Id, p.ShortId, p.Status, p.MonthCount, p.Amount, p.ExpectedAmount, p.PaidAt, p.SubmittedAt, p.SubmittedBy,
            p.Note, p.Slip, p.PeriodStartsAt, p.PeriodEndsAt, p.PeriodLastUsableDate, p.ReviewedAt, p.RejectReason,
            p.RejectReasonTh, p.RejectNote, p.CancelledAt,
            item.ReviewedBy, item.ReviewNote, item.CancelledBy,
            item.WorkspaceNo, item.WorkspaceName, item.BranchId, item.Plan, item.WorkspaceStatus, item.CurrentEndsAt,
            item.Duplicates, item.Preview);
    }

    public static InternalPackageReviewDto ToDto(PackagePaymentReviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new InternalPackageReviewDto(ToDto(result.Payment), result.EndsFrom, result.EndsTo, result.EmailSent,
            result.InAppSaved);
    }
}
