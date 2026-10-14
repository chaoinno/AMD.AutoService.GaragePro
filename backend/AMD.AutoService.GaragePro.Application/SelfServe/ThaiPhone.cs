namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// เบอร์โทรไทย → E.164 (<c>+66…</c>) — เก็บรูปเดียวใน sp_Workspace.Phone เพื่อให้ตรวจ "เบอร์เคยสมัครแล้ว" เทียบกันได้
/// (docs/12 §7 ชั้น 5: ซ้ำไม่บล็อก แค่ติดธง)
/// · มือถือ <c>0[689]xxxxxxxx</c> (10 หลัก) · บ้าน/สำนักงาน <c>0[2-7]xxxxxxx</c> (9 หลัก)
/// · รับรูปที่คนพิมพ์จริง: ช่องว่าง/ขีด/วงเล็บ/จุด · <c>+66</c>/<c>66</c> นำหน้า (มี 0 ตามหลังหรือไม่ก็ได้)
/// </summary>
public static class ThaiPhone
{
    public static bool TryNormalize(string? input, out string e164)
    {
        e164 = string.Empty;
        if (string.IsNullOrWhiteSpace(input) || input.Length > 30) return false;

        var hasPlus = false;
        var digits = new System.Text.StringBuilder(input.Length);
        foreach (var c in input.Trim())
        {
            if (char.IsAsciiDigit(c)) digits.Append(c);
            else if (c == '+' && digits.Length == 0 && !hasPlus) hasPlus = true;
            else if (c is ' ' or '-' or '(' or ')' or '.') continue;
            else return false;
        }

        var national = digits.ToString();
        if (hasPlus || (national.StartsWith("66", StringComparison.Ordinal) && national.Length is 10 or 11 or 12))
        {
            if (!national.StartsWith("66", StringComparison.Ordinal)) return false; // +1… +44… ไม่ใช่เบอร์ไทย
            national = national[2..];
            if (!national.StartsWith('0')) national = "0" + national;
        }

        if (!IsValidNational(national)) return false;

        e164 = "+66" + national[1..];
        return true;
    }

    /// <summary>E.164 → รูปที่คนไทยอ่าน (081-234-5678 · 02-123-4567 · 053-123-456) — ใช้ในข้อความ LINE/อีเมล</summary>
    public static string FormatLocal(string? e164)
    {
        if (string.IsNullOrWhiteSpace(e164) || !e164.StartsWith("+66", StringComparison.Ordinal)) return e164 ?? "";
        var n = "0" + e164[3..];
        return n.Length switch
        {
            10 => $"{n[..3]}-{n[3..6]}-{n[6..]}",
            9 when n[1] == '2' => $"{n[..2]}-{n[2..5]}-{n[5..]}",
            9 => $"{n[..3]}-{n[3..6]}-{n[6..]}",
            _ => n
        };
    }

    private static bool IsValidNational(string n) =>
        n.Length switch
        {
            10 => n[0] == '0' && n[1] is '6' or '8' or '9',
            9 => n[0] == '0' && n[1] is >= '2' and <= '7',
            _ => false
        };
}
