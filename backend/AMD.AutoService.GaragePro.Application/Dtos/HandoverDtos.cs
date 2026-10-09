using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record HandoverChecklistItemDto(
    Guid Id,
    string ItemCode,
    string Name,
    bool IsReturned,
    string? Note,
    DateTime? UpdatedAt,
    string? UpdatedByUserName);

/// <summary>
/// [BIZ] ReceiptIssued/ReceiptDocumentNo อยู่ที่นี่เพื่อให้หน้าส่งมอบบอกได้ว่า "ทำไมยังเซ็นไม่ได้"
/// โดยไม่ต้องเรียก /payments ซึ่งเปิดเฉพาะ Cashier/Office/Manager — ช่างที่ยืนอยู่ข้างรถจะโดน 403
/// ส่งเฉพาะ "ออกแล้วหรือยัง" กับเลขที่เอกสาร ไม่มียอดเงินใดๆ (docs/01-workflow.md §4: ช่างไม่เห็นตัวเงิน)
/// </summary>
public sealed record HandoverDto(
    Guid Id,
    Guid JobId,
    bool IsLocked,
    string? SignatureImagePath,
    DateTime? SubmittedAt,
    string? SubmittedByUserName,
    bool ReceiptIssued,
    string? ReceiptDocumentNo,
    IReadOnlyList<HandoverChecklistItemDto> Items,
    int? MileageAtIntake = null,
    int? MileageAtHandover = null,
    int? NextServiceMileage = null,
    int? NextServiceMonths = null,
    DateOnly? NextServiceDueOn = null);

/// <summary>[BIZ] Note บังคับเมื่อ IsReturned = false</summary>
public sealed record SaveHandoverItemRequest(bool IsReturned, string? Note);

public sealed record SubmitHandoverRequest(string SignatureAttachmentPath);

/// <summary>เลขไมล์ตอนส่งมอบ + นัดครั้งถัดไป (ไมล์ และจำนวนเดือน — ระบบคำนวณวันที่ให้)</summary>
public sealed record SaveHandoverServiceInfoRequest(int MileageAtHandover, int NextServiceMileage, int NextServiceMonths);

public static class HandoverMapper
{
    /// <param name="job">ใช้เติมไมล์ขณะรับรถให้หน้าส่งมอบเทียบกับไมล์ส่งมอบ</param>
    public static HandoverDto ToDto(HandoverRecord record, Receipt? receipt, Job job) => new(
        record.Id,
        record.JobId,
        record.IsLocked,
        record.SignatureImagePath,
        record.SubmittedAt,
        record.SubmittedByUserName,
        receipt is not null,
        receipt?.DocumentNo,
        record.Items.OrderBy(i => i.ItemCode).Select(ToItemDto).ToList(),
        job.MileageAtIntake,
        record.MileageAtHandover,
        record.NextServiceMileage,
        record.NextServiceMonths,
        record.NextServiceDueOn);

    public static HandoverChecklistItemDto ToItemDto(HandoverChecklistItem item) => new(
        item.Id, item.ItemCode, item.Name, item.IsReturned, item.Note, item.UpdatedAt, item.UpdatedByUserName);
}
