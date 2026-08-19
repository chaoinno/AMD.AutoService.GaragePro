using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.API.Auth;
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
public sealed class CatalogController(ICatalogRepository catalog, ICurrentUser user) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, CancellationToken ct)
    {
        var items = await catalog.SearchAsync(user.ShardKey, user.BranchId, q, ct);
        var dto = items.Select(c => QuotationMapper.ToDto(c, user.CanSeeCost)).ToList();

        return Ok(Envelope.From(
            Result<IReadOnlyList<CatalogItemDto>>.Ok(dto), HttpContext.TraceIdentifier));
    }
}
