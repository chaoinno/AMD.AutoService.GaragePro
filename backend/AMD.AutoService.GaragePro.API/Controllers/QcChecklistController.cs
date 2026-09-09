using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Qc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// เช็คลิสต์ตรวจสอบคุณภาพ (QC) — รายการอิงบรรทัดที่อนุมัติในใบเสนอราคาของ job นี้ ไม่มี process ตีกลับ
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class QcChecklistController(IQcChecklistService service) : ControllerBase
{
    /// <summary>resume ร่าง — สร้างจากบรรทัดที่อนุมัติในใบเสนอราคาปัจจุบันให้อัตโนมัติถ้างานนี้ยังไม่มีเช็คลิสต์ QC</summary>
    [HttpGet("jobs/{jobId:guid}/qc-checklist")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct) =>
        Render(await service.GetOrCreateAsync(jobId, ct));

    /// <summary>ติ๊กผ่าน/ยกเลิกผ่านรายการเดียว — ไม่มีสถานะ "ไม่ผ่าน" ในระบบ</summary>
    [HttpPut("jobs/{jobId:guid}/qc-checklist/items/{itemId:guid}")]
    public async Task<IActionResult> SaveItem(
        Guid jobId, Guid itemId, [FromBody] SaveQcChecklistItemRequest request, CancellationToken ct) =>
        Render(await service.SaveItemAsync(jobId, itemId, request, ct));

    /// <summary>บันทึกผลทดลองขับ (ระยะทาง + หมายเหตุ) — เงื่อนไขคงที่อีกข้อของ QC ผ่าน นอกเหนือจากรายการซ่อมทุกบรรทัด</summary>
    [HttpPut("jobs/{jobId:guid}/qc-checklist/test-drive")]
    public async Task<IActionResult> SaveTestDrive(
        Guid jobId, [FromBody] SaveQcTestDriveRequest request, CancellationToken ct) =>
        Render(await service.SaveTestDriveAsync(jobId, request, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "JOB_NOT_FOUND" or "QC_NOT_FOUND" or "QC_ITEM_UNKNOWN" => StatusCodes.Status404NotFound,
            "JOB_OTHER_BRANCH" => StatusCodes.Status403Forbidden,
            "QC_LOCKED" => StatusCodes.Status409Conflict,
            "QC_RESULT_INVALID" or "QC_VALIDATION" or "QC_NO_APPROVED_LINES" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
