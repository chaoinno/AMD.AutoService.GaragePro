namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// อีเมลหนึ่งฉบับ — <paramref name="TextBody"/> บังคับ (client อีเมลที่ไม่แสดง HTML ต้องอ่านได้) · <paramref name="Tag"/> ใช้จัดกลุ่ม/ติดตาม
/// ที่ผู้ให้บริการ (เช่น <c>otp</c>, <c>welcome</c>, <c>reminder-7</c>) ไม่ใช่เนื้อหา
/// </summary>
public sealed record EmailMessage(
    string To,
    string Subject,
    string TextBody,
    string? HtmlBody = null,
    string? Tag = null);

/// <summary>
/// ส่งอีเมล (OTP · ต้อนรับ · เตือนหมดอายุ) — ระบบเดิมไม่เคยส่งอีเมลเลย (docs/12 §1.1)
/// · <see cref="IsConfigured"/> = false เมื่อ provider เป็นแบบ log (dev) → <c>GET /public/signup/config.emailEnabled=false</c>
///   เว็บซ่อนทางสมัครด้วยอีเมล เหลือ Google เท่านั้น ไม่ใช่ส่ง OTP ไปที่ log ของ production
/// · [SECURITY] implementation ห้าม log เนื้อหาเมื่อ IsConfigured = true
/// </summary>
public interface IEmailSender
{
    bool IsConfigured { get; }
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
