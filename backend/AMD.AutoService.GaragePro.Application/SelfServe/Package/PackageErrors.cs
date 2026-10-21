using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>
/// error code ของการแจ้งชำระ/ต่ออายุแพ็กเกจ (docs/17 §5.1 · สัญญากับเว็บ แอป และ API ภายในของ GaragePro Admin)
/// · ข้อความไทยพร้อมแสดงผล client ห้ามแปล/แต่งใหม่ · HTTP status ดู <see cref="StatusCodeFor"/>
/// · รหัสไฟล์สลิป (ATTACHMENT_*) ใช้ชุดเดียวกับไฟล์แนบของจ๊อบ (FtpAttachmentStorage.Validate)
/// </summary>
public static class PackageErrors
{
    public const string ForbiddenCode = "PACKAGE_PAYMENT_FORBIDDEN";               // 403
    public const string UnavailableCode = "PACKAGE_PAYMENT_UNAVAILABLE";           // 503
    public const string ValidationCode = "PACKAGE_PAYMENT_VALIDATION";             // 422 (+field)
    public const string PendingExistsCode = "PACKAGE_PAYMENT_PENDING_EXISTS";      // 409
    public const string NotRenewableCode = "PACKAGE_NOT_RENEWABLE";                // 409
    public const string NotFoundCode = "PACKAGE_PAYMENT_NOT_FOUND";                // 404
    public const string NotPendingCode = "PACKAGE_PAYMENT_NOT_PENDING";            // 409
    public const string ApprovalConflictCode = "PACKAGE_APPROVAL_CONFLICT";        // 409
    public const string SlipUploadFailedCode = "PACKAGE_SLIP_UPLOAD_FAILED";       // 503

    public const string AttachmentEmptyCode = "ATTACHMENT_EMPTY";                  // 422
    public const string AttachmentTypeInvalidCode = "ATTACHMENT_TYPE_INVALID";     // 422
    public const string AttachmentTooLargeCode = "ATTACHMENT_TOO_LARGE";           // 413

    public static ApiError Forbidden() => new(ForbiddenCode,
        "เฉพาะผู้จัดการอู่เท่านั้นที่แจ้งชำระเงินและดูรายละเอียดการชำระได้");

    public static ApiError Unavailable() => new(UnavailableCode,
        $"ยังไม่เปิดให้แจ้งชำระผ่านระบบ กรุณาติดต่อ {SelfServeText.ContactLine} / โทร {SelfServeText.ContactPhone}");

    public static ApiError Validation(string messageTh, string field) => new(ValidationCode, messageTh, field);

    public static ApiError PendingExists() => new(PendingExistsCode,
        "มีรายการแจ้งชำระรอตรวจอยู่แล้ว ทีมงานจะตรวจภายใน 1 วันทำการ");

    public static ApiError NotRenewable(string reasonTh) => new(NotRenewableCode, reasonTh);

    public static ApiError NotFound() => new(NotFoundCode, "ไม่พบรายการแจ้งชำระนี้");

    public static ApiError NotPending() => new(NotPendingCode,
        "รายการนี้ไม่ได้อยู่ระหว่างรอตรวจแล้ว (อนุมัติ ปฏิเสธ หรือยกเลิกไปแล้ว) กรุณารีเฟรชหน้า");

    public static ApiError ApprovalConflict() => new(ApprovalConflictCode,
        "วันหมดอายุของอู่เปลี่ยนระหว่างอนุมัติ (อาจมีการต่ออายุพร้อมกัน) ไม่ได้บันทึกอะไร กรุณาลองใหม่");

    public static ApiError SlipUploadFailed() => new(SlipUploadFailedCode,
        "เก็บไฟล์สลิปไม่สำเร็จ ยังไม่ได้บันทึกการแจ้งชำระ กรุณาลองใหม่อีกครั้ง", "slip");

    public static ApiError AttachmentEmpty() => new(AttachmentEmptyCode, "ไฟล์สลิปไม่มีข้อมูล", "slip");

    public static ApiError AttachmentTypeInvalid() => new(AttachmentTypeInvalidCode,
        "สลิปต้องเป็นรูป PNG, JPEG, WebP หรือไฟล์ PDF", "slip");

    public static ApiError AttachmentTooLarge(long maxBytes) => new(AttachmentTooLargeCode,
        $"ไฟล์สลิปต้องมีขนาดไม่เกิน {maxBytes / 1024 / 1024} MB", "slip");

    /// <summary>null = ไม่ใช่รหัสของชุดนี้</summary>
    public static int? StatusCodeFor(string? code) => code switch
    {
        ForbiddenCode => 403,
        UnavailableCode => 503,
        ValidationCode => 422,
        PendingExistsCode => 409,
        NotRenewableCode => 409,
        NotFoundCode => 404,
        NotPendingCode => 409,
        ApprovalConflictCode => 409,
        SlipUploadFailedCode => 503,
        AttachmentEmptyCode => 422,
        AttachmentTypeInvalidCode => 422,
        AttachmentTooLargeCode => 413,
        _ => null
    };
}
