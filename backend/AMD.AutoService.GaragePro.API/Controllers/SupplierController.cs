using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Suppliers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>จัดการซัพพลายเออร์และความสัมพันธ์ระหว่างซัพพลายเออร์กับสินค้าในสาขาปัจจุบัน</summary>
[ApiController]
[Route("api/v1/suppliers")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class SupplierController(ISupplierService service) : ControllerBase
{
    /// <summary>ค้นหารายการซัพพลายเออร์ รองรับคำค้น สถานะ และ pagination</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] bool includeInactive,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        Render(await service.SearchAsync(q, includeInactive, page, pageSize, ct));

    /// <summary>อ่านรายละเอียดซัพพลายเออร์ตามรหัส</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Render(await service.GetAsync(id, ct));

    /// <summary>เพิ่มซัพพลายเออร์ใหม่ โดยตรวจรหัสซ้ำก่อนบันทึก</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SupplierUpsertRequest request, CancellationToken ct) =>
        Render(await service.CreateAsync(request, ct), created: true);

    /// <summary>แก้ไขข้อมูลซัพพลายเออร์เดิม</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SupplierUpsertRequest request, CancellationToken ct) =>
        Render(await service.UpdateAsync(id, request, ct));

    /// <summary>เปิดหรือปิดใช้งานซัพพลายเออร์แบบไม่ลบข้อมูล</summary>
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, [FromBody] MasterDataStatusRequest request, CancellationToken ct) =>
        Render(await service.SetStatusAsync(id, request, ct));

    /// <summary>อ่านซัพพลายเออร์ทั้งหมดที่ผูกกับสินค้า พร้อมต้นทุนและเงื่อนไขสั่งซื้อ</summary>
    [HttpGet("/api/v1/catalog/manage/{catalogItemId:guid}/suppliers")]
    public async Task<IActionResult> GetItemSuppliers(Guid catalogItemId, CancellationToken ct) =>
        Render(await service.GetForCatalogItemAsync(catalogItemId, ct));

    /// <summary>เพิ่มหรือแก้ไขการผูกสินค้าเข้ากับซัพพลายเออร์หนึ่งราย</summary>
    [HttpPut("/api/v1/catalog/manage/{catalogItemId:guid}/suppliers/{supplierId:guid}")]
    public async Task<IActionResult> UpsertItemSupplier(Guid catalogItemId, Guid supplierId,
        [FromBody] CatalogItemSupplierUpsertRequest request, CancellationToken ct) =>
        Render(await service.UpsertForCatalogItemAsync(catalogItemId, supplierId, request, ct));

    /// <summary>ยกเลิกการผูกซัพพลายเออร์ออกจากสินค้า</summary>
    [HttpDelete("/api/v1/catalog/manage/{catalogItemId:guid}/suppliers/{supplierId:guid}")]
    public async Task<IActionResult> RemoveItemSupplier(Guid catalogItemId, Guid supplierId, CancellationToken ct) =>
        Render(await service.RemoveFromCatalogItemAsync(catalogItemId, supplierId, ct));

    private IActionResult Render<T>(Result<T> result, bool created = false)
    {
        if (result.Success)
            return created ? StatusCode(StatusCodes.Status201Created, Envelope.From(result, HttpContext.TraceIdentifier))
                : Ok(Envelope.From(result, HttpContext.TraceIdentifier));
        var status = result.Error!.Code switch
        {
            "SUPPLIER_NOT_FOUND" or "SUPPLIER_LINK_NOT_FOUND" or "CATALOG_NOT_FOUND" => StatusCodes.Status404NotFound,
            "MASTER_DATA_MANAGE_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "SUPPLIER_CODE_DUPLICATE" or "SUPPLIER_PREFERRED_DUPLICATE" or "SUPPLIER_LINK_DUPLICATE" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
