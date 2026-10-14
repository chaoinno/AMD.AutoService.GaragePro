using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// รหัส error ของข้อมูลตัวอย่าง (S3) — แยกจาก <see cref="SelfServeErrors"/> (C0) เพื่อไม่ต้องแก้ไฟล์กลาง
/// · <c>messageTh</c> แสดงตรงตัวที่ client (ห้ามแปล/แต่งใหม่)
/// · สิทธิ์ (เจ้าของอู่/ผู้จัดการเท่านั้น) ใช้ <see cref="SelfServeErrors.SampleForbidden"/> ของเดิม — ผู้เรียก (S4) ตรวจก่อนเรียก
/// </summary>
public static class StarterDataErrors
{
    public const string WorkspaceNotReadyCode = "SELFSERVE_SAMPLE_WORKSPACE_NOT_READY"; // 409
    public const string ClearIncompleteCode = "SELFSERVE_SAMPLE_CLEAR_INCOMPLETE";      // 500

    public static ApiError WorkspaceNotReady() => new(WorkspaceNotReadyCode,
        "อู่นี้ยังสร้างไม่เสร็จหรือถูกปิดไปแล้ว จึงจัดการข้อมูลตัวอย่างไม่ได้ — ลองใหม่อีกครั้งในอีกสักครู่");

    /// <param name="summary">สรุปขั้นที่ล้ม (ไม่มีรหัสผ่าน/connection string) — ส่งไปใน details ให้ทีมงานไล่ปัญหาได้</param>
    public static ApiError ClearIncomplete(string? summary) => new(ClearIncompleteCode,
        $"ล้างข้อมูลตัวอย่างได้ไม่ครบ — กดล้างอีกครั้ง หรือติดต่อ {PlanErrors.ContactLine}",
        Details: string.IsNullOrWhiteSpace(summary) ? null : new { summary });

    /// <summary>HTTP status ของรหัสในชุดนี้ — null = ไม่ใช่รหัสของข้อมูลตัวอย่าง</summary>
    public static int? StatusCodeFor(string? code) => code switch
    {
        WorkspaceNotReadyCode => 409,
        ClearIncompleteCode => 500,
        _ => null
    };
}
