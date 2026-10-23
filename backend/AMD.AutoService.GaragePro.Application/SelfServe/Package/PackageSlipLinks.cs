using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>
/// ลิงก์สลิปแบบมีลายเซ็นที่ LINE ใช้ดึงรูป (docs/17 §5.3):
/// <c>GET /api/v1/public/package-slips/{id}?exp={unix}&amp;sig={base64url(HMAC-SHA256(key, "{id:N}.{exp}"))}</c>
/// · [SECURITY] เปิดดูได้โดยไม่ล็อกอินจนลิงก์หมดอายุ → กลุ่ม LINE ต้องเป็นทีมงานเท่านั้น · ผิดทุกกรณีตอบ 404
/// · ตรวจลายเซ็นด้วย <see cref="CryptographicOperations.FixedTimeEquals"/> ก่อนแตะฐาน/FTP (คำขอปลอมเสียแค่ HMAC หนึ่งครั้ง)
/// · ไม่ตั้งคีย์ (หรือสั้นกว่า 32 ตัว) = ไม่สร้างลิงก์ → LINE ได้ข้อความอย่างเดียว
/// </summary>
public static class PackageSlipLinks
{
    public const string RoutePrefix = "/api/v1/public/package-slips/";

    public static string Sign(string key, Guid paymentId, long expiresUnix)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Payload(paymentId, expiresUnix));
        return Base64UrlEncode(mac);
    }

    /// <summary>false ทุกกรณีที่ไม่ใช่ลิงก์ที่ระบบออกและยังไม่หมดอายุ (ไม่มีคีย์ · หมดอายุ · ลายเซ็นผิดรูป/ไม่ตรง)</summary>
    public static bool Verify(string? key, Guid paymentId, long expiresUnix, string? signature, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(signature)) return false;
        if (expiresUnix <= new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()) return false;
        if (!TryBase64UrlDecode(signature, out var provided)) return false;

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Payload(paymentId, expiresUnix));
        return CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    /// <summary>ลิงก์เต็ม · null = ไม่ได้ตั้งคีย์ หรือไม่ได้ตั้ง PublicApiBaseUrl</summary>
    public static string? Create(SelfServeOptions.PackageOptions options, Guid paymentId, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(options);
        var key = options.EffectiveSlipLinkKey;
        var baseUrl = options.PublicApiBaseUrl?.Trim().TrimEnd('/');
        if (key is null || string.IsNullOrEmpty(baseUrl)) return null;

        var exp = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc))
            .AddDays(Math.Max(1, options.SlipLinkDays)).ToUnixTimeSeconds();
        return string.Create(CultureInfo.InvariantCulture,
            $"{baseUrl}{RoutePrefix}{paymentId:N}?exp={exp}&sig={Sign(key, paymentId, exp)}");
    }

    private static byte[] Payload(Guid paymentId, long expiresUnix) =>
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{paymentId:N}.{expiresUnix}"));

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64UrlDecode(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length > 100) return false;
        var s = text.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: return false;
        }

        var buffer = new byte[s.Length];
        if (!Convert.TryFromBase64String(s, buffer, out var written)) return false;
        bytes = buffer[..written];
        return true;
    }
}
