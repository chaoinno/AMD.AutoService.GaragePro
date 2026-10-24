using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// LINE ทีมขายของ self-serve — <c>SelfServe:SalesLineGroupId</c> ตั้งไว้ = push เข้ากลุ่มนั้นด้วย channel token เดิม
/// (<c>LineMessaging:ChannelAccessToken</c>) · ว่าง = ใช้ IContactNotifier เดิม (กลุ่มฟอร์มติดต่อ <c>LineMessaging:ContactGroupId</c>)
/// · ใช้ <see cref="LineContactNotifier"/> ตัวเดิมทั้งก้อน (retry key · ไม่ log ข้อความ/token) เปลี่ยนแค่ groupId ปลายทาง
/// </summary>
public sealed class SelfServeSalesNotifier : ISelfServeSalesNotifier
{
    private readonly IContactNotifier _inner;

    public SelfServeSalesNotifier(
        IContactNotifier contact,
        IOptions<LineMessagingOptions> line,
        IOptions<SelfServeOptions> selfServe,
        ILogger<LineContactNotifier> logger)
    {
        var groupId = selfServe.Value.SalesLineGroupId?.Trim();
        _inner = string.IsNullOrEmpty(groupId)
            ? contact
            : new LineContactNotifier(Options.Create(new LineMessagingOptions
            {
                ChannelAccessToken = line.Value.ChannelAccessToken,
                ContactGroupId = groupId,
                BaseUrl = line.Value.BaseUrl
            }), logger);
    }

    public bool IsConfigured => _inner.IsConfigured;

    public Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct = default) =>
        _inner.SendAsync(retryKey, text, ct);

    /// <summary>ข้อความ + รูปใน push เดียว (แจ้งชำระพร้อมสลิป · docs/17 §5.3)</summary>
    public Task<bool> SendAsync(Guid retryKey, IReadOnlyList<LineOutboundMessage> messages, CancellationToken ct = default) =>
        _inner.SendAsync(retryKey, messages, ct);
}
