using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// เข้าสู่ระบบ เลือกสาขาและกะ — ใช้บัญชีเดิมใน Garage DB (dbo.User)
///
/// ลำดับการใช้งาน:
///   1. POST /auth/login          → token ขั้นแรก + รายการสาขา
///   2. GET  /auth/branches/{id}/shifts
///   3. POST /auth/shift-sessions → token ที่ใช้เรียก API งานได้จริง
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService service) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        Render(await service.LoginAsync(request, ct));

    /// <summary>กะของสาขา — ใช้ token ขั้นแรกได้</summary>
    [HttpGet("branches/{branchId:int}/shifts")]
    [Authorize]
    public async Task<IActionResult> GetShifts(int branchId, CancellationToken ct) =>
        Render(await service.GetShiftsAsync(branchId, ct));

    /// <summary>เปิดกะ — คืน token ที่ผูกสาขา/กะแล้ว</summary>
    [HttpPost("shift-sessions")]
    [Authorize]
    public async Task<IActionResult> OpenShift([FromBody] OpenShiftRequest request, CancellationToken ct) =>
        Render(await service.OpenShiftAsync(request, ct));

    /// <summary>ปิดกะ — [BIZ] ช่างและหัวหน้าช่างไม่มีสิทธิ์</summary>
    [HttpPost("shift-sessions/{sessionId:guid}/close")]
    [Authorize]
    [RequireShiftSession]
    public async Task<IActionResult> CloseShift(Guid sessionId, CancellationToken ct) =>
        Render(await service.CloseShiftAsync(sessionId, ct));

    [HttpGet("me")]
    [Authorize]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        Render(await service.GetMeAsync(ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "AUTH_INVALID_CREDENTIALS" => StatusCodes.Status401Unauthorized,
            "AUTH_NOT_STAFF" or "AUTH_BRANCH_FORBIDDEN" or "SHIFT_CLOSE_FORBIDDEN"
                => StatusCodes.Status403Forbidden,
            "AUTH_USER_NOT_FOUND" or "SHIFT_NOT_FOUND" or "SHIFT_SESSION_NOT_FOUND"
                => StatusCodes.Status404NotFound,
            "SHIFT_ALREADY_CLOSED" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
