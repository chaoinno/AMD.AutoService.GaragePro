using AMD.AutoService.GaragePro.API.Internal;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Internal;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// ทุก site ของ ServicePro (อู่สมัครเอง sp1 + สาขากลุ่ม Service ของ db2) ให้หน้า /branches ของ GaragePro Admin (docs/17 §8.6)
/// · ยืนยันตัวด้วย <c>X-Internal-Key</c> (<see cref="InternalApiAttribute"/>) ไม่ใช่ JWT · อ่านอย่างเดียว
/// · อ่านฐานไม่สำเร็จ = 503 envelope <c>INTERNAL_SOURCE_UNAVAILABLE</c> (ไม่ปล่อย 500 ดิบ · รายละเอียดอยู่ใน log)
/// </summary>
[ApiController]
[Route(InternalApi.RoutePrefix + "/sites")]
[Produces("application/json")]
[AllowAnonymous]
[InternalApi]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalSitesController(
    InternalSitesService sites,
    ILogger<InternalSitesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        InternalSitesDto result;
        try
        {
            result = await sites.GetAsync(ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogError(ex, "API ภายใน: อ่านรายการ site ไม่สำเร็จ");
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                Envelope.From(Result<InternalSitesDto>.Fail(InternalApi.SourceUnavailable()), HttpContext.TraceIdentifier));
        }

        return Ok(Envelope.From(Result<InternalSitesDto>.Ok(result), HttpContext.TraceIdentifier));
    }
}
