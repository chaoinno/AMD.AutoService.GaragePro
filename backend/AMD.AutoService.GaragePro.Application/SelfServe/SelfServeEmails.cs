using System.Globalization;
using System.Net;
using System.Text;
using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>เนื้อหาอีเมลที่ยังไม่ผูกผู้รับ — <see cref="To"/> แปลงเป็น <see cref="EmailMessage"/> สำหรับ IEmailSender</summary>
public sealed record EmailContent(string Subject, string TextBody, string HtmlBody, string Tag)
{
    public EmailMessage To(string address) => new(address, Subject, TextBody, HtmlBody, Tag);
}

/// <summary>ข้อมูลของอีเมลต้อนรับหลังเปิดอู่สำเร็จ (S1 เรียกหลัง provisioning + ข้อมูลตัวอย่าง)</summary>
/// <param name="OwnerLogin">สิ่งที่เจ้าของใช้เข้าสู่ระบบ — อีเมล (สมัครด้วยอีเมล) หรือชื่อผู้ใช้ SPnnnn.M01</param>
/// <param name="SignsInWithGoogle">สมัครด้วย Google = ไม่มีรหัสผ่านของระบบ ใช้ปุ่ม Google แทน</param>
/// <param name="Technicians">บัญชีช่างตัวอย่าง (รหัสผ่านส่งครั้งเดียว) — ว่าง = ไม่ได้ใส่ข้อมูลตัวอย่าง</param>
public sealed record WelcomeEmail(
    string GarageName,
    int WorkspaceNo,
    string OwnerLogin,
    bool SignsInWithGoogle,
    DateTime TrialEndsAtUtc,
    string PublicBaseUrl,
    IReadOnlyList<SampleCredential>? Technicians = null);

/// <summary>
/// อีเมลภาษาไทยของ self-serve ทั้งหมด (แผน S4) — เนื้อหาบอกเฉพาะข้อเท็จจริงของระบบ ไม่มีคำโฆษณาเกินจริง
/// · ทุกฉบับมี TextBody (บังคับ) + HtmlBody แบบเรียบ (escape ทุกค่า) — ไม่ฝังรูป/สคริปต์/ตัวติดตาม
/// · Tag ใช้จัดกลุ่มที่ผู้ให้บริการอีเมล: otp · account-exists · welcome · reminder-{n} · expired · comeback
/// · [SECURITY] ค่าที่ผู้ใช้กรอก (ชื่ออู่) ผ่าน <see cref="SelfServeText.SingleLine"/> และ HtmlEncode เสมอ
/// </summary>
public static class SelfServeEmails
{
    public const string OtpTag = "otp";
    public const string AccountExistsTag = "account-exists";
    public const string WelcomeTag = "welcome";
    public const string ExpiredTag = "expired";
    public const string ComebackTag = "comeback";

    public static string ReminderTag(int daysLeft) =>
        string.Create(CultureInfo.InvariantCulture, $"reminder-{daysLeft}");

    /// <summary>รหัสยืนยันอีเมล 6 หลัก (S1) — <paramref name="minutes"/> = อายุรหัส</summary>
    public static EmailContent Otp(string code, int minutes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return Build(OtpTag, $"รหัสยืนยันอีเมล ServicePro: {code}",
            "รหัสยืนยันอีเมลสำหรับสมัครทดลองใช้ ServicePro ของคุณคือ",
            code,
            $"รหัสนี้ใช้ได้ {minutes} นาที และใช้ได้ครั้งเดียว",
            "ห้ามบอกรหัสนี้กับผู้อื่น ทีมงาน ServicePro จะไม่ขอรหัสนี้จากคุณ",
            "ถ้าคุณไม่ได้เป็นคนขอรหัสนี้ ไม่ต้องทำอะไร");
    }

