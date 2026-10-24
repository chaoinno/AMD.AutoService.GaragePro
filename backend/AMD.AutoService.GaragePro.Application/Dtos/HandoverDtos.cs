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
///
/// [BIZ] นัดครั้งถัดไปเป็นหลายรายการ (<see cref="ServiceItems"/>) — ฟิลด์ NextService* เดี่ยวเดิมยังคงอยู่
/// เติมจากรายการที่ครบเร็วที่สุด เพื่อให้แอปรุ่นเก่าที่อยู่บน store ยังแสดงผลได้
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
    DateOnly? NextServiceDueOn = null,
    IReadOnlyList<HandoverServiceItemDto>? ServiceItems = null);

public sealed record HandoverServiceItemDto(
    Guid Id, Guid? TemplateId, string Name, int NextServiceMileage, int NextServiceMonths, DateOnly NextServiceDueOn);

/// <summary>[BIZ] Note บังคับเมื่อ IsReturned = false</summary>
public sealed record SaveHandoverItemRequest(bool IsReturned, string? Note);

public sealed record SubmitHandoverRequest(string SignatureAttachmentPath);

public sealed record HandoverServiceItemRequest(Guid? TemplateId, string Name, int NextServiceMileage, int NextServiceMonths);

/// <summary>
/// เลขไมล์ตอนส่งมอบ + นัดครั้งถัดไป (ไมล์ และจำนวนเดือน — ระบบคำนวณวันที่ให้)
/// รูปใหม่ (2026-10-25): <see cref="Items"/> หลายรายการ · รูปเดิม: <see cref="NextServiceMileage"/>/<see cref="NextServiceMonths"/>
/// ตัวเดียว — ยังรับอยู่ (แปลงเป็น 1 รายการชื่อ "เช็คระยะ") เพราะแอปรุ่นที่อยู่บน store ยังส่งรูปนี้
/// ถ้ามี Items (แม้ว่าง) ใช้ Items และไม่สนฟิลด์เดิม
/// </summary>
public sealed record SaveHandoverServiceInfoRequest(
    int MileageAtHandover,
    int? NextServiceMileage = null,
    int? NextServiceMonths = null,
    IReadOnlyList<HandoverServiceItemRequest>? Items = null);

/// <summary>รายการนัดตั้งต้นของสาขา — ผู้จัดการตั้ง ทุกบทบาทอ่านได้</summary>
public sealed record ServiceReminderTemplateDto(
    Guid Id, string Name, int DefaultMileageInterval, int DefaultMonths, int SortOrder, bool IsActive);

public sealed record ServiceReminderTemplateUpsertRequest(
    string Name, int DefaultMileageInterval, int DefaultMonths, int SortOrder = 0);

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
        Earliest(record)?.NextServiceMileage,
        Earliest(record)?.NextServiceMonths,
        Earliest(record)?.NextServiceDueOn,
        record.ServiceItems.OrderBy(i => i.SortOrder).ThenBy(i => i.NextServiceDueOn).Select(ToServiceItemDto).ToList());

    /// <summary>รายการที่ครบกำหนดเร็วที่สุด — ใช้เติมฟิลด์นัดเดี่ยวเดิมและเป็นวันนัดของรถในรายงาน</summary>
    public static HandoverServiceItem? Earliest(HandoverRecord record) =>
        record.ServiceItems.OrderBy(i => i.NextServiceDueOn).ThenBy(i => i.SortOrder).FirstOrDefault();

    public static HandoverServiceItemDto ToServiceItemDto(HandoverServiceItem i) =>
        new(i.Id, i.TemplateId, i.Name, i.NextServiceMileage, i.NextServiceMonths, i.NextServiceDueOn);

    public static HandoverChecklistItemDto ToItemDto(HandoverChecklistItem item) => new(
        item.Id, item.ItemCode, item.Name, item.IsReturned, item.Note, item.UpdatedAt, item.UpdatedByUserName);
}
