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

    public static EmailMessage Otp(string to, string code, int minutes) => new(
        to,
        $"รหัสยืนยันการสมัคร ServicePro: {code}",
        new StringBuilder()
            .AppendLine("รหัสยืนยันอีเมลสำหรับสมัครทดลองใช้ ServicePro ของคุณคือ")
            .AppendLine()
            .AppendLine($"    {code}")
            .AppendLine()
            .AppendLine($"รหัสนี้ใช้ได้ {minutes} นาที — อย่าบอกรหัสนี้กับผู้อื่น ทีมงาน ServicePro จะไม่ถามรหัสนี้จากคุณ")
            .AppendLine("ถ้าคุณไม่ได้สมัคร ServicePro ไม่ต้องทำอะไร ระบบจะไม่สร้างบัญชีให้")
            .ToString(),
        Tag: "otp");

    /// <summary>
    /// [SECURITY] ส่งแทน OTP เมื่ออีเมลมีบัญชีอยู่แล้ว — หน้าเว็บตอบเหมือนกันทุกกรณี (กัน enumeration)
    /// คนที่เป็นเจ้าของอีเมลจริงจึงรู้จากกล่องจดหมายเท่านั้นว่าต้องเข้าสู่ระบบแทนการสมัครใหม่
    /// </summary>
    public static EmailMessage AccountExists(string to, string publicBaseUrl) => new(
        to,
        "อีเมลนี้มีบัญชี ServicePro อยู่แล้ว",
        new StringBuilder()
            .AppendLine("มีการขอสมัครทดลองใช้ ServicePro ด้วยอีเมลนี้ แต่อีเมลนี้มีบัญชีอยู่แล้ว (หรือเคยใช้ทดลองใช้ไปแล้ว)")
            .AppendLine()
            .AppendLine($"เข้าสู่ระบบได้ที่ {publicBaseUrl.TrimEnd('/')}/login ด้วยอีเมลนี้ หรือปุ่ม \"เข้าสู่ระบบด้วย Google\"")
            .AppendLine($"ลืมรหัสผ่าน ต้องการต่ออายุ หรือสมัครแพ็กเกจ ติดต่อ {PlanErrors.ContactLine}")
            .AppendLine()
            .AppendLine("ถ้าคุณไม่ได้ขอสมัคร ไม่ต้องทำอะไร")
            .ToString(),
        Tag: "account-exists");

    public static EmailMessage Welcome(
        string to, string garageName, string workspaceNo, DateTime? trialEndsAtUtc, bool viaGoogle,
        string publicBaseUrl, IReadOnlyList<SampleCredential> technicians)
    {
        var sb = new StringBuilder()
            .AppendLine($"ยินดีต้อนรับสู่ ServicePro — อู่ {OneLine(garageName)} พร้อมใช้งานแล้ว")
            .AppendLine()
            .AppendLine($"รหัสอู่: {workspaceNo}");
        if (trialEndsAtUtc is { } end)
            sb.AppendLine($"ทดลองใช้ฟรีถึงวันที่ {ThaiDate(end)}");
        sb.AppendLine()
            .AppendLine($"เข้าสู่ระบบ: {publicBaseUrl.TrimEnd('/')}/login")
            .AppendLine(viaGoogle
                ? $"ใช้ปุ่ม \"เข้าสู่ระบบด้วย Google\" ด้วยบัญชี {to}"
                : $"ใช้อีเมล {to} กับรหัสผ่านที่ตั้งตอนสมัคร");

        if (technicians.Count > 0)
        {
            sb.AppendLine()
                .AppendLine("รหัสช่างตัวอย่าง สำหรับลองแอป ServicePro บนมือถือ (ข้อมูลตัวอย่าง — ลบได้ในหน้าจ๊อบ):");
            foreach (var t in technicians)
                sb.AppendLine($"  • {t.UserName} / รหัสผ่าน {t.Password}");
        }

        sb.AppendLine()
            .AppendLine($"มีคำถามหรือต้องการให้ช่วยตั้งค่า ติดต่อ {PlanErrors.ContactLine}");

        return new EmailMessage(to, $"ยินดีต้อนรับสู่ ServicePro — {workspaceNo}", sb.ToString(), Tag: "welcome");
    }

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
