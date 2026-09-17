using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Jobs;
using AMD.AutoService.GaragePro.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// จ๊อบ — svc_Job เป็นแหล่งข้อมูลเดียว (ไม่เขียน/อ่านสด PJCarPickUp อีกต่อไป ตั้งแต่ 2026-08-31)
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class JobsController(
    ILegacyReader legacy,
    IJobService jobService,
    ICurrentUser user) : ControllerBase
{
    /// <summary>
    /// ค้นงานด้วยเลขงาน / ทะเบียน / ชื่อลูกค้า / เบอร์ — หน้าถัดไปใช้ keyset:
    /// ส่ง beforeCreatedAt/beforeJobId ของแถวสุดท้ายที่ได้รับแล้ว
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] int take = 50,
        [FromQuery] DateTime? beforeCreatedAt = null, [FromQuery] Guid? beforeJobId = null,
        [FromQuery] int? jobTypeId = null, [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        var result = await jobService.SearchAsync(
            q, Math.Clamp(take, 1, 100), beforeCreatedAt, beforeJobId, jobTypeId, status, ct);

        if (!result.Success)
            return StatusCode(StatusCodes.Status400BadRequest, Envelope.From(result, HttpContext.TraceIdentifier));

        return Ok(Envelope.From(result, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// มุมมองปฏิทินนัดหมาย — คืนงานทุกงานที่มี AppointmentAt อยู่ในช่วง [from, to)
    /// ไม่ใช่ keyset cursor (คนละ contract กับ /search)
    /// </summary>
    [HttpGet("calendar")]
    public async Task<IActionResult> Calendar(
        [FromQuery] DateTimeOffset from, [FromQuery] DateTimeOffset to,
        [FromQuery] string? q = null, [FromQuery] string? status = null, CancellationToken ct = default)
    {
        var result = await jobService.GetCalendarAsync(from, to, q, status, ct);

        if (!result.Success)
        {
            var httpStatus = result.Error?.Code switch
            {
                "JOB_STATUS_UNKNOWN" or "JOB_CALENDAR_RANGE" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status400BadRequest
            };
            return StatusCode(httpStatus, Envelope.From(result, HttpContext.TraceIdentifier));
        }

        return Ok(Envelope.From(result, HttpContext.TraceIdentifier));
    }

    /// <summary>สถานะทั้งหมดที่ระบบรองรับ — ตัวเลือกกรองหน้าจ๊อบ</summary>
    [HttpGet("status-options")]
    public IActionResult StatusOptions() =>
        Ok(Envelope.From(Result<IReadOnlyList<JobStatusOptionDto>>.Ok(jobService.GetStatusOptions()),
            HttpContext.TraceIdentifier));

    /// <summary>จำนวนงานที่ยังไม่ปิดของสาขาปัจจุบัน — ใช้แสดงตัวเลขที่เมนูจ๊อบ</summary>
    [HttpGet("count-open")]
    public async Task<IActionResult> CountOpen([FromQuery] int? jobTypeId, CancellationToken ct)
    {
        var result = await jobService.CountOpenAsync(jobTypeId, ct);
        return Ok(Envelope.From(result, HttpContext.TraceIdentifier));
    }

    /// <summary>จำนวนจ๊อบที่ยังไม่ปิด แยกตามสถานะ + จำนวนที่เกินเวลานัดส่ง (สโคปตามสาขาใน JWT)</summary>
    [HttpGet("counts")]
    public async Task<IActionResult> Counts([FromQuery] int? jobTypeId, CancellationToken ct)
    {
        var result = await jobService.CountsAsync(jobTypeId, ct);
        return Ok(Envelope.From(result, HttpContext.TraceIdentifier));
    }

    [HttpGet("{jobId:guid}")]
    public async Task<IActionResult> Get(Guid jobId, CancellationToken ct)
    {
        var result = await jobService.GetAsync(jobId, ct);

        if (!result.Success)
            return NotFound(Envelope.From(result, HttpContext.TraceIdentifier));

        return Ok(Envelope.From(result, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// เปลี่ยนสถานะจ๊อบ (svc_Job) ตาม JobStateMachine — guard ที่ยังไม่มีระบบรองรับต้องระบุ reason
    /// </summary>
    [HttpPost("{jobId:guid}/transitions")]
    public async Task<IActionResult> Transition(
        Guid jobId, [FromBody] TransitionJobRequest request, CancellationToken ct)
    {
        var result = await jobService.TransitionAsync(jobId, request, ct);

        if (!result.Success)
        {
            var status = result.Error?.Code switch
            {
                "JOB_NOT_FOUND" => StatusCodes.Status404NotFound,
                "JOB_STATUS_UNKNOWN" or "JOB_TRANSITION_NEEDS_REASON" => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status409Conflict
            };
            return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
        }

        return Ok(Envelope.From(result, HttpContext.TraceIdentifier));
    }

    /// <summary>เลื่อน/แก้วันเวลานัดหมาย — เฉพาะงานประเภทรถนัดหมายและยังไม่ถึงสถานะจบ</summary>
    [HttpPut("{jobId:guid}/appointment")]
    public async Task<IActionResult> UpdateAppointment(
        Guid jobId, [FromBody] UpdateJobAppointmentRequest request, CancellationToken ct)
    {
        var result = await jobService.UpdateAppointmentAsync(jobId, request, ct);

        if (!result.Success)
        {
            var status = result.Error?.Code switch
            {
                "JOB_NOT_FOUND" => StatusCodes.Status404NotFound,
                "JOB_APPOINTMENT_LOCKED" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
        }

        return Ok(Envelope.From(result, HttpContext.TraceIdentifier));
    }

    /// <summary>แปลงงานนัดหมายเป็นรถในอู่พร้อมบันทึกวันเวลาที่รถเข้าอู่จริง — ไม่ผูกกับวันนัดหมายที่ตั้งไว้</summary>
    [HttpPut("{jobId:guid}/convert-to-in-shop")]
    public async Task<IActionResult> ConvertToInShop(
        Guid jobId, [FromBody] ConvertToInShopRequest request, CancellationToken ct)
    {
        var result = await jobService.ConvertToInShopAsync(jobId, request, ct);

        if (!result.Success)
        {
            var status = result.Error?.Code switch
            {
                "JOB_NOT_FOUND" => StatusCodes.Status404NotFound,
                "JOB_TYPE_CONVERSION_NOT_ALLOWED" => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
        }

        return Ok(Envelope.From(result, HttpContext.TraceIdentifier));
    }

    /// <summary>เปิดจ๊อบ — เก็บใน svc_Job เท่านั้น อ้างอิงลูกค้า/รถที่มีอยู่แล้วด้วย id (ไม่เขียนกลับ Garage DB)</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateJobRequest request, CancellationToken ct)
    {
        var result = await jobService.CreateAsync(request, ct);

        if (!result.Success)
        {
            var status = result.Error?.Code switch
            {
                "JOB_DUPLICATE_OPEN" => StatusCodes.Status409Conflict,
                "CUSTOMER_NOT_FOUND" or "VEHICLE_NOT_FOUND" => StatusCodes.Status404NotFound,
                _ => StatusCodes.Status400BadRequest
            };
            return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
        }

        return StatusCode(
            StatusCodes.Status201Created,
            Envelope.From(result, HttpContext.TraceIdentifier));
    }

    /// <summary>ช่างในสาขา — ใช้เลือกผู้รับผิดชอบต่อบรรทัดค่าแรง</summary>
    [HttpGet("/api/v1/technicians")]
    public async Task<IActionResult> Technicians(CancellationToken ct)
    {
        var list = await legacy.GetTechniciansAsync(user.ShardKey, user.BranchId, ct);
        return Ok(Envelope.From(Result<IReadOnlyList<LegacyTechnicianDto>>.Ok(list), HttpContext.TraceIdentifier));
    }
}
