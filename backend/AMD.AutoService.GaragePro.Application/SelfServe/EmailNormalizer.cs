namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// ทำอีเมลให้เป็นค่าเดียวกันก่อนเทียบ/เก็บใน <c>sp_Account.NormalizedEmail</c> (unique ตลอดกาล — docs/12 §7)
/// · ใช้ทั้งตอนสมัคร (S1 เขียนค่า) และตอน login ด้วยอีเมล / Google (B5 อ่านค่า) — กติกาต้องตรงกันทุกตัวอักษร
///   ไม่งั้นคนที่สมัครแล้วจะ login ด้วยอีเมลไม่เจอบัญชี (ตกลงกับ S1 แล้ว 2026-10-14)
/// · กติกา: ตัดช่องว่างหัวท้าย → ตัวพิมพ์เล็กทั้งที่อยู่ (invariant) → ตัด '.' ท้ายโดเมน →
///   ถ้าโดเมนเป็น gmail.com/googlemail.com: ลบ '.' ทุกตัวใน local part · ตัดตั้งแต่ '+' ตัวแรก · googlemail.com → gmail.com
///   [BIZ] Gmail ส่งอีเมลของ a.b+x@gmail.com กับ ab@gmail.com เข้ากล่องเดียวกัน — ไม่ทำแบบนี้คนเดียวสมัครทดลองใช้ซ้ำได้ไม่จำกัด
/// · โดเมนอื่นไม่ตัด +tag (ผู้ให้บริการหลายรายถือว่าเป็นคนละกล่อง) · ไม่แปลง IDN
/// </summary>
public static class EmailNormalizer
{
    private const string Gmail = "gmail.com";
    private const string GoogleMail = "googlemail.com";

    /// <summary>
    /// ค่าที่ normalize แล้ว — null เมื่อไม่ใช่อีเมล (ไม่มี '@' หรือมีมากกว่าหนึ่งตัว · local/domain ว่าง · มีช่องว่างข้างใน)
    /// ไม่ได้ตรวจรูปแบบครบตาม RFC — แค่พอให้เทียบความเป็นบัญชีเดียวกันได้ (การตรวจรูปแบบตอนสมัครเป็นของ S1)
    /// </summary>
    public static string? Normalize(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        var value = email.Trim().ToLowerInvariant();
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@')) return null;
        if (value.Any(char.IsWhiteSpace)) return null;

        var local = value[..at];
        var domain = value[(at + 1)..].TrimEnd('.');
        if (domain.Length == 0) return null;

        if (domain is Gmail or GoogleMail)
        {
            var plus = local.IndexOf('+');
            if (plus >= 0) local = local[..plus];
            local = local.Replace(".", string.Empty, StringComparison.Ordinal);
            domain = Gmail;
        }

        return local.Length == 0 ? null : $"{local}@{domain}";
    }
}
