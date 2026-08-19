using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// ปฏิเสธ token ขั้นแรกที่ยังไม่ได้เลือกสาขา/กะ
///
/// [BIZ] สาขาและกะที่เลือกกำหนดคิวงานและคลังทั้งวัน (docs/01-workflow.md §4)
/// endpoint ที่ทำงานกับข้อมูลของสาขาจึงต้องมี session เสมอ ไม่งั้น BranchId = 0
/// แล้วจะไปอ่าน/เขียนผิดสาขา
/// </summary>
public sealed class RequireShiftSessionAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;

        if (user.HasClaim(c => c.Type == GarageClaims.PreSession))
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                data = (object?)null,
                error = new
                {
                    code = "AUTH_SHIFT_REQUIRED",
                    messageTh = "กรุณาเลือกสาขาและกะก่อนเริ่มใช้งาน"
                },
                traceId = context.HttpContext.TraceIdentifier
            })
            { StatusCode = StatusCodes.Status403Forbidden };
            return;
        }

        if (!user.HasClaim(c => c.Type == GarageClaims.BranchId))
        {
            context.Result = new ObjectResult(new
            {
                success = false,
                data = (object?)null,
                error = new
                {
                    code = "AUTH_SHIFT_REQUIRED",
                    messageTh = "โทเคนนี้ยังไม่ผูกกับสาขา — เข้าสู่ระบบใหม่แล้วเลือกสาขาและกะ"
                },
                traceId = context.HttpContext.TraceIdentifier
            })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
