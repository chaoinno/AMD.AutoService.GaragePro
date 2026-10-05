using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Promotions;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

[ApiController, Route("api/v1/promotions"), Produces("application/json"), Authorize, RequireShiftSession]
public sealed class PromotionsController(IPromotionService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? keyword, [FromQuery] bool includeInactive = false, CancellationToken ct = default) => Render(await service.SearchAsync(keyword, includeInactive, ct));
    [HttpGet("applicable")]
    public async Task<IActionResult> Applicable([FromQuery] PromotionScope scope, CancellationToken ct = default) => Render(await service.ApplicableAsync(scope, ct));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct = default) => Render(await service.GetAsync(id, ct));
    [HttpPost]
    public async Task<IActionResult> Create(PromotionUpsertRequest request, CancellationToken ct = default) => Render(await service.CreateAsync(request, ct), true);
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, PromotionUpsertRequest request, CancellationToken ct = default) => Render(await service.UpdateAsync(id, request, ct));
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, PromotionStatusRequest request, CancellationToken ct = default) => Render(await service.SetStatusAsync(id, request, ct));

    private IActionResult Render<T>(Result<T> result, bool created = false)
    {
        if (result.Success) return created ? StatusCode(StatusCodes.Status201Created, Envelope.From(result, HttpContext.TraceIdentifier)) : Ok(Envelope.From(result, HttpContext.TraceIdentifier));
        var status = result.Error!.Code switch { "PROMOTION_NOT_FOUND" => 404, "MASTER_DATA_MANAGE_FORBIDDEN" => 403, "PROMOTION_CODE_DUPLICATE" => 409, _ => 422 };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}