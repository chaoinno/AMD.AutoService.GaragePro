using AMD.AutoService.GaragePro.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AMD.AutoService.GaragePro.Infrastructure.Email;

/// <summary>
/// ผู้ส่งอีเมลแบบ "เขียนลง log" สำหรับ dev เท่านั้น — ยังไม่ได้เลือกผู้ให้บริการอีเมลจริง (แผน §1)
/// · <see cref="IsConfigured"/> = false เสมอ → signup/config ตอบ emailEnabled=false เว็บจะซ่อนการสมัครด้วยอีเมล
///   ทำให้ OTP ไม่มีทางถูกส่ง "ลง log" ใน production โดยที่ผู้ใช้จริงรออีเมลที่ไม่มีวันมา
/// · [SECURITY] log เนื้อความทั้งฉบับ (มี OTP/ลิงก์ตั้งค่า) — ยอมรับได้เฉพาะเครื่อง dev
///   production ต้องตั้ง <c>SelfServe:Email:Provider</c> เป็นผู้ให้บริการจริงและลงทะเบียน IEmailSender ตัวอื่นแทน
///   ห้ามใช้คลาสนี้เป็น fallback เงียบๆ เมื่อ provider จริงตั้งค่าผิด
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public bool IsConfigured => false;

    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        logger.LogInformation(
            "[email:log] ถึง {To} · เรื่อง {Subject} · tag {Tag}\n{TextBody}",
            message.To, message.Subject, message.Tag ?? "-", message.TextBody);

        return Task.CompletedTask;
    }
}
