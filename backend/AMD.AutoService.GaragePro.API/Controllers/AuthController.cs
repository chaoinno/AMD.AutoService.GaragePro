using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// เข้าสู่ระบบด้วยบัญชีเดิมใน Garage DB (dbo.User + dbo.Staff)
///
/// ลำดับการใช้งาน:
///   Web:   POST /auth/login → token ที่ผูก Staff.BranchId และใช้งานได้ทันที
///   Mobile compatibility: ยังเลือกสาขา/กะและเปิด ShiftSession ต่อได้
/// </summary>
[ApiController]
[Route("api/v1/auth")]
[Produces("application/json")]
public sealed class AuthController(IAuthService service) : ControllerBase
{
    /// <summary>
    /// [SECURITY] จำกัด 3 ชั้น: ต่อ IP 10 ครั้ง/นาที (policy นี้) · ต่อ IP 50 ครั้ง/ชม. (global limiter ตาม path)
    /// · ต่อ username ผิด 10 ครั้ง/15 นาที (AuthService) — ทุกชั้นตอบ 429 AUTH_RATE_LIMITED พร้อม Retry-After
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimiting.RateLimitPolicies.AuthLogin)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct) =>
        Render(await service.LoginAsync(request, ct));

    /// <summary>
    /// เข้าสู่ระบบด้วย Google (เจ้าของอู่ที่สมัครเอง) — รับ ID token จาก Google Identity Services คืนรูปเดียวกับ /login
    /// · ไม่สร้างบัญชีใหม่: อีเมลที่ยังไม่สมัครได้ 404 AUTH_GOOGLE_NOT_REGISTERED (สมัครที่ /public/signup/google)
    /// · ไม่ได้ตั้ง self-serve หรือยังไม่มีตัวตรวจ token ของ Google → 503 AUTH_GOOGLE_DISABLED
    /// · [SECURITY] จำกัดอัตราด้วย policy เดียวกับ /login
    /// </summary>
    [HttpPost("google")]
    [AllowAnonymous]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting(RateLimiting.RateLimitPolicies.AuthLogin)]
    public async Task<IActionResult> Google([FromBody] GoogleLoginRequest request, CancellationToken ct)
    {
        // ลงทะเบียนเฉพาะเมื่อตั้ง self-serve — resolve แบบ optional ไม่ให้ controller ทั้งตัวพังบน deployment ที่ไม่มี sp1
        if (HttpContext.RequestServices.GetService<IGoogleLoginService>() is not { } google)
            return Render(Result<LoginResultDto>.Fail(SelfServeAuthErrors.GoogleDisabled()));

        return Render(await google.LoginAsync(request, ct));
    }

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

    /// <summary>สาขาที่ผู้ดูแลระบบสลับไปได้ — บัญชีผู้ดูแลระบบชื่อเดียวกันในสาขาอื่น</summary>
    /// <remarks>B5: แพ็กเกจที่ไม่มีฟีเจอร์หลายสาขา (ทดลองใช้) ได้ 403 PLAN_FEATURE_LOCKED · legacy ไม่มีผล</remarks>
    [HttpGet("switchable-branches")]
    [Authorize]
    [RequireFeature(PlanFeature.MultiBranch)]
    public async Task<IActionResult> GetSwitchableBranches(CancellationToken ct) =>
        Render(await service.GetSwitchableBranchesAsync(ct));

    /// <summary>สลับสาขา — คืน token ใหม่ในนามบัญชีของสาขาที่เลือก (รูปเดียวกับ login)</summary>
    [HttpPost("switch-branch")]
    [Authorize]
    [RequireFeature(PlanFeature.MultiBranch)]
    public async Task<IActionResult> SwitchBranch([FromBody] SwitchBranchRequest request, CancellationToken ct) =>
        Render(await service.SwitchBranchAsync(request, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        // TRIAL_EXPIRED (ล็อกแล้ว login ไม่ได้) 403 · PLAN_* · AUTH_RATE_LIMITED/AUTH_BRANCH_FORBIDDEN · AUTH_GOOGLE_*
        var status = PlanErrors.StatusCodeFor(result.Error!.Code)
            ?? SelfServeAuthErrors.StatusCodeFor(result.Error.Code)
            ?? SelfServeErrors.StatusCodeFor(result.Error.Code)
            ?? result.Error.Code switch
        {
            "AUTH_CHECK_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            "AUTH_REQUIRED" => StatusCodes.Status401Unauthorized,
            "AUTH_INVALID_CREDENTIALS" => StatusCodes.Status401Unauthorized,
            "AUTH_RATE_LIMITED" => StatusCodes.Status429TooManyRequests,
            "AUTH_NOT_STAFF" or "AUTH_BRANCH_FORBIDDEN" or "AUTH_SWITCH_FORBIDDEN" or "SHIFT_CLOSE_FORBIDDEN"
                => StatusCodes.Status403Forbidden,
            "AUTH_USER_NOT_FOUND" or "SHIFT_NOT_FOUND" or "SHIFT_SESSION_NOT_FOUND"
                => StatusCodes.Status404NotFound,
            "SHIFT_ALREADY_CLOSED" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        // ถูกบล็อกต่อ username — บอกเวลาเท่ากับ window เต็ม (ขอบบน: ตัวนับอาจหมดก่อนนั้น) client อ่าน header นี้เป็น retryAfterSeconds
        if (status == StatusCodes.Status429TooManyRequests)
            Response.Headers.RetryAfter = LoginAttemptPolicy.WindowSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
