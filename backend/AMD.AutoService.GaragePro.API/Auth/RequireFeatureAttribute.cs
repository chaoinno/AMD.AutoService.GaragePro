using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// [BIZ] ล็อกฟีเจอร์ขั้นสูงตามแพ็กเกจ (docs/12 §5) — แพ็กเกจที่ไม่มีฟีเจอร์นี้ได้ 403 <c>PLAN_FEATURE_LOCKED</c>
/// พร้อม <c>details.feature</c> (ชื่อ camelCase ชุดเดียวกับ <c>plan.features[]</c>) ให้เว็บ/แอปแสดงหน้าล็อกได้ตรงตัว
/// · ใช้ได้ทั้งระดับ controller และ action (ติดหลายตัวได้ — ต้องผ่านทุกตัว)
/// · legacy/paid = ทุกฟีเจอร์ → ผ่านเสมอ (WorkspacePolicy คืน Legacy ของ shard เดิมโดยไม่แตะฐาน)
/// · ไม่ได้ login → ปล่อยให้ [Authorize] ตอบ 401 ตามปกติ (filter นี้ไม่ใช่ตัวยืนยันตัวตน)
/// · resolve นโยบายจาก RequestServices ต่อคำขอ (attribute เป็น singleton ต่อ action — inject ผ่าน ctor ไม่ได้)
/// · <see cref="AllowReads"/> = ล็อกเฉพาะคำขอเขียน (GET/HEAD ผ่าน) — สำหรับข้อมูลหลักที่หน้าจอซึ่งเปิดให้ทดลองใช้ต้องอ่านประกอบ
///   (เช่น รายชื่อคลังในฟอร์มสินค้า) · ติดระดับ controller แล้ว action เขียนที่เพิ่มทีหลังยังถูกล็อกเองโดยไม่ต้องจำ
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = true)]
public sealed class RequireFeatureAttribute(PlanFeature feature) : ActionFilterAttribute
{
    public PlanFeature Feature { get; } = feature;

    /// <summary>
    /// true = GET/HEAD ผ่านทุกแพ็กเกจ ล็อกเฉพาะคำขอเขียน — [BIZ] อ่านข้อมูลหลักของอู่ตัวเองไม่ใช่ฟีเจอร์ที่ขาย ส่วนหน้าจัดการ
    /// ของฟีเจอร์นั้นถูกปิดที่เว็บด้วย FeatureGate อยู่แล้ว · ห้ามใช้กับ endpoint ที่ "การอ่าน" คือตัวฟีเจอร์เอง (รายงาน/สต็อก/ใบกำกับ)
    /// </summary>
    public bool AllowReads { get; set; }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var services = http.RequestServices;

        if ((AllowReads && (HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method)))
            || http.User.Identity?.IsAuthenticated != true
            || services.GetService(typeof(IWorkspacePolicy)) is not IWorkspacePolicy policy
            || policy is UnlimitedWorkspacePolicy
            || services.GetService(typeof(ICurrentUser)) is not ICurrentUser user)
        {
            await next();
            return;
        }

        var plan = await policy.GetAsync(user.ShardKey, user.BranchId, http.RequestAborted);
        if (plan.HasFeature(Feature))
        {
            await next();
            return;
        }

        context.Result = PlanErrorResult.Create(PlanErrors.FeatureLocked(Feature), http.TraceIdentifier);
    }
}
