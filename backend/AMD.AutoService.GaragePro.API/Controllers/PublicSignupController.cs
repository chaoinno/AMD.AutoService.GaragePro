using System.Globalization;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// สมัครทดลองใช้ ServicePro ด้วยตัวเอง (docs/12 §3–§4 · แผน §3 "Routes ใหม่") — ไม่ต้องล็อกอิน
/// · ตรรกะทั้งหมดอยู่ที่ <see cref="ISignupService"/> · ที่นี่แค่เติมข้อมูลจาก HTTP (IP/ประเทศ/UA) และแปลง error code → HTTP status
/// · ไม่ได้ตั้ง self-serve (<c>SelfServe:ConnectionString</c>) = ไม่มี ISignupService ใน DI → ทุก endpoint ตอบ 503 SIGNUP_DISABLED
///   ยกเว้น <c>config</c> ที่ตอบ <c>enabled:false</c> (เว็บใช้ตัดสินว่าจะแสดงหน้าสมัครหรือไม่)
/// · [SECURITY] rate limit ตาม path อยู่ใน global limiter แล้ว (RateLimitRules: email/start · google 3/ชม. · verify/resend 10/ชม.
///   · ทุก path ใต้ /signup 30/ชม. ต่อ IP) — ถ้าเปลี่ยน route ที่นี่ต้องแก้ RateLimitRules ด้วย ไม่งั้นเพดานหายเงียบๆ
/// · honeypot <c>website</c> ได้คำตอบ "สำเร็จ" หลอก (เหมือน PublicContactController)
/// </summary>
[ApiController]
[Route("api/v1/public/signup")]
[AllowAnonymous]
[Produces("application/json")]
public sealed class PublicSignupController : ControllerBase
{
    private static readonly SignupConfigDto Disabled = new(false, false, false, null);

    private ISignupService? Service => HttpContext.RequestServices.GetService<ISignupService>();

    /// <summary>เปิดรับสมัครหรือไม่ + ช่องทางที่ใช้ได้ (Google/อีเมล) + site key ของ Turnstile</summary>
    [HttpGet("config")]
    public IActionResult Config() =>
        Ok(Envelope.From(Result<SignupConfigDto>.Ok(Service?.GetConfig() ?? Disabled), HttpContext.TraceIdentifier));

    /// <summary>รายชื่อจังหวัด (id ตรงกับ Garage db2) — cache 24 ชม. · ช่องจังหวัดไม่บังคับ</summary>
    [HttpGet("provinces")]
    public Task<IActionResult> Provinces(CancellationToken ct) =>
        Run(service => service.GetProvincesAsync(ct));

    /// <summary>เริ่มสมัครด้วยอีเมล + รหัสผ่าน → ส่ง OTP 6 หลัก (ตอบเหมือนกันทุกกรณี — ไม่บอกว่าอีเมลนี้มีบัญชีแล้วหรือไม่)</summary>
    [HttpPost("email/start")]
    public Task<IActionResult> StartEmail([FromBody] EmailSignupStartRequest request, CancellationToken ct) =>
        Run(service => service.StartEmailAsync(request, Client(), ct));

    /// <summary>ขอรหัสใหม่ (≥60 วินาทีหลังครั้งก่อน · ≤3 ครั้ง/ชม.)</summary>
    [HttpPost("email/resend")]
    public Task<IActionResult> ResendEmail([FromBody] EmailSignupResendRequest request, CancellationToken ct) =>
        Run(service => service.ResendEmailAsync(request, Client(), ct));

    /// <summary>ยืนยันรหัส → signupToken (ใช้ครั้งเดียว 15 นาที) สำหรับขั้นตั้งค่าอู่</summary>
    [HttpPost("email/verify")]
    public Task<IActionResult> VerifyEmail([FromBody] EmailSignupVerifyRequest request, CancellationToken ct) =>
        Run(service => service.VerifyEmailAsync(request, Client(), ct));

    /// <summary>สมัครด้วย Google (ID token จาก Google Identity Services) → signupToken</summary>
    [HttpPost("google")]
    public Task<IActionResult> Google([FromBody] GoogleSignupRequest request, CancellationToken ct) =>
        Run(service => service.GoogleAsync(request, Client(), ct));

    /// <summary>
    /// ตั้งค่าอู่ + เปิดอู่ → ผลเหมือน login (accessToken ฯลฯ) + workspaceNo + รหัสช่างตัวอย่าง (ถ้าใส่ข้อมูลตัวอย่าง)
    /// · ส่ง requestId เดิมซ้ำได้อย่างปลอดภัย (ได้อู่เดิม ไม่เปิดอู่ใหม่)
    /// </summary>
    [HttpPost("workspace")]
    public Task<IActionResult> Workspace([FromBody] CreateWorkspaceRequest request, CancellationToken ct) =>
        Run(service => service.CreateWorkspaceAsync(request, Client(), ct));

    private async Task<IActionResult> Run<T>(Func<ISignupService, Task<Result<T>>> work)
    {
        var service = Service;
        if (service is null) return Render(Result<T>.Fail(SelfServeErrors.SignupDisabled()));
        return Render(await work(service));
    }

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        // web client (api/client.ts) อ่าน Retry-After เป็น retryAfterSeconds · details ซ้ำไว้ให้ client ที่อ่านแต่ body
        if (SignupErrors.RetryAfterSeconds(result.Error) is int retryAfter)
            Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        return StatusCode(SignupErrors.StatusCodeFor(result.Error?.Code), Envelope.From(result, HttpContext.TraceIdentifier));
    }

    /// <summary>
    /// IP จริงมาจาก UseForwardedHeaders (Nginx เติม X-Forwarded-For · Nginx ตั้ง real_ip จาก Cloudflare) ·
    /// CF-IPCountry รับเฉพาะรหัส 2 ตัวอักษร (ค่าที่ client ปลอมมาได้ถ้าเข้าตรงไม่ผ่าน Cloudflare — ใช้ดูสถิติเท่านั้น)
    /// </summary>
    private SignupClientInfo Client()
    {
        var address = HttpContext.Connection.RemoteIpAddress;
        if (address is { IsIPv4MappedToIPv6: true }) address = address.MapToIPv4();

        var country = Request.Headers["CF-IPCountry"].FirstOrDefault()?.Trim().ToUpperInvariant();
        if (country is not { Length: 2 } || !country.All(char.IsAsciiLetterOrDigit)) country = null;

        return new SignupClientInfo(address?.ToString(), country, Request.Headers.UserAgent.FirstOrDefault());
    }
}
