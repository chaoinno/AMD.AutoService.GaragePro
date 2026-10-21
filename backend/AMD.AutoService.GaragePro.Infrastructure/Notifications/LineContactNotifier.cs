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

    public Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct) =>
        SendAsync(retryKey, [LineOutboundMessage.FromText(text)], ct);

    public async Task<bool> SendAsync(Guid retryKey, IReadOnlyList<LineOutboundMessage> messages, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl.TrimEnd('/')}/v2/bot/message/push")
        {
            // StringContent ไม่ใช่ JsonContent — JsonContent สตรีมแบบ chunked ไม่มี Content-Length
            // ซึ่ง endpoint ที่เข้มงวดจะตัดการเชื่อมต่อทิ้ง (เจอจริงตอนทดสอบกับ mock)
            Content = new StringContent(BuildPushBody(_options.ContactGroupId, messages), Encoding.UTF8, "application/json"),
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

    /// <summary>
    /// body ของ push: ข้อความ = <c>{type:"text",text}</c> · รูป = <c>{type:"image",originalContentUrl,previewImageUrl}</c>
    /// (url เดียวกันทั้งคู่ · สลิปถูกแปลงเป็น JPEG ≤ 1 MB แล้ว) · LINE รับ 1 ถึง 5 ข้อความ และรูปต้องเป็น https
    /// ผิดเงื่อนไข = ArgumentException ก่อนยิง (ดีกว่าโดน 400 แล้วข้อความทั้งชุดหาย)
    /// </summary>
    internal static string BuildPushBody(string to, IReadOnlyList<LineOutboundMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        if (messages.Count is < 1 or > 5)
            throw new ArgumentException("LINE push ต้องมี 1 ถึง 5 ข้อความ", nameof(messages));

        var items = messages.Select(object (m) =>
        {
            if (!m.IsImage) return new { type = "text", text = m.Text ?? string.Empty };
            if (!Uri.TryCreate(m.ImageUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("รูปใน LINE push ต้องเป็นลิงก์ https", nameof(messages));
            return new { type = "image", originalContentUrl = m.ImageUrl, previewImageUrl = m.ImageUrl };
        }).ToArray();

        return JsonSerializer.Serialize(new { to, messages = items });
    }
}
