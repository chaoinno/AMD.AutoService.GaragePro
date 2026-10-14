using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ตรวจ Cloudflare Turnstile ผ่าน siteverify (docs/12 §7 ชั้น 6)
/// · ไม่ได้ตั้ง <c>SelfServe:Turnstile:SecretKey</c>: นอก Production = ผ่าน (dev ไม่ต้องมีบัญชี Cloudflare) ·
///   Production = ไม่ผ่านเสมอ (fail-closed — ลืมตั้งค่าแล้วเปิดประตูให้บอทไม่ได้)
/// · Cloudflare ล่ม/timeout = ไม่ผ่าน (คนจริงกดลองใหม่ได้ · ปล่อยผ่านจะทำให้การล่มกลายเป็นช่องโหว่)
/// · [SECURITY] ไม่ log secret/token — log แค่ error-codes ของ Cloudflare
/// </summary>
public sealed class TurnstileVerifier : ITurnstileVerifier
{
    public const string SiteVerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";
    private const int MaxTokenLength = 2048; // Cloudflare: token ยาวไม่เกิน 2048 ตัว

    // HttpClient ตัวเดียวทั้งแอป + PooledConnectionLifetime กัน DNS ค้าง (แบบ LineContactNotifier — ไม่เพิ่ม HttpClientFactory)
    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = TimeSpan.FromSeconds(10),
    };

    private readonly HttpClient _http;
    private readonly string _secret;
    private readonly bool _isProduction;
    private readonly ILogger<TurnstileVerifier> _logger;

    public TurnstileVerifier(
        IOptions<SelfServeOptions> options, ILogger<TurnstileVerifier> logger, SignupHostEnvironment? host = null)
        : this(options, logger, host, SharedHttp)
    {
    }

    internal TurnstileVerifier(
        IOptions<SelfServeOptions> options, ILogger<TurnstileVerifier> logger, SignupHostEnvironment? host, HttpClient http)
    {
        _secret = options.Value.Turnstile.SecretKey?.Trim() ?? "";
        _isProduction = (host ?? SignupHostEnvironment.Production).IsProduction;
        _logger = logger;
        _http = http;
    }

    public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
    {
        if (_secret.Length == 0)
        {
            if (_isProduction)
            {
                _logger.LogError("ไม่ได้ตั้ง SelfServe:Turnstile:SecretKey บน Production ปฏิเสธคำขอสมัครทั้งหมด");
                return false;
            }

            return true;
        }

        if (string.IsNullOrWhiteSpace(token) || token.Length > MaxTokenLength) return false;

        var form = new Dictionary<string, string> { ["secret"] = _secret, ["response"] = token };
        if (!string.IsNullOrWhiteSpace(remoteIp)) form["remoteip"] = remoteIp;

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await _http.PostAsync(SiteVerifyUrl, content, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Turnstile siteverify ตอบ HTTP {Status}", (int)response.StatusCode);
                return false;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = json.RootElement;
            if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True) return true;

            var codes = root.TryGetProperty("error-codes", out var errors) && errors.ValueKind == JsonValueKind.Array
                ? string.Join(",", errors.EnumerateArray().Select(e => e.GetString()))
                : "-";
            _logger.LogInformation("Turnstile ไม่ผ่าน: {Codes}", codes);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                       || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            _logger.LogWarning(ex, "เชื่อมต่อ Turnstile siteverify ไม่ได้");
            return false;
        }
    }
}
