using System.Security.Claims;
using AMD.AutoService.GaragePro.Application.Abstractions;
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
    private readonly TimeSpan _ttl = TimeSpan.FromSeconds(Math.Clamp(options.Value.SessionCheckSeconds, 1, 60));

    public async Task InvokeAsync(HttpContext context, IAuthService auth, ISessionVersionStore versions, ICurrentUser currentUser)
    {
        var principal = context.User;
        // API ภายใน (docs/17 §8.6) ยืนยันตัวด้วย X-Internal-Key ไม่ใช่ JWT · token ที่ติดมาด้วยต้องไม่ทำให้ได้ 401 AUTH_REQUIRED
        // และไม่ยิง Garage DB เพิ่ม
        if (principal.Identity?.IsAuthenticated != true
            || Internal.InternalApi.IsInternalPath(context.Request.Path))
        {
            await next(context);
            return;
        }

        // [SECURITY] รุ่นเซสชัน (docs/19): ไม่ cache และอยู่นอก catch แบบ fail-open ของฐานเดิมด้านล่างโดยตั้งใจ
        // ทุก instance อ่านค่าถาวรตัวเดียวกัน รวม token ก่อนเลือกกะ — ตรวจไม่ได้ = 503 (client ไม่ล้างเซสชัน) ไม่ใช่ปล่อยผ่าน
        try
        {
            var version = await versions.ReadAsync(currentUser.ShardKey, currentUser.UserId, context.RequestAborted);
            if (version.Version != currentUser.SessionVersion)
            {
                await RejectAsync(context, 401, "AUTH_REQUIRED", "สิทธิ์การเข้าใช้งานถูกยกเลิก กรุณาเข้าสู่ระบบใหม่");
                return;
            }
            if (version.Changing)
            {
                await RejectAsync(context, 503, "AUTH_CHECK_UNAVAILABLE", "กำลังเปลี่ยนสถานะบัญชี กรุณาลองใหม่");
                return;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Session version verification unavailable");
            await RejectAsync(context, 503, "AUTH_CHECK_UNAVAILABLE", "ไม่สามารถตรวจสอบสิทธิ์ได้ชั่วคราว กรุณาลองใหม่");
            return;
        }
        if (principal.HasClaim(GarageClaims.PreSession, "true"))
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
            catch (SessionVerificationException ex)
            {
                logger.LogError(ex, "Could not persist observed session revocation");
                await RejectAsync(context, 503, "AUTH_CHECK_UNAVAILABLE", "ไม่สามารถตรวจสอบสิทธิ์ได้ชั่วคราว กรุณาลองใหม่");
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // [BIZ] fail-open: Garage DB ล่มต้องไม่เตะทุกคนออกจากแอปพร้อมกัน (login ใหม่ก็ไม่ได้อยู่ดี
                // และงานที่กรอกค้างจะหาย) — ไม่ cache ความล้มเหลว คำขอถัดไปจะลองตรวจใหม่เอง
                logger.LogWarning(ex, "ตรวจสถานะพนักงานไม่สำเร็จ ปล่อยคำขอผ่านไปก่อน");
                reason = null;
            }
        }

        if (reason is null)
        {
            await next(context);
            return;
        }

        await RejectAsync(context, 401, "AUTH_REQUIRED", reason);
    }

    private static Task RejectAsync(HttpContext context, int status, string code, string reason)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";
        return context.Response.WriteAsJsonAsync(new
        {
            success = false,
            data = (object?)null,
            error = new { code, messageTh = reason },
            traceId = context.TraceIdentifier
        });
    }
}