    /// <summary>
    /// มีคนขอสมัครด้วยอีเมลที่มีบัญชีอยู่แล้ว (S1 ส่งแทน OTP เพื่อไม่บอกใบ้ทางหน้าเว็บว่าอีเมลนี้มีบัญชี)
    /// · ใช้ทั้งบัญชีที่ใช้งานอยู่ ถูกระงับ และถูกลบข้อมูลแล้ว จึงบอกทางติดต่อทีมงานไว้ด้วย
    /// </summary>
    public static EmailContent AccountExists(string publicBaseUrl) =>
        Build(AccountExistsTag, "มีการขอสมัคร ServicePro ด้วยอีเมลของคุณ",
            "มีการขอสมัครทดลองใช้ ServicePro ด้วยอีเมลนี้ แต่อีเมลนี้เคยใช้สมัครไว้แล้ว ระบบจึงไม่ได้สร้างบัญชีใหม่",
            $"เข้าสู่ระบบด้วยอีเมลนี้ได้ที่ {LoginUrl(publicBaseUrl)}",
            $"ถ้าเข้าสู่ระบบไม่ได้ ลืมรหัสผ่าน หรือบัญชีถูกปิดไปแล้ว ติดต่อ {Contact()}",
            "ถ้าคุณไม่ได้เป็นคนขอสมัคร ไม่ต้องทำอะไร");

    public static EmailContent Welcome(WelcomeEmail model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var name = Garage(model.GarageName);
        var code = SelfServeText.WorkspaceCode(model.WorkspaceNo);

        var paragraphs = new List<string>
        {
            $"อู่ {name} เปิดใช้งาน ServicePro แบบทดลองใช้แล้ว",
            $"รหัสอู่: {code}\nทดลองใช้ได้ถึง: {SelfServeText.FormatThaiDateTime(model.TrialEndsAtUtc)}",
            $"เข้าสู่ระบบบนเว็บ: {LoginUrl(model.PublicBaseUrl)}\n" + (model.SignsInWithGoogle
                ? $"ใช้ปุ่ม \"เข้าสู่ระบบด้วย Google\" ด้วยบัญชี {model.OwnerLogin}"
                : $"ชื่อผู้ใช้: {model.OwnerLogin}\nรหัสผ่าน: รหัสที่คุณตั้งไว้ตอนสมัคร")
        };

        if (model.Technicians is { Count: > 0 } technicians)
        {
            var lines = new StringBuilder("บัญชีช่างตัวอย่าง สำหรับเข้าแอปมือถือ ServicePro:");
            foreach (var t in technicians)
                lines.Append('\n').Append("- ชื่อผู้ใช้ ").Append(t.UserName).Append(" รหัสผ่าน ").Append(t.Password);
            lines.Append("\nระบบเก็บรหัสผ่านแบบเข้ารหัสและแสดงให้ครั้งนี้ครั้งเดียว กรุณาเก็บไว้");
            paragraphs.Add(lines.ToString());
        }

        paragraphs.Add($"แอปมือถือสำหรับช่าง (Android): {SelfServeText.PlayStoreUrl}");
        paragraphs.Add($"แอปมือถือสำหรับช่าง (iPhone): {SelfServeText.AppStoreUrl}");
        paragraphs.Add("ระหว่างทดลองใช้ ฟีเจอร์ขั้นสูงยังปิดอยู่: " + PlanFeatureNames.LabelTh(PlanFeature.All));
        paragraphs.Add($"สอบถามหรือสนใจแพ็กเกจ: {Contact()}");

        return Build(WelcomeTag, $"ยินดีต้อนรับสู่ ServicePro | อู่ {name} ({code}) พร้อมใช้งานแล้ว", [.. paragraphs]);
    }

