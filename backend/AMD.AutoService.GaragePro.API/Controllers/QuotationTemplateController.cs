using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.QuotationTemplates;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// เทมเพลตใบเสนอราคา — อ่านเปิดทุก role (ตัวเลือกเทมเพลตในหน้าจ๊อบต้องใช้)
/// สร้าง/แก้ไข/เปิดปิดใช้งาน จำกัดเฉพาะผู้จัดการ (บังคับที่ service ผ่าน MasterDataSupport.EnsureManager)
/// อ้างอิง: docs/08-quotation-template.md
/// </summary>
[ApiController]
[Route("api/v1/quotation-templates")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class QuotationTemplateController(IQuotationTemplateService service) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] bool includeInactive, CancellationToken ct) =>
        Render(await service.SearchAsync(q, includeInactive, ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Render(await service.GetAsync(id, ct));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] QuotationTemplateUpsertRequest request, CancellationToken ct) =>
        Render(await service.CreateAsync(request, ct), created: true);

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] QuotationTemplateUpsertRequest request, CancellationToken ct) =>
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
            "QUOTE_TEMPLATE_NOT_FOUND" => StatusCodes.Status404NotFound,
            "MASTER_DATA_MANAGE_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "QUOTE_TEMPLATE_CODE_DUPLICATE" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
