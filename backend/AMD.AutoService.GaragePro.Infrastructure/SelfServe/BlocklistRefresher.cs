using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ดาวน์โหลดรายชื่อโดเมนอีเมลชั่วคราว (ไฟล์ข้อความ หนึ่งโดเมนต่อบรรทัด เช่น disposable-email-domains) จาก
/// <c>SelfServe:Blocklist:Url</c> — เรียกจากงานรายวันเท่านั้น (ทุก Blocklist:RefreshDays วัน)
/// · [SECURITY] URL มาจาก config ของเราเอง ไม่ใช่จากผู้ใช้ · จำกัดขนาด/เวลา · ไม่ตามข้อมูลที่ไม่ใช่ข้อความ
/// · รายการที่สั้นผิดปกติ (&lt; <see cref="MinimumEntries"/>) ถือว่าดาวน์โหลดผิด (หน้า error/HTML) แล้วโยน — ไม่เขียนทับอะไร
/// </summary>
public sealed partial class BlocklistRefresher : IBlocklistSource
{
    public const int MinimumEntries = 10;
    public const long MaxBytes = 5 * 1024 * 1024;

    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3
    })
    {
        Timeout = TimeSpan.FromSeconds(30),
        MaxResponseContentBufferSize = MaxBytes
    };

    public async Task<IReadOnlyList<string>> FetchAsync(string url, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("SelfServe:Blocklist:Url ต้องเป็น URL แบบ https");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/plain"));
        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var text = await response.Content.ReadAsStringAsync(ct);
        var domains = Parse(text);
        if (domains.Count < MinimumEntries)
            throw new InvalidOperationException(
                $"รายชื่อโดเมนจาก blocklist มีแค่ {domains.Count} รายการ น่าจะดาวน์โหลดผิดไฟล์ จึงไม่นำเข้า");
        return domains;
    }

    /// <summary>หนึ่งโดเมนต่อบรรทัด · ข้ามบรรทัดว่าง/คอมเมนต์ (#, //) · ตัวพิมพ์เล็ก · ตัดซ้ำ · ทิ้งค่าที่ไม่ใช่รูปแบบโดเมน</summary>
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('@').ToLowerInvariant();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("//", StringComparison.Ordinal)) continue;
            if (line.Length > 255 || !DomainPattern().IsMatch(line)) continue;
            if (seen.Add(line)) result.Add(line);
        }

        return result;
    }

    [GeneratedRegex(@"^(?=.{1,255}$)([a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z][a-z0-9-]{0,62}$")]
    private static partial Regex DomainPattern();
}
