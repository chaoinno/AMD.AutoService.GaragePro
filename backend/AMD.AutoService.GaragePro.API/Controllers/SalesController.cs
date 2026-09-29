using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Sales;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

[ApiController, Route("api/v1/sales"), Produces("application/json"), Authorize, RequireShiftSession]
public sealed class SalesController(ISaleService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] SaleSearchQuery query, CancellationToken ct = default) => Render(await service.SearchAsync(query, ct));
    [HttpGet("count-drafts")]
    public async Task<IActionResult> CountDrafts(CancellationToken ct = default) => Render(await service.CountDraftsAsync(ct));
    [HttpPost]
    public async Task<IActionResult> Create(CreateSaleRequest request, CancellationToken ct = default) => Render(await service.CreateAsync(request, ct), true);
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct = default) => Render(await service.GetAsync(id, ct));
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateSaleRequest request, CancellationToken ct = default) => Render(await service.UpdateAsync(id, request, ct));
    [HttpPost("{id:guid}/lines")]
    public async Task<IActionResult> AddLine(Guid id, AddSaleLineRequest request, CancellationToken ct = default) => Render(await service.AddLineAsync(id, request, ct));
    [HttpPut("{id:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> UpdateLine(Guid id, Guid lineId, UpdateSaleLineRequest request, CancellationToken ct = default) => Render(await service.UpdateLineAsync(id, lineId, request, ct));
    [HttpDelete("{id:guid}/lines/{lineId:guid}")]
    public async Task<IActionResult> DeleteLine(Guid id, Guid lineId, CancellationToken ct = default) => Render(await service.DeleteLineAsync(id, lineId, ct));
    [HttpPost("{id:guid}/checkout")]
    public async Task<IActionResult> Checkout(Guid id, CheckoutSaleRequest request, CancellationToken ct = default) => Render(await service.CheckoutAsync(id, request, ct));
    [HttpPost("{id:guid}/void")]
    public async Task<IActionResult> Void(Guid id, VoidSaleRequest request, CancellationToken ct = default) => Render(await service.VoidAsync(id, request, ct));
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct = default) => Render(await service.CancelAsync(id, ct));

    private IActionResult Render<T>(Result<T> result, bool created = false)
    {
        if (result.Success) return created ? StatusCode(StatusCodes.Status201Created, Envelope.From(result, HttpContext.TraceIdentifier)) : Ok(Envelope.From(result, HttpContext.TraceIdentifier));
        var status = result.Error!.Code switch
        {
            "SALE_NOT_FOUND" or "SALE_LINE_NOT_FOUND" => 404,
            "SALE_FORBIDDEN" => 403,
            "SALE_CONFLICT" or "SALE_LOCKED" or "STOCK_INSUFFICIENT" => 409,
            _ => 422
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}