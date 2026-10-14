using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.Auth;

/// <summary>
/// error code ใหม่ของ B5 (login ด้วย Google · พนักงานของอู่ที่สมัครเอง) — นอกเหนือจาก PlanErrors/SelfServeErrors ของ C0
/// · messageTh พร้อมแสดงผล client ห้ามแปลหรือแต่งใหม่ · HTTP status ดู <see cref="StatusCodeFor"/>
/// </summary>
public static class SelfServeAuthErrors
{
    public const string GoogleNotRegisteredCode = "AUTH_GOOGLE_NOT_REGISTERED";   // 404
    public const string GoogleDisabledCode = "AUTH_GOOGLE_DISABLED";              // 503
    public const string StaffRoleRequiredCode = "STAFF_ROLE_REQUIRED";            // 422
    public const string StaffWorkspaceNotFoundCode = "STAFF_WORKSPACE_NOT_FOUND"; // 422
    public const string StaffPasswordTooShortCode = "STAFF_PASSWORD_TOO_SHORT";   // 422
    public const string StaffAccountConflictCode = "STAFF_ACCOUNT_CONFLICT";      // 409

    /// <summary>รหัสผ่านที่เจ้าของอู่ตั้งใหม่ให้พนักงาน sp1 ต้องยาวอย่างน้อยเท่านี้ (เท่ากฎตอนสมัคร)</summary>
    public const int MinimumStaffPasswordLength = 8;

    public static ApiError GoogleNotRegistered() => new(GoogleNotRegisteredCode,
        "ยังไม่มีบัญชีสำหรับอีเมลนี้ สมัครทดลองใช้ได้ที่หน้า ทดลองใช้ฟรี");

    public static ApiError GoogleDisabled() => new(GoogleDisabledCode,
        $"เข้าสู่ระบบด้วย Google ยังไม่เปิดให้บริการ ใช้อีเมลและรหัสผ่าน หรือติดต่อ {PlanErrors.ContactLine}");

    public static ApiError StaffRoleRequired() => new(StaffRoleRequiredCode,
        "กรุณาเลือกบทบาทของพนักงาน", "role");

    public static ApiError StaffWorkspaceNotFound() => new(StaffWorkspaceNotFoundCode,
        $"ไม่พบข้อมูลอู่ของสาขานี้ จึงสร้างบัญชีพนักงานไม่ได้ ติดต่อ {PlanErrors.ContactLine}");

    public static ApiError StaffPasswordTooShort() => new(StaffPasswordTooShortCode,
        $"รหัสผ่านใหม่ต้องยาวอย่างน้อย {MinimumStaffPasswordLength} ตัวอักษร", "password");

    public static ApiError StaffAccountConflict() => new(StaffAccountConflictCode,
        "สร้างชื่อผู้ใช้ของพนักงานไม่สำเร็จเพราะมีการเพิ่มพนักงานพร้อมกัน กรุณาลองอีกครั้ง");

    /// <summary>403 — เขียนไม่ได้เพราะแพ็กเกจแบบชำระเงินสิ้นสุดแล้ว/ย้ายไปสาขาจริงแล้ว (code เดียวกับ trial ให้ client จัดการทางเดียว)</summary>
    public static ApiError PaidPlanReadOnly() => new(PlanErrors.TrialExpiredCode,
        $"แพ็กเกจของอู่สิ้นสุดแล้ว ดูและพิมพ์ข้อมูลได้ แต่แก้ไขไม่ได้ ติดต่อ {PlanErrors.ContactLine} เพื่อต่ออายุ");

    public static int? StatusCodeFor(string? code) => code switch
    {
        GoogleNotRegisteredCode => 404,
        GoogleDisabledCode => 503,
        StaffRoleRequiredCode or StaffWorkspaceNotFoundCode or StaffPasswordTooShortCode => 422,
        StaffAccountConflictCode => 409,
        _ => null
    };
}
