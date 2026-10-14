using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.Application.Common;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// คำตอบ error ของ filter แพ็กเกจ (TrialReadOnlyFilter · RequireFeature) — envelope เดียวกับ controller
/// <c>{success:false, error:{code, messageTh, details?}, traceId}</c> (data = null ถูกตัดทิ้งเหมือนทุกคำตอบ) ·
/// รูปเดียวกับ RequireShiftSessionAttribute แต่ส่ง details ของ PLAN_FEATURE_LOCKED ไปด้วยให้ client ไม่ต้อง parse ข้อความ
/// </summary>
internal static class PlanErrorResult
{
    public static ObjectResult Create(ApiError error, string traceId) =>
        new(new Envelope<object>(false, null, error, traceId))
        {
            StatusCode = PlanErrors.StatusCodeFor(error.Code) ?? StatusCodes.Status403Forbidden
        };
}
