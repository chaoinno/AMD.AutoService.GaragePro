using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// [BIZ] ช่วงอ่านอย่างเดียวของอู่ที่สมัครเอง (หลัง EndPackageDate · docs/12 §6) — ดู/พิมพ์ได้ แต่คำขอเขียนทุกตัวได้
/// 403 <c>TRIAL_EXPIRED</c> · บังคับที่ API เป็น global filter (ไม่ใช่แค่ซ่อนปุ่ม) จึงครอบ endpoint ใหม่ในอนาคตเองด้วย
/// · ข้าม: คำขอที่ไม่ได้ login · GET/HEAD/OPTIONS · <c>/api/v1/auth</c> (login/me/สลับสาขา) · <c>/api/v1/public</c> (signup) ·
///   <c>/api/v1/selfserve</c> (ขอต่ออายุ — ต้องทำได้ตอนหมดอายุ) · <c>/api/v1/notifications</c> (แอปมือถือกดอ่านแจ้งเตือน
///   อยู่เบื้องหลัง ถ้าบล็อกจะเด้ง error ใส่ผู้ใช้ทุก 30 วิ) · action/controller ที่ติด <see cref="AllowWhenTrialExpiredAttribute"/>
/// · fast path: token ที่ไม่มี claim <c>plan</c> (หรือเป็น legacy) และไม่ใช่ shard ของ self-serve → ผ่านทันทีโดยไม่ถามอะไร
///   (ลูกค้าเดิมไม่ช้าลง) · token ของ sp1 ถามนโยบายจริงเสมอ (cache 60 วิใน WorkspacePolicy) ไม่เชื่อ claim อย่างเดียว
///   เพราะทีมงานอาจเลื่อนวัน/ระงับหลังออก token
/// · ไม่มีผลเลยเมื่อไม่ได้ตั้ง self-serve (นโยบายเป็น <see cref="UnlimitedWorkspacePolicy"/>)
/// </summary>
public sealed class TrialReadOnlyFilter(
    IWorkspacePolicy policy,
    ICurrentUser currentUser,
    IOptions<SelfServeOptions> options) : IAsyncActionFilter
{
    private static readonly PathString[] ExemptPrefixes =
    [
        "/api/v1/auth",
        "/api/v1/public",
        "/api/v1/selfserve",
        "/api/v1/notifications"
    ];

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (policy is UnlimitedWorkspacePolicy || IsExempt(context))
        {
            await next();
            return;
        }

        var selfServeShard = string.Equals(currentUser.ShardKey, options.Value.ShardKey, StringComparison.OrdinalIgnoreCase);
        if (!selfServeShard && currentUser.Plan is null or PlanKind.Legacy)
        {
            await next();
            return;
        }

        var plan = await policy.GetAsync(currentUser.ShardKey, currentUser.BranchId, context.HttpContext.RequestAborted);
        if (!plan.IsReadOnly)
        {
            await next();
            return;
        }

        context.Result = PlanErrorResult.Create(
            PlanAccess.ReadOnlyError(plan, options.Value.Trial.Days), context.HttpContext.TraceIdentifier);
    }

    private static bool IsExempt(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        if (http.User.Identity?.IsAuthenticated != true) return true;

        var method = http.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)) return true;

        var path = http.Request.Path;
        if (ExemptPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))) return true;

        return context.ActionDescriptor.EndpointMetadata.OfType<AllowWhenTrialExpiredAttribute>().Any();
    }
}
