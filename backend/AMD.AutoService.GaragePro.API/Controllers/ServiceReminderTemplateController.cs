using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.ServiceReminders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// รายการนัดเข้ารับบริการตั้งต้นของสาขา (น้ำมันเครื่อง/กรอง/เบรก…) — อ่านเปิดทุก role (หน้าส่งมอบรถต้องใช้เลือก)
/// สร้าง/แก้ไข/เปิดปิดใช้งาน จำกัดเฉพาะผู้จัดการ (บังคับที่ service ผ่าน MasterDataSupport.EnsureManager)
/// </summary>
[ApiController]
[Route("api/v1/service-reminder-templates")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class ServiceReminderTemplateController(IServiceReminderTemplateService service) : ControllerBase
{
    /// <summary>สาขาที่ยังไม่เคยมีรายการจะได้ชุดตั้งต้นให้เองตอนเรียกครั้งแรก</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] bool includeInactive, CancellationToken ct) =>
        Render(await service.SearchAsync(includeInactive, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ServiceReminderTemplateUpsertRequest request, CancellationToken ct) =>
        Render(await service.CreateAsync(request, ct), created: true);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] ServiceReminderTemplateUpsertRequest request, CancellationToken ct) =>
        Render(await service.UpdateAsync(id, request, ct));

    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, [FromBody] MasterDataStatusRequest request, CancellationToken ct) =>
        Render(await service.SetStatusAsync(id, request, ct));

    private IActionResult Render<T>(Result<T> result, bool created = false)
    {
        if (result.Success)
            return created ? StatusCode(StatusCodes.Status201Created, Envelope.From(result, HttpContext.TraceIdentifier))
                : Ok(Envelope.From(result, HttpContext.TraceIdentifier));
        var status = result.Error!.Code switch
        {
            "SERVICE_TEMPLATE_NOT_FOUND" => StatusCodes.Status404NotFound,
            "MASTER_DATA_MANAGE_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "SERVICE_TEMPLATE_NAME_DUPLICATE" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
