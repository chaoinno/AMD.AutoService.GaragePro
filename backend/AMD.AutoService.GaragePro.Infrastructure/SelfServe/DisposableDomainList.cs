using System.Reflection;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// รายชื่อโดเมนอีเมลชั่วคราวที่ฝังมากับ build (<c>Resources/disposable-domains.txt</c>) — ใช้เมื่อ sp_BlockedDomain ยังว่าง
/// (docs/12 §7 ชั้น 2) · S4 (BlocklistRefresher) นำเข้ารายชื่อสาธารณะที่ใหญ่กว่าลงตารางได้ภายหลัง
/// · ไฟล์: หนึ่งโดเมนต่อบรรทัด · บรรทัดว่าง/ขึ้นต้นด้วย # ถูกข้าม · ตัวพิมพ์เล็กใหญ่ไม่สำคัญ
/// · ตรวจทั้งโดเมนและโดเมนแม่ (<c>x.mailinator.com</c> ถูกบล็อกด้วย <c>mailinator.com</c>) แต่ไม่ถึงระดับ TLD
/// </summary>
public static class DisposableDomainList
{
    public const string ResourceName = "AMD.AutoService.GaragePro.Infrastructure.SelfServe.Resources.disposable-domains.txt";

    private static readonly Lazy<HashSet<string>> Set = new(Load);

    public static IReadOnlyCollection<string> Domains => Set.Value;

    public static bool Contains(string? domain) =>
        WithParents(domain).Any(Set.Value.Contains);

    /// <summary>โดเมนเองและโดเมนแม่ที่มีอย่างน้อยสองส่วน — <c>a.b.example.com</c> → a.b.example.com · b.example.com · example.com</summary>
    public static IReadOnlyList<string> WithParents(string? domain)
    {
        var value = domain?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(value)) return [];

        var labels = value.Split('.');
        var result = new List<string>(labels.Length);
        for (var i = 0; i <= labels.Length - 2; i++)
            result.Add(string.Join('.', labels[i..]));
        return result;
    }

    private static HashSet<string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"ไม่พบ embedded resource {ResourceName} ตรวจ EmbeddedResource ใน csproj");
        using var reader = new StreamReader(stream);

        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var value = line.Trim().ToLowerInvariant();
            if (value.Length == 0 || value.StartsWith('#')) continue;
            set.Add(value);
        }

        return set;
    }
}
