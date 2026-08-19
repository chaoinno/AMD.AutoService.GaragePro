using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// ใบเสนอราคา — docs/03-api-contract.md §5
/// </summary>
[ApiController]
[Route("api/v1/quotations")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class QuotationsController(IQuotationService service) : ControllerBase
{
    /// <summary>คิวใบเสนอราคาของสาขา · filter: todo | wait | rev | done</summary>
    [HttpGet]
    public async Task<IActionResult> GetQueue([FromQuery] string? filter, CancellationToken ct) =>
        Render(await service.GetQueueAsync(filter, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Render(await service.GetAsync(id, ct));

    /// <summary>สร้างใบเสนอราคาฉบับร่างจากงานใน Garage DB เดิม</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateQuotationRequest request, CancellationToken ct) =>
        Render(await service.CreateAsync(request, ct));

    [HttpPost("{id:guid}/lines")]
    public async Task<IActionResult> AddLine(
        Guid id, [FromBody] UpsertLineRequest request, CancellationToken ct) =>
        Render(await service.AddLineAsync(id, request, ct));

    [HttpPut("{id:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> UpdateLine(
        Guid id, Guid lineId, [FromBody] UpsertLineRequest request, CancellationToken ct) =>
        Render(await service.UpdateLineAsync(id, lineId, request, ct));

    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> RemoveLine(Guid id, Guid lineId, CancellationToken ct) =>
        Render(await service.RemoveLineAsync(id, lineId, ct));

    /// <summary>ตรวจก่อนส่ง — คืนทั้งข้อที่ต้องแก้และคำเตือน</summary>
    [HttpGet("{id:guid}/validate")]
    public async Task<IActionResult> Validate(Guid id, CancellationToken ct) =>
        Render(await service.ValidateAsync(id, ct));

    /// <summary>ส่งให้ลูกค้าอนุมัติ — งานเปลี่ยนเป็น "รออนุมัติ"</summary>
    [HttpPost("{id:guid}/send")]
    public async Task<IActionResult> Send(Guid id, CancellationToken ct) =>
        Render(await service.SendAsync(id, ct));

    /// <summary>ออกฉบับแก้ไข — เวอร์ชันเดิมถูกแทนที่และการอนุมัติเดิมเป็นโมฆะ</summary>
    [HttpPost("{id:guid}/revise")]
    public async Task<IActionResult> Revise(
        Guid id, [FromBody] ReviseQuotationRequest request, CancellationToken ct) =>
        Render(await service.ReviseAsync(id, request, ct));

    /// <summary>ลูกค้าตัดสินใจรายบรรทัด (เรียกจากแอปมือถือบนเครื่องพนักงาน)</summary>
    [HttpPut("{id:guid}/lines/{lineId:guid}/decision")]
    public async Task<IActionResult> DecideLine(
        Guid id, Guid lineId, [FromBody] LineDecisionRequest request, CancellationToken ct) =>
        Render(await service.DecideLineAsync(id, lineId, request, ct));

    /// <summary>ลูกค้าเซ็นยืนยัน — ลายเซ็นผูกกับเวอร์ชันนี้เท่านั้น</summary>
    [HttpPost("{id:guid}/sign")]
    public async Task<IActionResult> Sign(
        Guid id, [FromBody] SignQuotationRequest request, CancellationToken ct) =>
        Render(await service.SignAsync(id, request, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "QUOTE_NOT_FOUND" or "JOB_NOT_FOUND" or "QUOTE_LINE_NOT_FOUND" or "CATALOG_ITEM_NOT_FOUND"
                => StatusCodes.Status404NotFound,
            "QUOTE_OTHER_SCOPE" or "JOB_OTHER_BRANCH"
                => StatusCodes.Status403Forbidden,
            "QUOTE_LOCKED_BY_OTHER" or "QUOTE_ALREADY_EXISTS" or "QUOTE_ALREADY_SUPERSEDED"
                => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}

/// <summary>
/// [UI] ทุก error state ต้องมี สาเหตุ + ปุ่มถัดไป + รหัสอ้างอิง — traceId คือรหัสอ้างอิงนั้น
/// </summary>
public sealed record Envelope<T>(bool Success, T? Data, ApiError? Error, string TraceId);

public static class Envelope
{
    public static Envelope<T> From<T>(Result<T> result, string traceId) =>
        new(result.Success, result.Data, result.Error, traceId);
}
