using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.AppUpdates;
using AMD.AutoService.GaragePro.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// นโยบายแจ้งอัปเดตแอปที่มือถือถาม (docs/19) — ไม่ต้องล็อกอิน เพราะหน้า login ก็ต้องเห็นการบังคับอัปเดต
/// และแอปไม่แนบ token มากับคำขอนี้
/// </summary>
[ApiController]
[Route("api/v1/app-updates")]
public sealed class AppUpdatesController(AppUpdateService service, IMemoryCache cache) : ControllerBase
{
    /// <summary>
    /// แอปทุกเครื่องถามทุกนาทีขณะเปิดอยู่ และ endpoint นี้ไม่ต้องล็อกอิน — cache สั้นๆ ให้จำนวนเครื่องไม่กลายเป็นจำนวน query
    /// ผู้ดูแลบันทึกแล้วล้าง cache ของ instance นั้นทันที (instance อื่นเห็นค่าใหม่ภายในเวลานี้)
    /// </summary>
    internal static readonly TimeSpan PublicCacheTtl = TimeSpan.FromSeconds(30);

    internal static string CacheKey(string platform) => $"app-update-policy:{platform}";

    [AllowAnonymous]
    [HttpGet("{platform}")]
    public async Task<IActionResult> Get(string platform, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!AppVersions.IsPlatform(platform)) return NotFound(Envelope.From(
            Result<object>.Fail("APP_UPDATE_PLATFORM", "รองรับเฉพาะ iOS และ Android"), HttpContext.TraceIdentifier));
        // อ่านไม่สำเร็จไม่ถูก cache (exception หลุดก่อนเก็บค่า) — แอปใช้นโยบายล่าสุดที่เคยอ่านได้แทน
        var policy = await cache.GetOrCreateAsync(CacheKey(platform), entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = PublicCacheTtl;
            return service.ReadAsync(platform, ct);
        });
        return Ok(Envelope.From(Result<AppUpdatePolicy>.Ok(policy!), HttpContext.TraceIdentifier));
    }
}

/// <summary>
/// หน้าจัดการ "เวอร์ชันแอป" — [BIZ] ผู้ดูแลระบบทุกบัญชีแก้นโยบายที่มีผลกับทุกอู่ได้ (การตัดสินใจของเจ้าของระบบ 2026-10-28)
/// สถานะผู้ดูแลอ่านจากฐานเดิมใหม่ทุกครั้งใน <see cref="AppUpdateService.CanManageAsync"/> ไม่เชื่อ claim อย่างเดียว
/// </summary>
[ApiController]
[Authorize]
[RequireShiftSession]
[Route("api/v1/admin/app-updates")]
public sealed class AdminAppUpdatesController(AppUpdateService service, IMemoryCache cache) : ControllerBase
{
    [HttpGet("{platform}")]
    public async Task<IActionResult> Get(string platform, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!await service.CanManageAsync(ct)) return Denied();
        if (!AppVersions.IsPlatform(platform)) return UnknownPlatform();
        return Ok(Envelope.From(Result<AppUpdatePolicy>.Ok(await service.ReadAsync(platform, ct)), HttpContext.TraceIdentifier));
    }

    [HttpPut("{platform}")]
    public async Task<IActionResult> Save(string platform, SaveAppUpdatePolicy input, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        var result = await service.SaveAsync(platform, input, ct);
        if (result.Success) cache.Remove(AppUpdatesController.CacheKey(platform));
        var status = result.Success ? 200 : result.Error!.Code switch {
            "APP_UPDATE_FORBIDDEN" => 403, "APP_UPDATE_CONFLICT" => 409, _ => 422 };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }

    [HttpGet("{platform}/history")]
    public async Task<IActionResult> History(string platform, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        if (!await service.CanManageAsync(ct)) return Denied();
        if (!AppVersions.IsPlatform(platform)) return UnknownPlatform();
        return Ok(Envelope.From(Result<IReadOnlyList<AppUpdateHistory>>.Ok(await service.HistoryAsync(platform, ct)), HttpContext.TraceIdentifier));
    }

    private IActionResult Denied() => StatusCode(403, Envelope.From(
        Result<object>.Fail("APP_UPDATE_FORBIDDEN", "เฉพาะผู้ดูแลระบบเท่านั้นที่จัดการเวอร์ชันแอปได้"), HttpContext.TraceIdentifier));
    private IActionResult UnknownPlatform() => NotFound(Envelope.From(
        Result<object>.Fail("APP_UPDATE_PLATFORM", "รองรับเฉพาะ iOS และ Android"), HttpContext.TraceIdentifier));
}