    /// <summary>
    /// เตือนก่อนหมดอายุ — <paramref name="daysLeft"/> = จำนวนวันปฏิทินไทยที่เหลือจริง (<see cref="SelfServeText.CalendarDaysLeft"/>)
    /// ไม่ใช่ค่าใน SelfServe:Trial:ReminderDays — ถ้างานรายวันพลาดไปวันหนึ่ง หัวเรื่องต้องยังบอกวันถูก ("วันนี้"/"พรุ่งนี้")
    /// </summary>
    public static EmailContent Reminder(
        int daysLeft, string garageName, int workspaceNo, DateTime trialEndsAtUtc, int readOnlyDays, string publicBaseUrl)
    {
        var name = Garage(garageName);
        var code = SelfServeText.WorkspaceCode(workspaceNo);
        var subject = daysLeft switch
        {
            <= 0 => $"ServicePro: ทดลองใช้ของอู่ {name} จะหมดอายุวันนี้",
            1 => $"ServicePro: ทดลองใช้ของอู่ {name} จะหมดอายุพรุ่งนี้",
            _ => $"ServicePro: ทดลองใช้ของอู่ {name} เหลืออีก {daysLeft} วัน"
        };

        return Build(ReminderTag(daysLeft), subject,
            $"ช่วงทดลองใช้ ServicePro ของอู่ {name} ({code}) จะสิ้นสุดเมื่อ {SelfServeText.FormatThaiDateTime(trialEndsAtUtc)}",
            $"หลังหมดอายุ ข้อมูลยังอยู่ครบ เข้าสู่ระบบ ดู และพิมพ์เอกสารได้อีก {readOnlyDays} วัน แต่เพิ่มหรือแก้ไขข้อมูลไม่ได้",
            $"สนใจใช้งานต่อ: กด \"ขอต่ออายุ\" บนแถบทดลองใช้ในระบบ ({LoginUrl(publicBaseUrl)}) หรือติดต่อ {Contact()}");
    }

    /// <summary>หมดอายุแล้ว — ระบบอ่านอย่างเดียว · ลบข้อมูลถาวรเมื่อครบ <paramref name="readOnlyDays"/> วัน</summary>
    public static EmailContent Expired(
        string garageName, int workspaceNo, DateTime expiredAtUtc, int readOnlyDays, DateTime purgeAtUtc, string publicBaseUrl)
    {
        var name = Garage(garageName);
        var code = SelfServeText.WorkspaceCode(workspaceNo);
        return Build(ExpiredTag, $"ServicePro: ช่วงทดลองใช้ของอู่ {name} สิ้นสุดแล้ว",
            $"ช่วงทดลองใช้ ServicePro ของอู่ {name} ({code}) สิ้นสุดเมื่อ {SelfServeText.FormatThaiDateTime(expiredAtUtc)}",
            $"ตอนนี้ระบบเป็นแบบอ่านอย่างเดียว: เข้าสู่ระบบ ดู และพิมพ์เอกสารได้ที่ {LoginUrl(publicBaseUrl)} แต่เพิ่มหรือแก้ไขข้อมูลไม่ได้",
            $"ข้อมูลของอู่จะถูกลบถาวรตั้งแต่วันที่ {SelfServeText.FormatThaiDate(purgeAtUtc)} ({readOnlyDays} วันหลังหมดอายุ)",
            $"ต้องการใช้งานต่อหรือขอต่ออายุ: กด \"ขอต่ออายุ\" ในระบบ หรือติดต่อ {Contact()}");
    }

    /// <summary>ชวนกลับมาใช้ (ครั้งเดียว ตาม SelfServe:Trial:ComebackDay) — ข้อมูลยังอยู่จนถึงวันลบ</summary>
    public static EmailContent Comeback(
        string garageName, int workspaceNo, DateTime expiredAtUtc, DateTime purgeAtUtc, string publicBaseUrl)
    {
        var name = Garage(garageName);
        var code = SelfServeText.WorkspaceCode(workspaceNo);
        return Build(ComebackTag, $"ServicePro: ข้อมูลของอู่ {name} ยังเก็บไว้ถึง {SelfServeText.FormatThaiDate(purgeAtUtc)}",
            $"ช่วงทดลองใช้ของอู่ {name} ({code}) สิ้นสุดไปแล้วเมื่อ {SelfServeText.FormatThaiDate(expiredAtUtc)}",
            $"ข้อมูลทั้งหมดของอู่ยังเก็บไว้ถึงวันที่ {SelfServeText.FormatThaiDate(purgeAtUtc)} หลังจากนั้นจะถูกลบถาวร",
            $"สมัครแพ็กเกจหรือขอต่ออายุเพื่อใช้งานต่อด้วยข้อมูลเดิม: {Contact()}",
            $"เข้าสู่ระบบเพื่อดูข้อมูล: {LoginUrl(publicBaseUrl)}");
    }

