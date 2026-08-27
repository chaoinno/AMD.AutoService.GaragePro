using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// จ๊อบ — อ่านรายการและเปิดจ๊อบใน Garage DB เดิม
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class JobsController(
    ILegacyReader legacy,
    ILegacyJobWriter jobWriter,
    ICurrentUser user) : ControllerBase
{
    /// <summary>
    /// ค้นงานด้วยเลขงาน / ทะเบียน / ชื่อลูกค้า / เบอร์ — หน้าถัดไปใช้ keyset:
    /// ส่ง beforeCreatedDate/beforeJobId ของแถวสุดท้ายที่ได้รับแล้ว
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] int take = 50,
        [FromQuery] DateTime? beforeCreatedDate = null, [FromQuery] long? beforeJobId = null,
        [FromQuery] int? pjTypeId = null, [FromQuery] int? pjStatusId = null,
        CancellationToken ct = default)
    {
        var jobs = await legacy.SearchJobsAsync(
            user.ShardKey, user.BranchId, q, Math.Clamp(take, 1, 100),
            beforeCreatedDate, beforeJobId, pjTypeId, pjStatusId, ct);
        return Ok(Envelope.From(Result<IReadOnlyList<LegacyJobDto>>.Ok(jobs), HttpContext.TraceIdentifier));
    }

    /// <summary>สถานะ (PJStatus) ที่ใช้งานจริงในสาขา — ตัวเลือกกรองหน้าจ๊อบ</summary>
    [HttpGet("status-options")]
    public async Task<IActionResult> StatusOptions(CancellationToken ct)
    {
        var options = await legacy.GetJobStatusOptionsAsync(user.ShardKey, user.BranchId, ct);
        return Ok(Envelope.From(Result<IReadOnlyList<JobStatusOptionDto>>.Ok(options), HttpContext.TraceIdentifier));
    }

    [HttpGet("{jobId:long}")]
    public async Task<IActionResult> Get(long jobId, CancellationToken ct)
    {
        var job = await legacy.GetJobAsync(user.ShardKey, jobId, ct);

        if (job is null)
            return NotFound(Envelope.From(
                Result<LegacyJobDto>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}"),
                HttpContext.TraceIdentifier));

        return Ok(Envelope.From(Result<LegacyJobDto>.Ok(job), HttpContext.TraceIdentifier));
    }

    /// <summary>ตัวเลือกสำหรับฟอร์มเปิดจ๊อบ</summary>
    [HttpGet("form-options")]
    public async Task<IActionResult> FormOptions(CancellationToken ct)
    {
        var options = await jobWriter.GetFormOptionsAsync(user.ShardKey, ct);
        return Ok(Envelope.From(Result<JobFormOptionsDto>.Ok(options), HttpContext.TraceIdentifier));
    }

    /// <summary>เปิดจ๊อบใน Garage DB เดิมตาม flow ของ ProjectAdd.aspx</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateLegacyJobRequest request, CancellationToken ct)
    {
        var result = await jobWriter.CreateAsync(
            user.ShardKey, user.BranchId, user.UserId, request, ct);

        if (!result.Success)
        {
            var status = result.Error?.Code switch
            {
                "JOB_DUPLICATE_OPEN" => StatusCodes.Status409Conflict,
                "JOB_REFERENCE_INVALID" => StatusCodes.Status400BadRequest,
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
