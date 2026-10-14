using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// error code ของ endpoint <c>/api/v1/selfserve/*</c> และการลบข้อมูลอู่ (S4) ที่ไม่อยู่ใน <see cref="SelfServeErrors"/>
/// · ข้อความไทยพร้อมแสดงผล — client ห้ามแปล/แต่งใหม่ · HTTP status ดู <see cref="StatusCodeFor"/>
/// </summary>
public static class SelfServeLifecycleErrors
{
    public const string ValidationCode = "SELFSERVE_VALIDATION";                    // 422
    public const string NotifyFailedCode = "SELFSERVE_NOTIFY_FAILED";               // 502
    public const string SampleUnavailableCode = "SELFSERVE_SAMPLE_UNAVAILABLE";     // 503
    public const string SampleFailedCode = "SELFSERVE_SAMPLE_FAILED";               // 503
    public const string WorkspaceNotFoundCode = "SELFSERVE_WORKSPACE_NOT_FOUND";    // 404
    public const string PurgeRefusedCode = "SELFSERVE_PURGE_REFUSED";               // 409

    public static ApiError Validation(string messageTh, string? field = null) =>
        new(ValidationCode, messageTh, field);

    public static ApiError NotifyFailed() => new(NotifyFailedCode,
        $"ส่งคำขอไปยังทีมงานไม่สำเร็จ — กรุณาลองใหม่อีกครั้ง หรือติดต่อ {SelfServeText.ContactLine} / โทร {SelfServeText.ContactPhone}");

    public static ApiError SampleUnavailable() => new(SampleUnavailableCode,
        $"ระบบข้อมูลตัวอย่างยังไม่พร้อมใช้งาน — กรุณาลองใหม่ภายหลัง หรือติดต่อ {SelfServeText.ContactLine}");

    public static ApiError SampleFailed() => new(SampleFailedCode,
        "สร้างข้อมูลตัวอย่างไม่สำเร็จ — กรุณาลองใหม่อีกครั้ง");

    public static ApiError WorkspaceNotFound() => new(WorkspaceNotFoundCode,
        "ไม่พบอู่ที่สมัครทดลองใช้รายการนี้");

    /// <summary>ใช้กับการลบข้อมูลอู่เท่านั้น (ทีมงาน/job รายวัน) — ไม่มีหน้าจอของผู้ใช้เรียก</summary>
    public static ApiError PurgeRefused(string reasonTh) => new(PurgeRefusedCode,
        $"ไม่ลบข้อมูลอู่: {reasonTh}");

    public static int? StatusCodeFor(string? code) => code switch
    {
        ValidationCode => 422,
        NotifyFailedCode => 502,
        SampleUnavailableCode => 503,
        SampleFailedCode => 503,
        WorkspaceNotFoundCode => 404,
        PurgeRefusedCode => 409,
        _ => null
    };
}