    // ---------- แพ็กเกจที่ชำระเงิน (docs/17 §5.4) · ไม่มีคำว่า "ทดลองใช้" ในฉบับเหล่านี้ ----------

    public const string PaymentApprovedTag = "package-approved";
    public const string PaymentRejectedTag = "package-rejected";

    public static string PaidReminderTag(int days) =>
        string.Create(CultureInfo.InvariantCulture, $"paid-reminder-{days}");

    /// <summary>
    /// ทีมงานอนุมัติการชำระแล้ว · <paramref name="endsAtUtc"/> = EndPackageDate ใหม่ (เที่ยงคืนไทยของวันแรกที่หมดสิทธิ์)
    /// แสดงเป็น "วันสุดท้ายที่ใช้ได้" (End − 1 tick) ให้ตรงกับหน้าเว็บ/แอป
    /// </summary>
    public static EmailContent PaymentApproved(
        string garageName, int workspaceNo, int months, DateTime endsAtUtc, string publicBaseUrl)
    {
        var name = Garage(garageName);
        var code = SelfServeText.WorkspaceCode(workspaceNo);
        var until = SelfServeText.FormatThaiDate(Package.PackageRenewal.LastUsableDay(endsAtUtc));
        return Build(PaymentApprovedTag, $"ServicePro: อนุมัติการชำระเงินของอู่ {name} แล้ว ใช้งานได้ถึง {until}",
            $"ทีมงานตรวจการชำระค่าแพ็กเกจ {months.ToString(CultureInfo.InvariantCulture)} เดือนของอู่ {name} ({code}) เรียบร้อยแล้ว",
            $"ใช้งานได้ถึงสิ้นวันที่ {until}",
            "ถ้าก่อนหน้านี้ระบบเป็นแบบอ่านอย่างเดียว จะเพิ่มและแก้ไขข้อมูลได้ตามปกติภายในประมาณ 1 นาที",
            $"ดูประวัติการชำระได้ที่ {PackageUrl(publicBaseUrl)}",
            $"สอบถาม: {Contact()}");
    }

    /// <summary>ทีมงานปฏิเสธ · <paramref name="reasonTh"/> = ข้อความตายตัวของเหตุผล · <paramref name="note"/> = หมายเหตุทีมงาน (ไม่บังคับ)</summary>
    public static EmailContent PaymentRejected(
        string garageName, int workspaceNo, int months, string reasonTh, string? note, string publicBaseUrl)
    {
        var name = Garage(garageName);
        var code = SelfServeText.WorkspaceCode(workspaceNo);
        var paragraphs = new List<string>
        {
            $"ทีมงานตรวจการแจ้งชำระค่าแพ็กเกจ {months.ToString(CultureInfo.InvariantCulture)} เดือนของอู่ {name} ({code}) แล้ว แต่ยังอนุมัติไม่ได้",
            $"เหตุผล: {SelfServeText.SingleLine(reasonTh, 200)}"
        };
        if (!string.IsNullOrWhiteSpace(note))
            paragraphs.Add($"หมายเหตุจากทีมงาน: {SelfServeText.SingleLine(note, 1000)}");
        paragraphs.Add($"แจ้งชำระใหม่ได้ที่ {PackageUrl(publicBaseUrl)} หรือสอบถาม {Contact()}");

        return Build(PaymentRejectedTag, $"ServicePro: การแจ้งชำระของอู่ {name} ยังไม่ผ่านการตรวจ", [.. paragraphs]);
    }

