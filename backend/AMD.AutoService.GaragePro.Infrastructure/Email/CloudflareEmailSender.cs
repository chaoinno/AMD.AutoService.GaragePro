using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Email;

/// <summary>ส่งอีเมลไม่สำเร็จ — ข้อความไม่มีที่อยู่ผู้รับหรือเนื้อหา (ผู้เรียก log exception ทั้งก้อน)</summary>
public sealed class EmailDeliveryException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// ส่งอีเมลผ่าน Cloudflare Email Service (REST API: <c>POST /accounts/{account_id}/email/sending/send</c>)
/// · โดเมนผู้ส่งต้อง onboard ไว้ใน Email Service ของบัญชีเดียวกับ token และต้องใช้แพ็กเกจ Workers Paid
///   (docs/16-cloudflare-email-setup.md)
/// · ตั้งค่าไม่ครบ = <see cref="IsConfigured"/> false (log error ครั้งเดียวตอนสร้าง) ระบบจะซ่อนการสมัครด้วยอีเมลแทนที่จะพังทั้งหน้า
/// · [SECURITY] ไม่ log เนื้อหา (มีรหัส OTP) ไม่ log ที่อยู่เต็ม (log แค่โดเมนผู้รับ) และไม่ log token
/// · HttpClient ตัวเดียวทั้งแอปแบบ LineContactNotifier (ไม่เพิ่ม package HttpClientFactory) · เทสต์ส่ง HttpClient ของตัวเองเข้ามาได้
/// </summary>
public sealed class CloudflareEmailSender : IEmailSender
{
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
    };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly SelfServeOptions.EmailOptions _options;
    private readonly ILogger<CloudflareEmailSender> _logger;
    private readonly string? _missingSettings;

    public CloudflareEmailSender(IOptions<SelfServeOptions> options, ILogger<CloudflareEmailSender> logger)
        : this(options, logger, SharedHttp)
    {
    }

    internal CloudflareEmailSender(IOptions<SelfServeOptions> options, ILogger<CloudflareEmailSender> logger, HttpClient http)
    {
        _options = options.Value.Email;
        _logger = logger;
        _http = http;

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_options.AccountId)) missing.Add("SelfServe:Email:AccountId");
        if (string.IsNullOrWhiteSpace(_options.ApiKey)) missing.Add("SelfServe:Email:ApiKey");
        if (string.IsNullOrWhiteSpace(_options.FromAddress)) missing.Add("SelfServe:Email:FromAddress");
        if (missing.Count > 0)
        {
            _missingSettings = string.Join(", ", missing);
            // บอกชื่อค่าที่ขาด ไม่บอกค่า — ระหว่างนี้ signup/config ตอบ emailEnabled=false (production) เหลือสมัครด้วย Google
            _logger.LogError("Cloudflare Email ตั้งค่าไม่ครบ ({Missing}) จะยังไม่ส่งอีเมล", _missingSettings);
        }
    }

    public bool IsConfigured => _missingSettings is null;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (_missingSettings is not null)
            throw new InvalidOperationException($"Cloudflare Email ตั้งค่าไม่ครบ ({_missingSettings})");

        var domain = RecipientDomain(message.To);
        var tag = message.Tag ?? "-";

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"{_options.BaseUrl.TrimEnd('/')}/accounts/{Uri.EscapeDataString(_options.AccountId.Trim())}/email/sending/send")
        {
            // StringContent ไม่ใช่ JsonContent — JsonContent ส่งแบบ chunked ไม่มี Content-Length (บทเรียนจาก LINE)
            Content = new StringContent(JsonSerializer.Serialize(BuildPayload(message), Json), Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey.Trim());

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "เชื่อมต่อ Cloudflare Email ไม่ได้ (tag {Tag}, โดเมน {Domain})", tag, domain);
            throw new EmailDeliveryException("เชื่อมต่อ Cloudflare Email Service ไม่ได้", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Cloudflare Email ไม่ตอบภายในเวลาที่กำหนด (tag {Tag}, โดเมน {Domain})", tag, domain);
            throw new EmailDeliveryException("Cloudflare Email Service ไม่ตอบภายในเวลาที่กำหนด", ex);
        }

        using (response)
        {
            var parsed = await ReadAsync(response, ct);
            if (!response.IsSuccessStatusCode || parsed?.Success != true)
            {
                var error = parsed?.Errors?.FirstOrDefault();
                _logger.LogWarning(
                    "Cloudflare Email ส่งไม่สำเร็จ: HTTP {Status} code {Code} {Error} (tag {Tag}, โดเมน {Domain})",
                    (int)response.StatusCode, error?.Code, error?.Message, tag, domain);
                throw new EmailDeliveryException(error is null
                    ? $"Cloudflare Email Service ตอบ HTTP {(int)response.StatusCode}"
                    : $"Cloudflare Email Service ตอบ HTTP {(int)response.StatusCode} ({error.Code} {error.Message})");
            }

            // ปลายทางตีกลับถาวร (เช่น กล่องจดหมายไม่มีอยู่จริง) — ผู้ใช้จะไม่ได้รับ ให้ผู้เรียกจัดการเหมือนส่งไม่สำเร็จ
            if (parsed.Result?.PermanentBounces is { Count: > 0 })
            {
                _logger.LogWarning("Cloudflare Email: ปลายทางตีกลับถาวร (tag {Tag}, โดเมน {Domain})", tag, domain);
                throw new EmailDeliveryException("ปลายทางปฏิเสธอีเมลถาวร (permanent bounce)");
            }

            _logger.LogInformation("ส่งอีเมล {Tag} ถึงโดเมน {Domain} แล้ว ({Outcome})", tag, domain,
                parsed.Result?.Queued is { Count: > 0 } ? "queued" : "delivered");
        }
    }

    /// <summary>ชื่อฟิลด์ตามเอกสาร REST (snake_case ของ reply_to · ผู้ส่งเป็น object <c>address</c>/<c>name</c>)</summary>
    internal Dictionary<string, object?> BuildPayload(EmailMessage message)
    {
        var payload = new Dictionary<string, object?>
        {
            ["from"] = new Dictionary<string, string>
            {
                ["address"] = _options.FromAddress.Trim(),
                ["name"] = string.IsNullOrWhiteSpace(_options.FromName) ? "ServicePro" : _options.FromName.Trim(),
            },
            ["to"] = message.To.Trim(),
            ["subject"] = message.Subject,
            ["text"] = message.TextBody,
        };
        if (!string.IsNullOrWhiteSpace(message.HtmlBody)) payload["html"] = message.HtmlBody;
        if (!string.IsNullOrWhiteSpace(_options.ReplyToAddress)) payload["reply_to"] = _options.ReplyToAddress.Trim();
        return payload;
    }

    private static async Task<CloudflareResponse?> ReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body) ? null : JsonSerializer.Deserialize<CloudflareResponse>(body, Json);
        }
        catch (JsonException)
        {
            return null;   // เช่น หน้า HTML จาก proxy — ตัดสินจาก HTTP status แทน
        }
    }

    private static string RecipientDomain(string? address)
    {
        var at = address?.LastIndexOf('@') ?? -1;
        return at >= 0 && at < address!.Length - 1 ? address[(at + 1)..].Trim().ToLowerInvariant() : "-";
    }

    private sealed record CloudflareResponse(bool Success, List<CloudflareError>? Errors, CloudflareResult? Result);

    private sealed record CloudflareError(int Code, string? Message);

    private sealed record CloudflareResult(
        List<string>? Delivered,
        [property: JsonPropertyName("permanent_bounces")] List<string>? PermanentBounces,
        List<string>? Queued);
}
