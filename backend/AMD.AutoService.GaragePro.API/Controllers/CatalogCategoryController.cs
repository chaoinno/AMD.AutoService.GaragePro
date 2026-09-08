using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.CatalogCategories;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>จัดการหมวดหมู่สินค้าแบบลำดับชั้น (tree) และกฎหมวดปลายทาง</summary>
[ApiController]
[Route("api/v1/catalog-categories")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class CatalogCategoryController(ICatalogCategoryService service) : ControllerBase
{
    /// <summary>อ่านหมวดหมู่สินค้าเป็นโครงสร้าง tree พร้อมค้นหาและกรองสถานะ</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] bool includeInactive, CancellationToken ct) =>
        Render(await service.SearchAsync(q, includeInactive, ct));
    /// <summary>อ่านรายละเอียดหมวดหมู่ตามรหัส</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Render(await service.GetAsync(id, ct));
    /// <summary>เพิ่มหมวดหมู่ใหม่ หรือเพิ่มหมวดย่อยใต้หมวดแม่</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CatalogCategoryUpsertRequest request, CancellationToken ct) =>
        Render(await service.CreateAsync(request, ct), created: true);
    /// <summary>แก้ไขชื่อ รหัส ลำดับ หรือหมวดแม่ของหมวดหมู่</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] CatalogCategoryUpsertRequest request, CancellationToken ct) =>
        Render(await service.UpdateAsync(id, request, ct));
    /// <summary>เปิดหรือปิดใช้งานหมวดหมู่ โดยห้ามปิดหมวดที่มีลูกใช้งานอยู่</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, [FromBody] MasterDataStatusRequest request, CancellationToken ct) =>
        Render(await service.SetStatusAsync(id, request, ct));
    /// <summary>ลบหมวดหมู่เมื่อไม่มีหมวดย่อยหรือสินค้าใช้งานอยู่</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => Render(await service.DeleteAsync(id, ct));

    private IActionResult Render<T>(Result<T> result, bool created = false)
    {
        if (result.Success)
            return created ? StatusCode(StatusCodes.Status201Created, Envelope.From(result, HttpContext.TraceIdentifier))
                : Ok(Envelope.From(result, HttpContext.TraceIdentifier));
        var status = result.Error!.Code switch
        {
            "CATEGORY_NOT_FOUND" or "CATEGORY_PARENT_NOT_FOUND" => StatusCodes.Status404NotFound,
            "MASTER_DATA_MANAGE_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "CATEGORY_CODE_DUPLICATE" or "CATEGORY_HAS_CHILDREN" or "CATEGORY_IN_USE" or "CATEGORY_CIRCULAR_REFERENCE" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
