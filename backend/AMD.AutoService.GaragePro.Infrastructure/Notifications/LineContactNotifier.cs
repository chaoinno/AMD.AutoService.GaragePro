using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Contact;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Notifications;

public sealed class LineMessagingOptions
{
    public const string SectionName = "LineMessaging";

    /// <summary>[SECURITY] Channel access token (long-lived) — user-secrets / env file เท่านั้น ห้าม commit</summary>
    public string ChannelAccessToken { get; set; } = "";

    /// <summary>groupId ของกลุ่มทีมขาย (ขึ้นต้นด้วย C) — bot ต้องถูกเชิญเข้ากลุ่มก่อน</summary>
    public string ContactGroupId { get; set; } = "";

    public string BaseUrl { get; set; } = "https://api.line.me";
}

/// <summary>
/// ส่งข้อความเข้ากลุ่มด้วย LINE Messaging API push (POST /v2/bot/message/push)
/// ใช้ X-Line-Retry-Key = RequestId ของฟอร์ม — LINE ตอบ 409 ถ้าคีย์นี้เคยส่งสำเร็จแล้ว ซึ่งนับว่าสำเร็จ
/// </summary>
public sealed class LineContactNotifier(
    IOptions<LineMessagingOptions> options,
    ILogger<LineContactNotifier> logger) : IContactNotifier
{
    // HttpClient ตัวเดียวทั้งแอป + PooledConnectionLifetime กัน DNS ค้าง (ไม่ต้องเพิ่ม package HttpClientFactory)
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly LineMessagingOptions _options = options.Value;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.ChannelAccessToken) && !string.IsNullOrWhiteSpace(_options.ContactGroupId);

    public async Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/v2/bot/message/push")
        {
            // StringContent ไม่ใช่ JsonContent — JsonContent สตรีมแบบ chunked ไม่มี Content-Length
            // ซึ่ง endpoint ที่เข้มงวดจะตัดการเชื่อมต่อทิ้ง (เจอจริงตอนทดสอบกับ mock)
            Content = new StringContent(JsonSerializer.Serialize(new
            {
                to = _options.ContactGroupId,
                messages = new[] { new { type = "text", text } },
            }), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ChannelAccessToken);
        request.Headers.Add("X-Line-Retry-Key", retryKey.ToString());

        try
        {
            using var response = await Http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Conflict)
                return true;

            // [SECURITY] log แค่สถานะ + requestId ของ LINE — ไม่ log ข้อความ (มีเบอร์โทรลูกค้า) และไม่ log token
            response.Headers.TryGetValues("x-line-request-id", out var ids);
            logger.LogWarning("LINE push ไม่สำเร็จ: HTTP {Status} (x-line-request-id {LineRequestId}, retryKey {RetryKey})",
                (int)response.StatusCode, ids?.FirstOrDefault(), retryKey);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "เชื่อมต่อ LINE Messaging API ไม่ได้ (retryKey {RetryKey})", retryKey);
            return false;
        }
    }
}