    /// <summary>
    /// เตือนอู่ที่ชำระเงินแล้วก่อนหมดอายุ · <paramref name="daysLeft"/> = จำนวนวันปฏิทินไทยจากวันนี้ถึง<b>วันสุดท้ายที่ใช้ได้</b>
    /// (0 = วันนี้เป็นวันสุดท้าย · กติกาเดียวกับ PlanAccess.DaysLeft) · tag = paid-reminder-{รอบเตือนตาม config}
    /// </summary>
    public static EmailContent PaidReminder(
        int daysLeft, string garageName, int workspaceNo, DateTime endsAtUtc, int readOnlyDays, string publicBaseUrl)
    {
        var name = Garage(garageName);
        var code = SelfServeText.WorkspaceCode(workspaceNo);
        var until = SelfServeText.FormatThaiDate(Package.PackageRenewal.LastUsableDay(endsAtUtc));
        var subject = daysLeft switch
        {
            <= 0 => $"ServicePro: วันนี้เป็นวันสุดท้ายของแพ็กเกจอู่ {name}",
            1 => $"ServicePro: แพ็กเกจของอู่ {name} ใช้ได้ถึงพรุ่งนี้",
            _ => $"ServicePro: แพ็กเกจของอู่ {name} เหลืออีก {daysLeft} วัน"
        };

        return Build(PaidReminderTag(Math.Max(daysLeft, 0)), subject,
            $"แพ็กเกจ ServicePro ของอู่ {name} ({code}) ใช้งานได้ถึงสิ้นวันที่ {until}",
            $"หลังหมดอายุ ข้อมูลยังอยู่ครบ เข้าสู่ระบบ ดู และพิมพ์เอกสารได้อีก {readOnlyDays} วัน แต่เพิ่มหรือแก้ไขข้อมูลไม่ได้",
            $"ต่ออายุ: แจ้งชำระเงินได้ที่หน้า \"แพ็กเกจและการชำระเงิน\" ในระบบ ({PackageUrl(publicBaseUrl)}) หรือติดต่อ {Contact()}");
    }

    private static string PackageUrl(string? publicBaseUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? new SelfServeOptions().PublicBaseUrl
            : publicBaseUrl.Trim();
        return baseUrl.TrimEnd('/') + "/package";
    }

    private static string Contact() => $"{SelfServeText.ContactLine} หรือโทร {SelfServeText.ContactPhone}";

    private static string Garage(string? garageName)
    {
        var name = SelfServeText.SingleLine(garageName, 100);
        return name.Length == 0 ? "ของคุณ" : name;
    }

    private static string LoginUrl(string? publicBaseUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(publicBaseUrl)
            ? new SelfServeOptions().PublicBaseUrl
            : publicBaseUrl.Trim();
        return baseUrl.TrimEnd('/') + "/login";
    }

    private static EmailContent Build(string tag, string subject, params string[] paragraphs)
    {
        var text = new StringBuilder();
        var html = new StringBuilder(
            "<div style=\"font-family:'Noto Sans Thai','Noto Sans',sans-serif;font-size:15px;line-height:1.6;color:#0f2440\">");

        foreach (var paragraph in paragraphs)
        {
            if (text.Length > 0) text.Append("\n\n");
            text.Append(paragraph);

            html.Append("<p>")
                .Append(string.Join("<br>", paragraph.Split('\n').Select(WebUtility.HtmlEncode)))
                .Append("</p>");
        }

        text.Append("\n\nทีมงาน ServicePro");
        html.Append("<p style=\"color:#64748b\">ทีมงาน ServicePro</p></div>");
        return new EmailContent(subject, text.ToString(), html.ToString(), tag);
    }
}
