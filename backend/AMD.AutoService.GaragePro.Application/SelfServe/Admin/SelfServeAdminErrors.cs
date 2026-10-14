using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Admin;

/// <summary>
/// error code ของเครื่องมือทีมงาน (tools/spadmin) — ไม่มีหน้าจอของลูกค้าเรียก จึงไม่มี HTTP status
/// · ข้อความไทยพิมพ์ออก terminal ตรงๆ ให้ทีมงานอ่าน (ไม่มี client แปลต่อ)
/// </summary>
public static class SelfServeAdminErrors
{
    public const string ValidationCode = "SPADMIN_VALIDATION";
    public const string RefusedCode = "SPADMIN_REFUSED";
    public const string ConversionRefusedCode = "SPADMIN_CONVERSION_REFUSED";
    public const string ConversionTargetInvalidCode = "SPADMIN_CONVERSION_TARGET_INVALID";

    public static ApiError Validation(string messageTh, string? field = null) => new(ValidationCode, messageTh, field);

    /// <summary>คำสั่งถูกต้องแต่สถานะของอู่ไม่ยอมให้ทำ (เช่น ต่ออายุอู่ที่ลบแล้ว)</summary>
    public static ApiError Refused(string reasonTh) => new(RefusedCode, reasonTh);

    public static ApiError ConversionRefused(string reasonTh) => new(ConversionRefusedCode, $"ไม่ย้ายข้อมูล: {reasonTh}");

    public static ApiError ConversionTargetInvalid(string reasonTh) =>
        new(ConversionTargetInvalidCode, $"สาขา/ผู้ใช้ปลายทางไม่ถูกต้อง: {reasonTh}");
}
