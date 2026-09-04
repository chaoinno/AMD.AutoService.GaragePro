using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Warehouses;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>จัดการคลังหลักของสาขาปัจจุบัน โดยสาขาอ่านจาก JWT ของผู้ใช้</summary>
[ApiController]
[Route("api/v1/warehouses")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class WarehouseController(IWarehouseService service) : ControllerBase
{
    /// <summary>ค้นหารายการคลังของสาขาปัจจุบัน</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] bool includeInactive, CancellationToken ct) =>
        Render(await service.SearchAsync(q, includeInactive, ct));
    /// <summary>อ่านรายละเอียดคลังตามรหัส</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Render(await service.GetAsync(id, ct));
    /// <summary>เพิ่มคลังใหม่และผูกสาขาจาก JWT อัตโนมัติ</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WarehouseUpsertRequest request, CancellationToken ct) =>
        Render(await service.CreateAsync(request, ct), created: true);
    /// <summary>แก้ไขข้อมูลคลังเดิมของสาขาปัจจุบัน</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] WarehouseUpsertRequest request, CancellationToken ct) =>
        Render(await service.UpdateAsync(id, request, ct));
    /// <summary>เปิดหรือปิดใช้งานคลังแบบไม่ลบข้อมูล</summary>
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
            "WAREHOUSE_NOT_FOUND" or "WAREHOUSE_BRANCH_NOT_FOUND" => StatusCodes.Status404NotFound,
            "MASTER_DATA_MANAGE_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "WAREHOUSE_CODE_DUPLICATE" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
