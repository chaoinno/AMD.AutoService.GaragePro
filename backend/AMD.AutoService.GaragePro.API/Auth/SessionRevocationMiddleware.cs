using System.Security.Claims;
using AMD.AutoService.GaragePro.Application.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// ตัดสิทธิ์ token ที่เจ้าของถูกปิดใช้งาน/ย้ายสาขา/เปลี่ยนสิทธิ์ — คู่กับ token มือถือที่ไม่หมดอายุ
/// (คำขอผู้ใช้ 2026-10-05) ถ้าไม่มีตัวนี้ พนักงานที่ลาออกแล้วยังใช้แอปเดิมได้ตลอดไป
///
/// ตรวจทุก token ไม่ใช่เฉพาะมือถือ — เว็บได้ประโยชน์เดียวกันฟรี (เดิมพนักงานที่ถูกปิดใช้ยังใช้ต่อได้จนครบ 12 ชม.)
///
/// [RISK] ยิง Garage DB เดิมซึ่งมี lock convoy อยู่แล้ว — cache ผลต่อ token
/// <see cref="JwtOptions.SessionCheckSeconds"/> วินาที จึงเป็นอย่างมาก 1 query ต่อผู้ใช้ต่อช่วงนั้น
/// ไม่ใช่ต่อคำขอ
/// </summary>
public sealed class SessionRevocationMiddleware(
    RequestDelegate next,
    IMemoryCache cache,
    IOptions<JwtOptions> options,
    ILogger<SessionRevocationMiddleware> logger)
{
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(Math.Max(1, options.Value.SessionCheckSeconds));

    public async Task InvokeAsync(HttpContext context, IAuthService auth)
    {
        var principal = context.User;
        if (principal.Identity?.IsAuthenticated != true
            || principal.HasClaim(GarageClaims.PreSession, "true"))
        {
            await next(context);
            return;
        }

        // jti แยก token แต่ละใบ — ย้ายสาขา/เปลี่ยนสิทธิ์แล้ว login ใหม่จะได้ jti ใหม่ ไม่ติด cache ของใบเก่า
        var tokenId = principal.FindFirstValue("jti") ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var key = $"session-revocation:{tokenId}";

        string? reason;
        if (cache.TryGetValue(key, out string? cached))
        {
            reason = cached;
        }
        else
        {
            try
            {
                reason = await auth.GetSessionRevocationReasonAsync(context.RequestAborted);
                cache.Set(key, reason, _ttl);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // [BIZ] fail-open: Garage DB ล่มต้องไม่เตะทุกคนออกจากแอปพร้อมกัน (login ใหม่ก็ไม่ได้อยู่ดี
                // และงานที่กรอกค้างจะหาย) — ไม่ cache ความล้มเหลว คำขอถัดไปจะลองตรวจใหม่เอง
                logger.LogWarning(ex, "ตรวจสถานะพนักงานไม่สำเร็จ — ปล่อยคำขอผ่านไปก่อน");
                reason = null;
            }
        }

        if (reason is null)
        {
            await next(context);
            return;
        }

        // ใช้ code AUTH_REQUIRED เดิม — client ทุกรุ่น (รวมแอปที่ยังไม่อัปเดต) ล้างเซสชันเมื่อเจอ code นี้อยู่แล้ว
        // เหตุผลจริงอยู่ใน messageTh ให้ client แสดงตรงๆ ตามกฎ envelope
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            data = (object?)null,
            error = new { code = "AUTH_REQUIRED", messageTh = reason },
            traceId = context.TraceIdentifier
        });
    }
}
