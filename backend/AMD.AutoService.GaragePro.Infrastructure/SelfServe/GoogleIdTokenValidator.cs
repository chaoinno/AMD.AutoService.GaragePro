using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ตรวจ Google ID token (Google Identity Services) ด้วย Microsoft.IdentityModel ชุดเดียวกับ JwtBearer — ไม่เพิ่ม Google.Apis.Auth
/// · กุญแจสาธารณะมาจาก discovery document ของ Google (<see cref="ConfigurationManager{T}"/> cache + refresh เองตาม Cache-Control)
/// · ตรวจ: ลายเซ็น RS256 · issuer (<c>accounts.google.com</c> ทั้งสองรูป) · audience = <c>SelfServe:Google:ClientId</c> · exp/nbf
/// · กุญแจหมุน (kid ใหม่ที่ cache ยังไม่มี) → ขอ refresh แล้วลองใหม่ครั้งเดียว
/// · null = ไม่ผ่านทุกกรณี (ไม่โยน exception) · [SECURITY] ไม่ log ตัว token
/// · ผู้เรียกต้องตรวจ <see cref="GoogleIdentity.EmailVerified"/> เอง (สัญญาของ C0)
/// </summary>
public sealed class GoogleIdTokenValidator : IGoogleIdTokenValidator
{
    public const string MetadataAddress = "https://accounts.google.com/.well-known/openid-configuration";
    public static readonly string[] Issuers = ["accounts.google.com", "https://accounts.google.com"];
    private const int MaxTokenLength = 8192;

    private readonly IConfigurationManager<OpenIdConnectConfiguration> _configuration;
    private readonly string _clientId;
    private readonly ILogger<GoogleIdTokenValidator> _logger;
    private readonly JsonWebTokenHandler _handler = new();

    public GoogleIdTokenValidator(IOptions<SelfServeOptions> options, ILogger<GoogleIdTokenValidator> logger)
        : this(options, logger, new ConfigurationManager<OpenIdConnectConfiguration>(
            MetadataAddress, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = true }))
    {
    }

    /// <summary>เทสต์ส่ง <see cref="StaticConfigurationManager{T}"/> ที่มีกุญแจที่สร้างเองได้ (ไม่ต่อเครือข่าย)</summary>
    internal GoogleIdTokenValidator(
        IOptions<SelfServeOptions> options, ILogger<GoogleIdTokenValidator> logger,
        IConfigurationManager<OpenIdConnectConfiguration> configuration)
    {
        _clientId = options.Value.Google.ClientId?.Trim() ?? "";
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default)
    {
        if (_clientId.Length == 0 || string.IsNullOrWhiteSpace(idToken) || idToken.Length > MaxTokenLength) return null;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            OpenIdConnectConfiguration config;
            try
            {
                config = await _configuration.GetConfigurationAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "โหลดกุญแจของ Google (openid-configuration) ไม่ได้");
                return null;
            }

            var result = await _handler.ValidateTokenAsync(idToken, new TokenValidationParameters
            {
                ValidIssuers = Issuers,
                ValidAudience = _clientId,
                IssuerSigningKeys = config.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.FromMinutes(5),
            });

            if (result.IsValid) return ToIdentity(result);

            if (attempt == 0 && result.Exception is SecurityTokenSignatureKeyNotFoundException)
            {
                _configuration.RequestRefresh();
                continue;
            }

            _logger.LogInformation("Google ID token ไม่ผ่าน: {Reason}", result.Exception?.GetType().Name ?? "invalid");
            return null;
        }

        return null;
    }

    private static GoogleIdentity? ToIdentity(TokenValidationResult result)
    {
        var claims = result.Claims;
        var subject = Text(claims, "sub");
        var email = Text(claims, "email");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email)) return null;

        return new GoogleIdentity(
            subject,
            email,
            IsTrue(claims.TryGetValue("email_verified", out var verified) ? verified : null),
            Text(claims, "given_name"),
            Text(claims, "family_name"),
            Text(claims, "picture"));
    }

    private static string? Text(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var value) ? value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            null => null,
            _ => value.ToString()
        } : null;

    /// <summary>Google ส่ง email_verified เป็น boolean (บาง token รุ่นเก่าเป็นข้อความ "true")</summary>
    private static bool IsTrue(object? value) => value switch
    {
        bool b => b,
        string s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase),
        JsonElement { ValueKind: JsonValueKind.True } => true,
        JsonElement { ValueKind: JsonValueKind.String } e => string.Equals(e.GetString(), "true", StringComparison.OrdinalIgnoreCase),
        _ => false
    };
}
