using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Intake;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// Checklist สภาพรถขณะรับ (ขั้นที่ 3 ของ "รับรถ 6 ขั้น") — docs/01-workflow.md §3.1
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class IntakeChecklistController(IIntakeChecklistService service) : ControllerBase
{
    /// <summary>4 หมวด 20 รายการคงที่ — cache ฝั่ง client ได้</summary>
    [HttpGet("intake-checklist/template")]
    public IActionResult Template() =>
        Ok(Envelope.From(
            Result<IReadOnlyList<IntakeChecklistTemplateItemDto>>.Ok(service.GetTemplate()),
            HttpContext.TraceIdentifier));

    /// <summary>resume ร่าง — สร้างให้อัตโนมัติถ้างานนี้ยังไม่มี checklist</summary>
    [HttpGet("jobs/{jobId:guid}/intake-checklist")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct) =>
        Render(await service.GetOrCreateAsync(jobId, ct));

    /// <summary>บันทึกทีละรายการ (เหมาะกับ offline-friendly / autosave)</summary>
    [HttpPut("jobs/{jobId:guid}/intake-checklist/items/{itemCode}")]
    public async Task<IActionResult> SaveItem(
        Guid jobId, string itemCode, [FromBody] SaveIntakeChecklistItemRequest request, CancellationToken ct) =>
        Render(await service.SaveItemAsync(jobId, itemCode, request, ct));

    /// <summary>ส่ง checklist — validate ครบทุกรายการแล้วล็อก</summary>
    [HttpPost("jobs/{jobId:guid}/intake-checklist/submit")]
    public async Task<IActionResult> Submit(Guid jobId, CancellationToken ct) =>
        Render(await service.SubmitAsync(jobId, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "JOB_NOT_FOUND" => StatusCodes.Status404NotFound,
            "JOB_OTHER_BRANCH" => StatusCodes.Status403Forbidden,
            "INTAKE_LOCKED" => StatusCodes.Status409Conflict,
            "INTAKE_ITEM_UNKNOWN" or "INTAKE_RESULT_INVALID" or "INTAKE_NOTE_REQUIRED" or "INTAKE_INCOMPLETE"
                => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
