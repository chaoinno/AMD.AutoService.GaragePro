namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>ชื่อ action ใน sp_WorkspaceAudit ของการแจ้งชำระ (กฎข้อ 9: ทุกการเปลี่ยนสถานะลง audit พร้อมผู้กระทำ)</summary>
public static class PackagePaymentAuditActions
{
    public const string Submit = "package-payment-submit";
    public const string Cancel = "package-payment-cancel";
    public const string Approve = "package-payment-approve";
    public const string Reject = "package-payment-reject";
}

/// <summary>
/// การอ้างรายการด้วย id ที่ทีมงานพิมพ์ (<c>spadmin payment --id</c>): id เต็ม (มี/ไม่มีขีด/วงเล็บปีกกา) หรือ
/// อย่างน้อย 8 ตัวแรก (shortId ในข้อความ LINE) · ฟังก์ชันล้วน
/// </summary>
public static class PackagePaymentReference
{
    public const int MinPrefixLength = 8;

    /// <summary>เลขฐานสิบหกตัวพิมพ์เล็กไม่มีขีด (8 ถึง 32 ตัว) · null = รูปแบบไม่ถูกต้อง/สั้นเกิน</summary>
    public static string? Normalize(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var hex = reference.Trim().Trim('{', '}').Replace("-", string.Empty).ToLowerInvariant();
        if (hex.Length is < MinPrefixLength or > 32) return null;
        return hex.All(Uri.IsHexDigit) ? hex : null;
    }

    /// <summary>prefix แบบไม่มีขีด → prefix ของรูปแบบ "D" (8-4-4-4-12) ให้เทียบกับข้อความของ Guid ได้ตรงตำแหน่งขีด</summary>
    public static string ToDisplayPrefix(string hexPrefix)
    {
        ArgumentNullException.ThrowIfNull(hexPrefix);
        var chars = new System.Text.StringBuilder(hexPrefix.Length + 4);
        for (var i = 0; i < hexPrefix.Length; i++)
        {
            if (i is 8 or 12 or 16 or 20) chars.Append('-');
            chars.Append(hexPrefix[i]);
        }

        return chars.ToString();
    }
}
