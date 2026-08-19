using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.API.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// งานซ่อม — อ่านจาก Garage DB เดิมแบบ read-only (docs/05 §5)
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class JobsController(ILegacyReader legacy, ICurrentUser user) : ControllerBase
{
    /// <summary>ค้นงานด้วยเลขงาน / ทะเบียน / ชื่อลูกค้า / เบอร์</summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] int take = 25, CancellationToken ct = default)
    {
        var jobs = await legacy.SearchJobsAsync(user.ShardKey, user.BranchId, q, Math.Clamp(take, 1, 100), ct);
        return Ok(Envelope.From(Result<IReadOnlyList<LegacyJobDto>>.Ok(jobs), HttpContext.TraceIdentifier));
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

    /// <summary>ช่างในสาขา — ใช้เลือกผู้รับผิดชอบต่อบรรทัดค่าแรง</summary>
    [HttpGet("/api/v1/technicians")]
    public async Task<IActionResult> Technicians(CancellationToken ct)
    {
        var list = await legacy.GetTechniciansAsync(user.ShardKey, user.BranchId, ct);
        return Ok(Envelope.From(Result<IReadOnlyList<LegacyTechnicianDto>>.Ok(list), HttpContext.TraceIdentifier));
    }
}
