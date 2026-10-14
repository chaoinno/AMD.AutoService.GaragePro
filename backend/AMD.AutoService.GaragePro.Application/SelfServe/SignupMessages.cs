using System.Globalization;
using System.Text;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>ข้อมูลที่ข้อความแจ้งทีมขายต้องใช้ — ค่าที่ผู้สมัครพิมพ์ทั้งหมดถูกทำให้เป็นบรรทัดเดียวก่อนใส่ข้อความ</summary>
public sealed record SignupSalesInfo(
    string GarageName,
    string WorkspaceNo,
    string PhoneE164,
    string? ProvinceName,
    string? Size,
    string Email,
    bool ViaGoogle,
    bool SampleData,
    bool DuplicatePhone);

/// <summary>
/// ข้อความที่ signup ส่งออก (อีเมล OTP · มีบัญชีอยู่แล้ว · ต้อนรับ · LINE ทีมขาย) — ภาษาไทยทั้งหมด
/// · [RISK] S4 ทำ <c>SelfServeEmails</c> (OTP/account-exists/welcome/เตือนหมดอายุ) ขนานกัน — integrator: เมื่อ merge แล้ว
///   ให้ SignupService ใช้ของ S4 สำหรับอีเมล 3 ฉบับนี้แล้วลบเมธอดอีเมลในไฟล์นี้ (คงเฉพาะ <see cref="SalesLine"/>)
/// · [SECURITY] ไม่ใส่ลิงก์ที่มี token ใดๆ ในอีเมล — OTP เป็นตัวเลขให้พิมพ์เอง
/// </summary>
public static class SignupMessages
{
    private static readonly TimeZoneInfo Bangkok = ResolveBangkok();

    // [แก้ตอนรวมงาน] อีเมล OTP/บัญชีมีอยู่แล้ว/ต้อนรับ ย้ายไปใช้ SelfServeEmails (S4) ที่เดียว — ไฟล์นี้เหลือข้อความ LINE ทีมขาย

    /// <summary>ข้อความแจ้งทีมขายในกลุ่ม LINE (แผน S1) — ไม่มีข้อมูลลับ (ไม่มีรหัสผ่าน/token)</summary>
    public static string SalesLine(SignupSalesInfo info)
    {
        var sb = new StringBuilder()
            .AppendLine("🆕 สมัครทดลองใช้ ServicePro")
            .AppendLine($"อู่: {OneLine(info.GarageName)} ({info.WorkspaceNo})")
            .AppendLine($"โทร: {ThaiPhone.FormatLocal(info.PhoneE164)}")
            .AppendLine($"จังหวัด: {Or(info.ProvinceName)}")
            .AppendLine($"ขนาด: {(string.IsNullOrWhiteSpace(info.Size) ? "-" : $"{OneLine(info.Size)} ช่าง")}")
            .AppendLine($"อีเมล: {OneLine(info.Email)}")
            .Append($"ช่องทาง: {(info.ViaGoogle ? "Google" : "อีเมล")} · ข้อมูลตัวอย่าง: {(info.SampleData ? "ใส่" : "ไม่ใส่")}");
        if (info.DuplicatePhone)
            sb.AppendLine().Append("⚠️ เบอร์นี้เคยสมัครแล้ว");
        return sb.ToString();
    }

    public static string WorkspaceNoText(int workspaceNo) =>
        string.Create(CultureInfo.InvariantCulture, $"SP{workspaceNo:D4}");

    /// <summary>ส่วนหน้า @ ของอีเมล (ค่าที่ผู้ใช้พิมพ์) — ชื่อเจ้าของสำรองเมื่อไม่มีชื่อจาก Google</summary>
    public static string LocalPart(string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return local.Length > 50 ? local[..50] : local;
    }

    // ช่องบรรทัดเดียวห้ามมีขึ้นบรรทัดใหม่ — กันปลอมบรรทัด "โทร: ..." ซ้อนเข้ามาในข้อความกลุ่ม (เหมือนฟอร์มติดต่อ)
    private static string OneLine(string? value) =>
        string.Join(' ', (value ?? "").Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : OneLine(value);

    private static string ThaiDate(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Bangkok);
        // พ.ศ. คำนวณเองแทนการพึ่ง culture ของเครื่อง (container แบบ invariant ให้ ค.ศ.)
        return string.Create(CultureInfo.InvariantCulture, $"{local:dd/MM}/{local.Year + 543}");
    }

    private static TimeZoneInfo ResolveBangkok()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "ICT", "ICT"); }
    }
}
