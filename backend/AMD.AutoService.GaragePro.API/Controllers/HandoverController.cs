using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Handover;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// [ASSUME] ยืนยันส่งมอบรถชั่วคราวบนเว็บ (Cashier/Office/Manager ยืนยันแทนลูกค้า) — มือถือ /handover/:jobId
/// จริงยังไม่ได้ออกแบบ (docs/01-workflow.md §11 [GAP·สูง])
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class HandoverController(IHandoverService service) : ControllerBase
{
    /// <summary>resume ร่าง — สร้างเช็คลิสต์ของในรถแบบคงที่ให้อัตโนมัติถ้างานนี้ยังไม่มี</summary>
    [HttpGet("jobs/{jobId:guid}/handover")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct) =>
        Render(await service.GetOrCreateAsync(jobId, ct));

    /// <summary>ติ๊กคืน/ไม่คืนของแต่ละรายการ — ไม่คืนต้องระบุเหตุผลเสมอ</summary>
    [HttpPut("jobs/{jobId:guid}/handover/items/{itemId:guid}")]
    public async Task<IActionResult> SaveItem(
        Guid jobId, Guid itemId, [FromBody] SaveHandoverItemRequest request, CancellationToken ct) =>
        Render(await service.SaveItemAsync(jobId, itemId, request, ct));

    /// <summary>ยืนยันส่งมอบ — ต้องตัดสินใจครบทุกรายการ + มีลายเซ็นแล้ว</summary>
    [HttpPut("jobs/{jobId:guid}/handover/submit")]
    public async Task<IActionResult> Submit(
        Guid jobId, [FromBody] SubmitHandoverRequest request, CancellationToken ct) =>
        Render(await service.SubmitAsync(jobId, request, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "JOB_NOT_FOUND" or "HANDOVER_NOT_FOUND" or "HANDOVER_ITEM_UNKNOWN" => StatusCodes.Status404NotFound,
            "JOB_OTHER_BRANCH" or "HANDOVER_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "HANDOVER_LOCKED" => StatusCodes.Status409Conflict,
            "HANDOVER_NOTE_REQUIRED" or "HANDOVER_SIGNATURE_REQUIRED" or "HANDOVER_INCOMPLETE"
                => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
