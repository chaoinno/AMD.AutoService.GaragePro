using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// ตัวช่วยจัดรูปข้อความของ self-serve (อีเมล · LINE ทีมขาย · หน้า status) — ฟังก์ชันล้วน ไม่มี I/O
/// · [BIZ] วันที่ทุกตัวที่คนอ่านแสดงเป็น "เวลาไทย" แบบ พ.ศ. (dd/MM/yyyy) ตรงกับข้อความ LINE ฟอร์มติดต่อเดิม
///   (ContactRequestService) — คำนวณ พ.ศ. เอง ไม่พึ่ง culture ของเครื่อง (container invariant ให้ ค.ศ.)
/// · ประเทศไทยไม่มี DST → offset คงที่ +7 ผ่าน <see cref="BranchPackageDates"/> ที่เดียว
/// </summary>
public static class SelfServeText
{
    /// <summary>ช่องทางติดต่อฝ่ายขายที่ใช้ในทุกข้อความ (ชุดเดียวกับหน้าเว็บ/ฟอร์มติดต่อ)</summary>
    public const string ContactLine = PlanErrors.ContactLine;
    public const string ContactPhone = "090-996-6446";

    /// <summary>แอปมือถือบน Google Play (ค่าเดียวกับ web/src/features/landing/appStore.ts)</summary>
    public const string PlayStoreUrl = "https://play.google.com/store/apps/details?id=net.garagepro.gpservice";

    /// <summary>รหัสอู่สำหรับแสดงผล เช่น 1042 → "SP1042" (prefix เดียวกับชื่อผู้ใช้ SP1042.M01)</summary>
    public static string WorkspaceCode(int workspaceNo) =>
        string.Create(CultureInfo.InvariantCulture, $"SP{workspaceNo:D4}");

    public static DateTime ToThai(DateTime utc) => BranchPackageDates.ToBranchLocal(utc);

    /// <summary>วันตามปฏิทินไทยของเวลา UTC — ใช้นับ "เหลืออีกกี่วัน" และเป็น RunDate ของงานรายวัน</summary>
    public static DateOnly ThaiDate(DateTime utc) => DateOnly.FromDateTime(ToThai(utc));

    public static string FormatThaiDate(DateTime utc) => FormatThaiDate(ThaiDate(utc));

    public static string FormatThaiDate(DateOnly date) =>
        string.Create(CultureInfo.InvariantCulture, $"{date.Day:D2}/{date.Month:D2}/{date.Year + 543}");

    public static string FormatThaiDateTime(DateTime utc)
    {
        var t = ToThai(utc);
        return string.Create(CultureInfo.InvariantCulture,
            $"{t.Day:D2}/{t.Month:D2}/{t.Year + 543} {t.Hour:D2}:{t.Minute:D2} น.");
    }

    /// <summary>
    /// จำนวนวันที่เหลือแบบ "ปัดขึ้น" (ค่าเดียวกับที่เว็บคำนวณเองจาก trialEndsAt ใน lib/plan.ts) — ติดลบได้เมื่อหมดอายุแล้ว
    /// ใช้แสดงผลเท่านั้น · การส่งอีเมลเตือนนับตามวันปฏิทินไทยแทน (<see cref="CalendarDaysLeft"/>)
    /// </summary>
    public static int DaysLeft(DateTime endsAtUtc, DateTime nowUtc) =>
        (int)Math.Ceiling((endsAtUtc - nowUtc).TotalDays);

    /// <summary>
    /// จำนวนวันปฏิทินไทยจากวันนี้ถึงวันหมดอายุ — "พรุ่งนี้หมดอายุ" ต้องตรงกับวันที่บนปฏิทินจริง
    /// ถ้านับแบบปัดขึ้นจากชั่วโมง อีเมล "เหลือ 1 วัน" จะไปถึงในเช้าวันที่หมดอายุแล้ว (job รัน 03:30 · หมดอายุ 10:00)
    /// </summary>
    public static int CalendarDaysLeft(DateTime endsAtUtc, DateTime nowUtc) =>
        ThaiDate(endsAtUtc).DayNumber - ThaiDate(nowUtc).DayNumber;

    /// <summary>
    /// เบอร์ E.164 ของไทย (+66…) → รูปแบบที่คนไทยอ่าน (081-234-5678 / 02-123-4567) · รูปแบบอื่นคืนตามเดิม · ว่าง → "-"
    /// </summary>
    public static string DisplayPhone(string? e164)
    {
        if (string.IsNullOrWhiteSpace(e164)) return "-";
        var value = e164.Trim();
        if (!value.StartsWith("+66", StringComparison.Ordinal)) return SingleLine(value, 30);

        var national = "0" + value[3..];
        if (!national.All(char.IsAsciiDigit)) return SingleLine(value, 30);
        return national.Length switch
        {
            10 => $"{national[..3]}-{national[3..6]}-{national[6..]}",
            9 => $"{national[..2]}-{national[2..5]}-{national[5..]}",
            _ => national
        };
    }

    /// <summary>
    /// ข้อความที่ผู้ใช้พิมพ์เองก่อนใส่ในข้อความบรรทัดเดียว (LINE ทีมขาย) — ตัด CR/LF/TAB กันปลอมบรรทัด
    /// "โทร: …" ซ้อนเข้ามา (กติกาเดียวกับ ContactRequestService.Clean) และตัดความยาว
    /// </summary>
    public static string SingleLine(string? value, int maxLength)
    {
        var clean = string.Join(' ', (value ?? string.Empty)
            .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength].TrimEnd() + "…";
    }

    /// <summary>
    /// Guid ที่ได้ค่าเดิมทุกครั้งจากข้อความเดียวกัน — ใช้เป็น X-Line-Retry-Key ของข้อความที่ระบบส่งเอง
    /// (สรุปรายวัน · แจ้งหมดอายุ) ให้การรันซ้ำของ job ไม่ทำให้กลุ่ม LINE ได้ข้อความซ้ำภายใน 24 ชม.
    /// </summary>
    public static Guid DeterministicGuid(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var bytes = hash[..16];
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);  // version 5-ish — กันชนรูปแบบกับ Guid.NewGuid (v4)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    /// <summary>ตรวจ shard แบบเดียวกับ ServiceDbConnectionResolver.IsSelfServeShard (Infrastructure) — ไม่สนตัวพิมพ์</summary>
    public static bool IsSelfServeShard(string? shardKey, SelfServeOptions options) =>
        !string.IsNullOrWhiteSpace(shardKey)
        && string.Equals(shardKey.Trim(), options.ShardKey, StringComparison.OrdinalIgnoreCase);
}
