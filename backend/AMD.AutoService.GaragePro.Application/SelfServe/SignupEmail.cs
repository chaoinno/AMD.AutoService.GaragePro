namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// ตรวจรูปแบบ + normalize อีเมลของ signup (docs/12 §7 ชั้น 1)
/// · [RISK] กติกา normalize ต้องตรงกับ <c>EmailNormalizer</c> ของ B5 ตัวอักษรต่อตัวอักษร (login ด้วยอีเมลค้น sp_Account ด้วยค่านี้)
///   — ยืนยันกับ B5 แล้ว 2026-10-14: trim → lowercase (invariant) → ตัด '.' ท้ายโดเมน → gmail.com/googlemail.com: ลบ '.'
///   ทุกตัวใน local · ตัดตั้งแต่ '+' ตัวแรก · googlemail.com → gmail.com · อย่างอื่นไม่แตะ (ไม่ตัด +tag โดเมนอื่น · ไม่แปลง IDN)
///   integrator: เมื่อ merge แล้วให้ <see cref="Normalize"/> เรียกตัวของ B5 ตัวเดียว (ไฟล์นี้แยกไว้กันชนชื่อไฟล์ตอนทำขนาน)
/// · การตรวจรูปแบบเข้มกว่าที่ login ต้องใช้ (สมัครครั้งเดียว ป้องกันพิมพ์ผิด) แต่ไม่พยายามครอบ RFC 5322 ทั้งหมด
/// </summary>
public static class SignupEmail
{
    public const int MaxLength = 254;
    private const int MaxLocalLength = 64;
    private const int MaxLabelLength = 63;
    private const string LocalSpecials = "!#$%&'*+/=?^_`{|}~.-";

    /// <summary>normalize อย่างเดียว (ไม่ตรวจรูปแบบละเอียด) — null เมื่อไม่มี '@' หนึ่งตัวพอดี หรือส่วนใดว่าง</summary>
    public static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var value = email.Trim().ToLowerInvariant();
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@')) return null;

        var local = value[..at];
        var domain = value[(at + 1)..].TrimEnd('.');
        if (domain.Length == 0) return null;

        if (domain is "gmail.com" or "googlemail.com")
        {
            var plus = local.IndexOf('+');
            if (plus >= 0) local = local[..plus];
            local = local.Replace(".", "", StringComparison.Ordinal);
            domain = "gmail.com";
        }

        return local.Length == 0 ? null : $"{local}@{domain}";
    }

    /// <summary>
    /// ตรวจรูปแบบแล้ว normalize — false เมื่อรูปแบบไม่ใช่อีเมลที่ส่งถึงได้จริง
    /// <paramref name="display"/> = อีเมลตามที่ผู้ใช้พิมพ์ (trim แล้ว) ใช้ส่งอีเมลจริง · <paramref name="domain"/> = โดเมนตัวพิมพ์เล็กของค่าที่ normalize
    /// </summary>
    public static bool TryParse(string? input, out string normalized, out string display, out string domain)
    {
        normalized = display = domain = string.Empty;
        if (string.IsNullOrWhiteSpace(input)) return false;

        var trimmed = input.Trim();
        if (trimmed.Length > MaxLength || !IsValidSyntax(trimmed)) return false;

        var value = Normalize(trimmed);
        if (value is null) return false;

        normalized = value;
        display = trimmed;
        domain = value[(value.IndexOf('@') + 1)..];
        return true;
    }

    /// <summary>โดเมนของค่าที่ normalize แล้ว (ส่วนหลัง @) — ใช้ลง sp_SignupAttempt.EmailDomain</summary>
    public static string? DomainOf(string? normalized)
    {
        var at = normalized?.IndexOf('@') ?? -1;
        return at < 0 ? null : normalized![(at + 1)..];
    }

    private static bool IsValidSyntax(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 0 || at != email.LastIndexOf('@')) return false;

        var local = email[..at];
        var domain = email[(at + 1)..].TrimEnd('.');

        if (local.Length > MaxLocalLength) return false;
        if (local.StartsWith('.') || local.EndsWith('.') || local.Contains("..", StringComparison.Ordinal)) return false;
        if (local.Any(c => !(char.IsAsciiLetterOrDigit(c) || LocalSpecials.Contains(c)))) return false;

        var labels = domain.Split('.');
        if (labels.Length < 2) return false;
        foreach (var label in labels)
        {
            if (label.Length is 0 or > MaxLabelLength) return false;
            if (label.StartsWith('-') || label.EndsWith('-')) return false;
            // ตัวอักษรนอก ASCII (โดเมนภาษาไทย .ไทย) ยอมรับ — DNS checker แปลงเป็น punycode เอง
            if (label.Any(c => !(char.IsLetterOrDigit(c) || c == '-'))) return false;
        }

        // TLD ต้องเป็นตัวอักษรอย่างน้อย 2 ตัว (หรือ punycode xn--) — กัน "a@b.c" / "a@1.2.3.4"
        var tld = labels[^1];
        return tld.StartsWith("xn--", StringComparison.OrdinalIgnoreCase)
            || (tld.Length >= 2 && tld.All(char.IsLetter));
    }
}
