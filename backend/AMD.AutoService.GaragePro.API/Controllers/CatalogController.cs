using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// แคตตาล็อกอะไหล่และค่าแรงของงาน service
/// [BIZ] ต้นทุนถูกตัดออกที่ mapper ตาม role ไม่ใช่ให้ client ซ่อน
/// </summary>
[ApiController]
[Route("api/v1/catalog")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class CatalogController(ICatalogRepository catalog, ICatalogService service, ICurrentUser user) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken ct)
    {
        var items = await catalog.SearchAsync(user.ShardKey, user.BranchId, q, ct);
        var dto = items.Select(c => QuotationMapper.ToDto(c, user.CanSeeCost)).ToList();

        return Ok(Envelope.From(
            Result<IReadOnlyList<CatalogItemDto>>.Ok(dto), HttpContext.TraceIdentifier));
    }

    [HttpGet("manage")]
    public async Task<IActionResult> SearchManagement([FromQuery] CatalogSearchModel model, CancellationToken ct) =>
        Render(await service.SearchAsync(model.ToQuery(), ct));

    [HttpGet("manage/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) =>
        Render(await service.GetAsync(id, ct));

    [HttpPost("manage")]
    public async Task<IActionResult> Create([FromBody] CatalogUpsertRequest request, CancellationToken ct) =>
        Render(await service.CreateAsync(request, ct), created: true);

    [HttpPut("manage/{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CatalogUpsertRequest request, CancellationToken ct) =>
        Render(await service.UpdateAsync(id, request, ct));

    [HttpPatch("manage/{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, [FromBody] CatalogStatusRequest request, CancellationToken ct) =>
        Render(await service.SetStatusAsync(id, request, ct));

    private IActionResult Render<T>(Result<T> result, bool created = false)
    {
        if (result.Success)
            return created
                ? StatusCode(StatusCodes.Status201Created, Envelope.From(result, HttpContext.TraceIdentifier))
                : Ok(Envelope.From(result, HttpContext.TraceIdentifier));
        var status = result.Error!.Code switch
        {
            "CATALOG_NOT_FOUND" => StatusCodes.Status404NotFound,
            "CATALOG_MANAGE_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "CATALOG_CODE_DUPLICATE" or "CATEGORY_NOT_LEAF" => StatusCodes.Status409Conflict,
            "CATEGORY_NOT_FOUND" or "WAREHOUSE_NOT_FOUND" => StatusCodes.Status404NotFound,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}

public sealed class CatalogSearchModel
{
    public string? Keyword { get; set; }
    public LineType? Type { get; set; }
    public bool IncludeInactive { get; set; }
    public bool LowStockOnly { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;

    public CatalogManagementQuery ToQuery() =>
        new(Keyword, Type, IncludeInactive, LowStockOnly, Page, PageSize);
}
