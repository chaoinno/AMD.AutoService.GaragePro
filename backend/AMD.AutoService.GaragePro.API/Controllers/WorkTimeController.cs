using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Work;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// จับเวลาการทำงานของช่าง (docs/09-technician-time-tracking.md §7)
///
/// [SECURITY] ต่างจาก JobsController ที่ยังมี [RISK] เรื่อง RBAC ค้างอยู่ — โมดูลนี้ตรวจบทบาทที่
/// WorkTimeService ทุกเมธอด ไม่ได้พึ่ง [RequireShiftSession] อย่างเดียว
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class WorkTimeController(IWorkTimeService service) : ControllerBase
{
    /// <summary>
    /// เริ่มจับเวลางานคันนี้ — ปิดคาบของคันเดิมให้อัตโนมัติในคำขอเดียวกัน และดันสถานะจ๊อบจาก
    /// "อนุมัติแล้ว" เป็น "กำลังซ่อม" ให้เอง · <b>ห้ามให้ client ยิง stop แล้ว start แยกกัน</b>
    /// เพราะถ้า start พังหลัง stop สำเร็จ ช่างจะเดินออกไปโดยไม่มีคาบเปิดและไม่รู้ตัว
    /// </summary>
    [HttpPost("jobs/{jobId:guid}/work/start")]
    public async Task<IActionResult> Start(
        Guid jobId, [FromBody] StartWorkRequest request, CancellationToken ct) =>
        Render(await service.StartAsync(jobId, request, ct));

    /// <summary>พักงาน — ปิดคาบ work แล้วเปิดคาบ pause ต่อทันที (เวลาพักไม่เข้าการคำนวณ)</summary>
    [HttpPost("jobs/{jobId:guid}/work/pause")]
    public async Task<IActionResult> Pause(
        Guid jobId, [FromBody] PauseWorkRequest request, CancellationToken ct) =>
        Render(await service.PauseAsync(jobId, request, ct));

    /// <summary>กลับมาทำต่อหลังพัก</summary>
    [HttpPost("jobs/{jobId:guid}/work/resume")]
    public async Task<IActionResult> Resume(
        Guid jobId, [FromBody] ResumeWorkRequest request, CancellationToken ct) =>
        Render(await service.ResumeAsync(jobId, request, ct));

    /// <summary>หยุดจับเวลา — ไม่เปลี่ยนสถานะจ๊อบ</summary>
    [HttpPost("jobs/{jobId:guid}/work/stop")]
    public async Task<IActionResult> Stop(
        Guid jobId, [FromBody] StopWorkRequest request, CancellationToken ct) =>
        Render(await service.StopAsync(jobId, request, ct));

    /// <summary>
    /// คาบที่กำลังเปิดอยู่ของผู้เรียก พร้อมเวลาปัจจุบันของ server — แอปต้องเรียกทุกครั้งที่เปิดขึ้นมา
    /// และตอนกลับจาก background ห้ามเชื่อ state ที่เก็บไว้ในเครื่อง
    /// </summary>
    [HttpGet("work/current")]
    public async Task<IActionResult> Current(CancellationToken ct) =>
        Render(await service.GetCurrentAsync(ct));

    /// <summary>คาบทั้งหมดของงานหนึ่ง — รวมคาบของช่างทุกคนที่ลงมือกับคันนี้</summary>
    [HttpGet("jobs/{jobId:guid}/work")]
    public async Task<IActionResult> ByJob(Guid jobId, CancellationToken ct) =>
        Render(await service.GetByJobAsync(jobId, ct));

    /// <summary>
    /// รายการคาบของทั้งสาขา — หน้าตรวจคุณภาพข้อมูลของหัวหน้าช่าง/ผู้จัดการ
    /// `onlyNeedsReview=true` คัดเฉพาะคาบที่ระบบตัดให้เองหรือยังเปิดค้างอยู่
    /// </summary>
    [HttpGet("work/intervals")]
    public async Task<IActionResult> Search(
        [FromQuery] DateTime? fromDate, [FromQuery] DateTime? toDate,
        [FromQuery] bool onlyNeedsReview, CancellationToken ct) =>
        Render(await service.SearchAsync(fromDate, toDate, onlyNeedsReview, ct));

    /// <summary>แก้เวลาย้อนหลัง — หัวหน้าช่าง/ผู้จัดการ ภายในกรอบเวลาที่ตั้งไว้</summary>
    [HttpPut("work/intervals/{id:guid}")]
    public async Task<IActionResult> Edit(
        Guid id, [FromBody] EditWorkIntervalRequest request, CancellationToken ct) =>
        Render(await service.EditAsync(id, request, ct));

    /// <summary>ยกเลิกคาบที่ผิดชัดเจน — ผู้จัดการเท่านั้น และเป็น soft delete (แถวยังอยู่)</summary>
    [HttpDelete("work/intervals/{id:guid}")]
    public async Task<IActionResult> Void(
        Guid id, [FromBody] VoidWorkIntervalRequest request, CancellationToken ct) =>
        Render(await service.VoidAsync(id, request, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "JOB_NOT_FOUND" or "WORK_NOT_FOUND" => StatusCodes.Status404NotFound,
            "WORK_FORBIDDEN" or "JOB_OTHER_BRANCH" => StatusCodes.Status403Forbidden,
            "WORK_ALREADY_OPEN" or "WORK_ALREADY_PAUSED" or "WORK_ALREADY_VOIDED"
                or "WORK_STILL_OPEN" or "WORK_EDIT_WINDOW_EXPIRED" => StatusCodes.Status409Conflict,
            "WORK_VALIDATION" or "WORK_JOB_NOT_REPAIRABLE" or "WORK_NOT_TRACKING"
                or "WORK_NOT_PAUSED" or "WORK_NO_STAFF_PROFILE" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
