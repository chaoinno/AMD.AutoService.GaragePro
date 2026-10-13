namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// ตรวจ token ของ Cloudflare Turnstile (siteverify) — กันบอทยิงสมัคร (docs/12 §7 ชั้น 6)
/// · <paramref name="remoteIp"/> ส่งให้ Cloudflare เทียบกับ IP ที่แก้ challenge (null ได้)
/// · implementation ข้ามการตรวจ (คืน true) เมื่อไม่ได้ตั้ง <c>SelfServe:Turnstile:SecretKey</c> และไม่ใช่ Production เท่านั้น
/// </summary>
public interface ITurnstileVerifier
{
    Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default);
}
