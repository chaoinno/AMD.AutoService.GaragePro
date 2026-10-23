using AMD.AutoService.GaragePro.API.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.API.Internal;

/// <summary>
/// ด่านของ controller ภายในทุกตัว (docs/17 §8.6) · เป็น authorization filter จึงทำงานก่อน model binding และ action filter
/// (ไม่มีการอ่าน body ของคำขอที่ไม่มีคีย์)
/// · server ไม่ได้ตั้ง <see cref="InternalOptions.ApiKey"/> (หรือสั้นเกิน) → 404 เปล่า เหมือนไม่มี endpoint
/// · header <see cref="InternalApi.KeyHeader"/> ไม่มี · มีหลายค่า · ไม่ตรง → 401 envelope <c>INTERNAL_AUTH_REQUIRED</c>
/// · [SECURITY] ไม่ log ค่าคีย์ · คำตอบทุกแบบ <c>Cache-Control: no-store</c> + <c>nosniff</c>
/// · คู่กับ <c>[AllowAnonymous]</c> บน controller: ไม่ใช้ JWT ของผู้ใช้ และ SessionRevocationMiddleware/TrialReadOnlyFilter
///   ข้าม path นี้ (<see cref="InternalApi.IsInternalPath"/>)
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class InternalApiAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";

        var key = http.RequestServices.GetService<IOptions<InternalOptions>>()?.Value.EffectiveKey;
        if (key is null)
        {
            context.Result = new NotFoundResult();
            return;
        }

        var provided = http.Request.Headers[InternalApi.KeyHeader];
        if (provided.Count == 1 && InternalApi.KeyMatches(provided[0], key)) return;

        context.Result = new ObjectResult(new Envelope<object>(false, null, InternalApi.AuthRequired(), http.TraceIdentifier))
        {
            StatusCode = StatusCodes.Status401Unauthorized
        };
    }
}
