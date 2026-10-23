using System.Globalization;
using System.Text.Json.Nodes;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>
/// การ์ด Flex ของการแจ้งชำระอู่ sp1 ในกลุ่ม LINE ทีมงาน (docs/17 §8.7.2) · ฟังก์ชันล้วน (เทสต์ JSON ได้ตรง)
/// · ปุ่มเป็น message action: กดแล้วข้อความ <c>/SP_…</c> ขึ้นในกลุ่มในนามคนกด แล้ว webhook ของระบบเดิมส่งต่อมาที่
///   <c>POST /api/v1/public/line-commands</c> · ไม่ตั้ง <c>LineCommandKey</c> = การ์ดไม่มีปุ่ม (อนุมัติผ่าน Admin/spadmin)
/// · ยอดโอนไม่ตรงราคา: ไม่มีปุ่มอนุมัติ (อนุมัติต้องมีหมายเหตุ ทำได้ใน Admin/spadmin เท่านั้น) · ปุ่มปฏิเสธยังอยู่
/// · [UI] ไม่ใช้ emoji · ไอคอนเป็น PNG ของ Material Design Icons (<see cref="SelfServeOptions.PackageOptions.EffectiveLineIconBaseUrl"/>)
///   ไอคอนมีแค่สีขาวกับดำจาง จึงวางไอคอนขาวบนวงกลม/ปุ่มสี และไอคอนดำจางหน้าป้ายแถว · ปุ่มทำจาก box (ปุ่ม Flex ปกติใส่ไอคอนไม่ได้)
/// · [SECURITY] ค่าที่ลูกค้าพิมพ์เอง (ชื่ออู่ · หมายเหตุ) ผ่าน <see cref="SelfServeText.SingleLine"/> · ข้อความทุกช่องไม่ว่าง (LINE ไม่รับ text ว่าง)
/// </summary>
public static class PackagePaymentFlex
{
    private const string Navy = "#0F2440";
    private const string Muted = "#5B6B82";
    private const string Danger = "#B42318";
    private const string DangerSoft = "#FDECEC";
    private const string DangerBorder = "#F3C5C0";
    private const string Success = "#0E7C66";
    private const string PendingColor = "#A15C00";
    private const string Ink = "#0F172A";
    private const string Accent = "#1552B3";
    private const string Surface = "#EEF2F8";
    private const string HeaderSub = "#C9D4E5";

    /// <summary>ไอคอน (path ตามโครงของแพ็กเกจ material-design-icons 3.0.1 · 96px) · ไฟล์ชุดเดียวกันอยู่ที่ web/public/line-icons/</summary>
    public static class Icons
    {
        public const string Receipt = "action/2x_web/ic_receipt_white_48dp.png";
        public const string Hourglass = "action/2x_web/ic_hourglass_empty_white_48dp.png";
        public const string CheckCircle = "action/2x_web/ic_check_circle_white_48dp.png";
        public const string Close = "navigation/2x_web/ic_close_white_48dp.png";
        public const string Warning = "alert/2x_web/ic_warning_white_48dp.png";
        public const string BrokenImage = "image/2x_web/ic_broken_image_white_48dp.png";
        public const string MoneyOff = "editor/2x_web/ic_money_off_white_48dp.png";
        public const string Search = "action/2x_web/ic_search_white_48dp.png";
        public const string Store = "action/2x_web/ic_store_black_48dp.png";
        public const string Money = "editor/2x_web/ic_monetization_on_black_48dp.png";
        public const string Schedule = "action/2x_web/ic_schedule_black_48dp.png";
        public const string Event = "action/2x_web/ic_event_black_48dp.png";
        public const string EventAvailable = "notification/2x_web/ic_event_available_black_48dp.png";
        public const string Comment = "editor/2x_web/ic_mode_comment_black_48dp.png";
        public const string Photo = "image/2x_web/ic_photo_black_48dp.png";
        public const string Person = "social/2x_web/ic_person_black_48dp.png";
        public const string Info = "action/2x_web/ic_info_black_48dp.png";

        public static readonly IReadOnlyList<string> All =
        [
            Receipt, Hourglass, CheckCircle, Close, Warning, BrokenImage, MoneyOff, Search, Store, Money, Schedule, Event,
            EventAvailable, Comment, Photo, Person, Info
        ];
    }

    /// <summary>ปุ่มปฏิเสธบนการ์ด (เหตุผลที่ทีมงานใช้บ่อย · อื่นๆ/สลิปซ้ำต้องมีหมายเหตุหรือดูประวัติ ทำใน Admin)</summary>
    public static readonly IReadOnlyList<(PackagePaymentRejectReason Reason, string Label, string Icon)> RejectButtons =
    [
        (PackagePaymentRejectReason.SlipUnreadable, "สลิปไม่ชัด", Icons.BrokenImage),
        (PackagePaymentRejectReason.AmountMismatch, "ยอดไม่ตรง", Icons.MoneyOff),
        (PackagePaymentRejectReason.NotReceived, "ไม่พบเงินเข้า", Icons.Search)
    ];

    public const string ApproveLabel = "อนุมัติ";

    /// <param name="slipLink">ลิงก์สลิปที่เซ็นแล้ว (null = ไม่ได้ตั้งคีย์) · รูป JPEG + https = รูปในหัวข้อหลักฐาน · อื่นๆ = ปุ่มเปิดสลิป</param>
    /// <remarks>
    /// [UI] วางตามการ์ดแจ้งชำระเดิมของ GaragePro ที่ทีมขายคุ้นมือ: ชื่ออู่ → จำนวนเดือนตัวใหญ่ → รายละเอียด → รูปสลิปใต้ "หลักฐานการโอน"
    /// → ปุ่มอนุมัติเด่นปุ่มเดียว · ปุ่มปฏิเสธเป็นกรอบแดงบาง (แบบ Cancel ของการ์ดเดิม) ไม่แย่งสายตาจากปุ่มอนุมัติ ·
    /// ป้ายแถวกว้างตามข้อความ (flex 0) ค่าที่ยาวอย่าง "22/10/2569 23:50 น." จึงไม่ตกบรรทัด
    /// </remarks>
    public static LineOutboundMessage Submitted(PackagePaymentSubmittedNotice notice, string? slipLink,
        SelfServeOptions.PackageOptions options, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(notice);
        ArgumentNullException.ThrowIfNull(options);
        var icons = options.EffectiveLineIconBaseUrl;
        var c = notice.Context;
        var p = c.Payment;
        var shortId = PackagePaymentRules.ShortId(p.Id);
        var shop = SelfServeText.SingleLine(c.WorkspaceName, 100) is { Length: > 0 } name ? name : "-";
        var code = SelfServeText.WorkspaceCode(c.WorkspaceNo);
        var mismatch = p.ExpectedAmount is { } expected && expected != p.Amount;
        var months = p.MonthCount.ToString(CultureInfo.InvariantCulture);

        var body = new JsonArray
        {
            Text(shop, size: "lg", weight: "bold", color: Ink),
            IconLine(icons, Icons.Store, $"{code} · โทร {SelfServeText.DisplayPhone(c.WorkspacePhone)}", "sm"),
            Summary(months, p.Amount, p.ExpectedAmount, mismatch)
        };

        var standard = p.ExpectedAmount is { } std ? PackagePaymentMessages.Baht(std) : "ยังไม่ตั้งราคามาตรฐาน";
        body.Add(Row(icons, Icons.Money, notice.Price?.VatRate is null ? "ราคามาตรฐาน" : "ราคารวม VAT", standard, margin: "lg"));
        if (notice.Price is { VatRate: { } rate } price && p.ExpectedAmount == price.Total)
            body.Add(Text($"ก่อน VAT {PackagePaymentMessages.Number(price.Subtotal)} + VAT {PackagePricing.Percent(rate)} " +
                          PackagePaymentMessages.Number(price.Vat), size: "xs", color: Muted, align: "end"));

        body.Add(Row(icons, Icons.Schedule, "เวลาโอน", SelfServeText.FormatThaiDateTime(p.PaidAt)));
        body.Add(Row(icons, Icons.Event, "ตอนนี้ใช้ได้ถึง", notice.CurrentEndsAt is { } end
            ? SelfServeText.FormatThaiDate(PackageRenewal.LastUsableDay(end))
            : "ไม่มีกำหนด"));
        body.Add(Row(icons, Icons.EventAvailable, "ถ้าอนุมัติวันนี้",
            $"ใช้ได้ถึง {SelfServeText.FormatThaiDate(notice.PreviewIfApprovedNow.LastUsableDate)}", Navy, bold: true));
        if (!string.IsNullOrWhiteSpace(p.Note))
            body.Add(Row(icons, Icons.Comment, "หมายเหตุ", SelfServeText.SingleLine(p.Note, 200)));

        if (mismatch)
            body.Add(Callout(icons, "ยอดโอนไม่ตรงราคา อนุมัติได้ใน Admin/spadmin พร้อมหมายเหตุ (เช่น ส่วนลด)"));
        foreach (var d in notice.Duplicates.Take(3))
            body.Add(Callout(icons, $"สลิปซ้ำกับรายการ {PackagePaymentRules.ShortId(d.PaymentId)} ({d.WorkspaceNo} · {d.Status})"));

        var linkIsHttps = slipLink is not null && slipLink.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        var slipImage = linkIsHttps && p.SlipContentType == "image/jpeg";
        body.Add(new JsonObject { ["type"] = "separator", ["margin"] = "lg" });
        var heading = IconLine(icons, Icons.Photo, "หลักฐานการโอน", "sm", bold: true);
        heading["margin"] = "lg";
        body.Add(heading);
        if (slipImage)
        {
            body.Add(new JsonObject
            {
                ["type"] = "image",
                ["url"] = slipLink,
                ["size"] = "full",
                ["aspectRatio"] = "1:1",
                ["aspectMode"] = "fit",
                ["backgroundColor"] = Surface,
                ["margin"] = "sm",
                ["action"] = Uri("ดูสลิป", slipLink!)
            });
            body.Add(Text("แตะรูปเพื่อดูสลิปขนาดเต็ม", size: "xxs", color: Muted, align: "center"));
        }
        else if (slipLink is not null)
        {
            body.Add(new JsonObject
            {
                ["type"] = "button", ["style"] = "secondary", ["height"] = "sm", ["margin"] = "sm",
                ["action"] = Uri(p.SlipContentType == "application/pdf" ? "เปิดสลิป PDF" : "เปิดสลิป", slipLink)
            });
        }
        else
        {
            body.Add(Text($"ดูสลิปได้ใน GaragePro Admin หรือ spadmin payment --id {shortId}", size: "xs", color: Muted));
        }

        var submittedBy = SelfServeText.SingleLine(p.SubmittedByUserName, 100);
        var by = IconLine(icons, Icons.Person,
            $"ผู้แจ้ง {(submittedBy.Length > 0 ? submittedBy : "-")} · แจ้งเมื่อ {SelfServeText.FormatThaiDateTime(p.SubmittedAt)}", "xxs");
        by["margin"] = "lg";
        body.Add(by);

        var bubble = new JsonObject
        {
            ["type"] = "bubble",
            ["size"] = "mega",
            ["header"] = Header(icons, Icons.Receipt, "แจ้งชำระเงิน", $"ServicePro · รายการ {shortId}", Navy,
                Pill(icons, Icons.Hourglass, "รอตรวจ", PendingColor)),
            ["body"] = new JsonObject
            {
                ["type"] = "box", ["layout"] = "vertical", ["spacing"] = "sm", ["paddingAll"] = "16px", ["contents"] = body
            },
            ["footer"] = Footer(icons, p, shortId, mismatch, options, nowUtc)
        };

        var alt = $"แจ้งชำระ {shop} ({code}) {months} เดือน {PackagePaymentMessages.Baht(p.Amount)}" +
                  (mismatch ? " (ยอดไม่ตรงราคา)" : "");
        return LineOutboundMessage.FromFlex(Limit(alt, 400), bubble);
    }

    /// <summary>คำตอบหลังอนุมัติผ่าน LINE: "อนุมัติแล้ว ใช้งานได้ถึง …"</summary>
    public static LineOutboundMessage Approved(PackagePaymentReviewResult result, string actorDisplay,
        SelfServeOptions.PackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        var icons = options.EffectiveLineIconBaseUrl;
        var item = result.Payment;
        var until = result.EndsTo is { } end ? SelfServeText.FormatThaiDate(PackageRenewal.LastUsableDay(end)) : "-";
        var shop = SelfServeText.SingleLine(item.WorkspaceName, 100) is { Length: > 0 } name ? name : "-";
        var months = item.Payment.MonthCount?.ToString(CultureInfo.InvariantCulture) ?? "-";

        var body = new JsonArray
        {
            Text(shop, size: "md", weight: "bold", color: Ink),
            IconLine(icons, Icons.Store, item.WorkspaceNo, "sm"),
            new JsonObject
            {
                ["type"] = "box",
                ["layout"] = "vertical",
                ["backgroundColor"] = Surface,
                ["cornerRadius"] = "12px",
                ["paddingAll"] = "14px",
                ["margin"] = "md",
                ["contents"] = new JsonArray
                {
                    Text("ใช้งานได้ถึง", size: "xs", color: Muted),
                    Text(until, size: "xxl", weight: "bold", color: Success)
                }
            },
            Row(icons, Icons.Event, "ต่ออายุ", $"{months} เดือน", margin: "md"),
            Row(icons, Icons.Person, "ผู้อนุมัติ", $"{SelfServeText.SingleLine(actorDisplay, 80)} (LINE)"),
            Row(icons, Icons.Info, "แจ้งเจ้าของอู่", NotifiedText(result.EmailSent, result.InAppSaved))
        };

        var bubble = new JsonObject
        {
            ["type"] = "bubble",
            ["size"] = "kilo",
            ["header"] = Header(icons, Icons.CheckCircle, "อนุมัติแล้ว", $"รายการ {item.Payment.ShortId}", Success),
            ["body"] = new JsonObject
            {
                ["type"] = "box", ["layout"] = "vertical", ["spacing"] = "sm", ["paddingAll"] = "16px", ["contents"] = body
            }
        };
        return LineOutboundMessage.FromFlex(Limit($"อนุมัติแล้ว {shop} ({item.WorkspaceNo}) ใช้งานได้ถึง {until}", 400), bubble);
    }

    /// <summary>คำตอบหลังปฏิเสธผ่าน LINE: เหตุผล + ผู้กด + แจ้งเจ้าของอู่แล้วหรือยัง (ลูกค้าแจ้งใหม่ได้ทันที)</summary>
    public static LineOutboundMessage Rejected(PackagePaymentEntry entry, PackagePaymentRejectReason reason, string actorDisplay,
        bool customerNotified, SelfServeOptions.PackageOptions options)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(options);
        var icons = options.EffectiveLineIconBaseUrl;
        var shop = SelfServeText.SingleLine(entry.Workspace.Name, 100) is { Length: > 0 } name ? name : "-";
        var code = SelfServeText.WorkspaceCode(entry.Workspace.WorkspaceNo);
        var shortId = PackagePaymentRules.ShortId(entry.Payment.Id);

        var body = new JsonArray
        {
            Text(shop, size: "md", weight: "bold", color: Ink),
            IconLine(icons, Icons.Store, code, "sm"),
            new JsonObject
            {
                ["type"] = "box",
                ["layout"] = "vertical",
                ["backgroundColor"] = DangerSoft,
                ["cornerRadius"] = "12px",
                ["paddingAll"] = "14px",
                ["margin"] = "md",
                ["contents"] = new JsonArray
                {
                    Text("เหตุผล", size: "xs", color: Muted),
                    Text(PackagePaymentRules.RejectReasonTh(reason), size: "lg", weight: "bold", color: Danger)
                }
            },
            Row(icons, Icons.Person, "ผู้ตรวจ", $"{SelfServeText.SingleLine(actorDisplay, 80)} (LINE)", margin: "md"),
            Row(icons, Icons.Info, "แจ้งเจ้าของอู่", customerNotified ? "แจ้งแล้ว ลูกค้าแจ้งใหม่ได้" : "แจ้งไม่สำเร็จ กรุณาแจ้งลูกค้าเอง")
        };

        var bubble = new JsonObject
        {
            ["type"] = "bubble",
            ["size"] = "kilo",
            ["header"] = Header(icons, Icons.Close, "ไม่อนุมัติ", $"รายการ {shortId}", Danger),
            ["body"] = new JsonObject
            {
                ["type"] = "box", ["layout"] = "vertical", ["spacing"] = "sm", ["paddingAll"] = "16px", ["contents"] = body
            }
        };
        return LineOutboundMessage.FromFlex(
            Limit($"ไม่อนุมัติ {shop} ({code}) รายการ {shortId}: {PackagePaymentRules.RejectReasonTh(reason)}", 400), bubble);
    }

    private static string NotifiedText(bool emailSent, bool inAppSaved) => (emailSent, inAppSaved) switch
    {
        (true, true) => "อีเมลและในระบบแล้ว",
        (false, true) => "ในระบบแล้ว (อีเมลไม่สำเร็จ)",
        (true, false) => "อีเมลแล้ว (ในระบบไม่สำเร็จ)",
        _ => "ไม่สำเร็จ กรุณาแจ้งลูกค้าเอง"
    };

    private static JsonObject Footer(string icons, PackagePayment p, string shortId, bool mismatch,
        SelfServeOptions.PackageOptions options, DateTime nowUtc)
    {
        var contents = new JsonArray();
        var key = options.EffectiveLineCommandKey;
        var admin = AdminUrl(options);

        if (key is not null)
        {
            if (!mismatch)
                contents.Add(ApproveButton(icons,
                    PackageLineCommands.Create(key, p.Id, LineCommandAction.Approve, null, nowUtc)));

            contents.Add(Text("ไม่อนุมัติ เพราะ", size: "xs", color: Muted, align: "center", margin: "md"));
            foreach (var (reason, label, icon) in RejectButtons)
                contents.Add(RejectButton(icons, icon, label,
                    PackageLineCommands.Create(key, p.Id, LineCommandAction.Reject, reason, nowUtc)));
        }
        else
        {
            contents.Add(Text($"อนุมัติ/ปฏิเสธผ่าน GaragePro Admin หรือ spadmin approve-payment --id {shortId} --by <ชื่อ>",
                size: "xs", color: Muted));
        }

        if (admin is not null && (key is null || mismatch))
            contents.Add(new JsonObject
            {
                ["type"] = "button", ["style"] = "link", ["height"] = "sm", ["action"] = Uri("เปิดหน้าตรวจใน Admin", admin)
            });

        return new JsonObject
        {
            ["type"] = "box", ["layout"] = "vertical", ["spacing"] = "sm", ["paddingAll"] = "16px",
            ["paddingTop"] = "0px", ["contents"] = contents
        };
    }

    /// <summary>ลิงก์หน้าตรวจของ Admin ที่ใช้ได้ (https เท่านั้น) · null = ไม่ได้ตั้ง</summary>
    public static string? AdminUrl(SelfServeOptions.PackageOptions options) =>
        System.Uri.TryCreate(options.AdminReviewUrl?.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == System.Uri.UriSchemeHttps
            ? uri.AbsoluteUri
            : null;

    /// <summary>หัวการ์ดสีทึบ: ไอคอนขาวในวงกลมโปร่ง + ชื่อเรื่อง + บรรทัดรอง + ป้ายด้านขวา (ถ้ามี)</summary>
    private static JsonObject Header(string icons, string icon, string title, string subtitle, string background,
        JsonObject? pill = null)
    {
        var contents = new JsonArray
        {
            Badge(icons, icon, "#FFFFFF33", "36px", "20px"),
            new JsonObject
            {
                ["type"] = "box",
                ["layout"] = "vertical",
                ["flex"] = 1,
                ["contents"] = new JsonArray
                {
                    Text(title, size: "lg", weight: "bold", color: "#FFFFFF"),
                    Text(subtitle, size: "xs", color: HeaderSub)
                }
            }
        };
        if (pill is not null) contents.Add(pill);
        return new JsonObject
        {
            ["type"] = "box",
            ["layout"] = "horizontal",
            ["alignItems"] = "center",
            ["backgroundColor"] = background,
            ["paddingAll"] = "16px",
            ["spacing"] = "md",
            ["contents"] = contents
        };
    }

    /// <summary>ป้ายสถานะทึบ ไอคอนขาว + ข้อความขาว (สี + ไอคอน + ข้อความ)</summary>
    private static JsonObject Pill(string icons, string icon, string text, string background) => new()
    {
        ["type"] = "box",
        ["layout"] = "horizontal",
        ["flex"] = 0,
        ["alignItems"] = "center",
        ["spacing"] = "xs",
        ["backgroundColor"] = background,
        ["cornerRadius"] = "20px",
        ["paddingStart"] = "8px",
        ["paddingEnd"] = "10px",
        ["paddingTop"] = "4px",
        ["paddingBottom"] = "4px",
        ["contents"] = new JsonArray { Image(icons, icon, "14px"), NoWrap(Text(text, size: "xs", weight: "bold", color: "#FFFFFF")) }
    };

    /// <summary>วงกลมสีที่มีไอคอนขาวตรงกลาง (หัวการ์ด · ปุ่มปฏิเสธ · กล่องเตือน)</summary>
    private static JsonObject Badge(string icons, string icon, string background, string diameter, string iconSize) => new()
    {
        ["type"] = "box",
        ["layout"] = "vertical",
        ["flex"] = 0,
        ["width"] = diameter,
        ["height"] = diameter,
        ["cornerRadius"] = diameter,
        ["backgroundColor"] = background,
        ["justifyContent"] = "center",
        ["alignItems"] = "center",
        ["contents"] = new JsonArray { Image(icons, icon, iconSize) }
    };

    private static JsonObject Image(string icons, string icon, string size) => new()
    {
        ["type"] = "image", ["url"] = icons + icon, ["size"] = size, ["aspectRatio"] = "1:1", ["flex"] = 0
    };

    /// <summary>กล่องสรุป: จำนวนเดือนตัวใหญ่ (แบบการ์ดเดิม) คู่กับยอดโอน · ยอดตรงราคา = เขียว + "ตรงราคา" · ไม่ตรง = แดง + "ไม่ตรงราคา"</summary>
    private static JsonObject Summary(string months, decimal amount, decimal? expected, bool mismatch)
    {
        var amountColor = expected is null ? Ink : mismatch ? Danger : Success;
        var amountNote = expected is null ? "บาท" : mismatch ? "บาท · ไม่ตรงราคา" : "บาท · ตรงราคา";
        var left = new JsonObject
        {
            ["type"] = "box",
            ["layout"] = "vertical",
            ["flex"] = 1,
            ["contents"] = new JsonArray
            {
                Text("จำนวน", size: "xs", color: Muted),
                Text($"{months} เดือน", size: "xxl", weight: "bold", color: Accent)
            }
        };
        var right = new JsonObject
        {
            ["type"] = "box",
            ["layout"] = "vertical",
            ["flex"] = 1,
            ["contents"] = new JsonArray
            {
                Text("ยอดโอน", size: "xs", color: Muted, align: "end"),
                Text(PackagePaymentMessages.Number(amount), size: "xl", weight: "bold", color: amountColor, align: "end"),
                Text(amountNote, size: "xxs", color: amountColor, align: "end", weight: mismatch ? "bold" : null)
            }
        };
        return new JsonObject
        {
            ["type"] = "box",
            ["layout"] = "horizontal",
            ["alignItems"] = "center",
            ["backgroundColor"] = Surface,
            ["cornerRadius"] = "12px",
            ["paddingAll"] = "14px",
            ["margin"] = "md",
            ["contents"] = new JsonArray { left, right }
        };
    }

    /// <summary>กล่องเตือนพื้นแดงอ่อน (ยอดไม่ตรง · สลิปซ้ำ) · ไอคอนเตือน + ข้อความ ไม่สื่อด้วยสีอย่างเดียว</summary>
    private static JsonObject Callout(string icons, string text) => new()
    {
        ["type"] = "box",
        ["layout"] = "horizontal",
        ["alignItems"] = "center",
        ["spacing"] = "md",
        ["backgroundColor"] = DangerSoft,
        ["cornerRadius"] = "8px",
        ["paddingAll"] = "10px",
        ["margin"] = "md",
        ["contents"] = new JsonArray
        {
            Badge(icons, Icons.Warning, Danger, "24px", "14px"),
            Text(text, size: "sm", color: Danger, weight: "bold")
        }
    };

    /// <summary>ปุ่มอนุมัติ (box ที่มี message action · ปุ่ม Flex ปกติใส่ไอคอนไม่ได้)</summary>
    private static JsonObject ApproveButton(string icons, string command) => new()
    {
        ["type"] = "box",
        ["layout"] = "horizontal",
        ["justifyContent"] = "center",
        ["alignItems"] = "center",
        ["spacing"] = "sm",
        ["backgroundColor"] = Success,
        ["cornerRadius"] = "10px",
        ["paddingAll"] = "12px",
        ["action"] = Message(ApproveLabel, command),
        ["contents"] = new JsonArray
        {
            Image(icons, Icons.CheckCircle, "20px"),
            NoWrap(Text(ApproveLabel, size: "md", weight: "bold", color: "#FFFFFF"))
        }
    };

    /// <summary>ปุ่มปฏิเสธ: กรอบแดงบาง · วงกลมแดงไอคอนขาวบอกเหตุผล · ข้อความแดง</summary>
    private static JsonObject RejectButton(string icons, string icon, string label, string command) => new()
    {
        ["type"] = "box",
        ["layout"] = "horizontal",
        ["alignItems"] = "center",
        ["spacing"] = "md",
        ["borderWidth"] = "1px",
        ["borderColor"] = DangerBorder,
        ["cornerRadius"] = "10px",
        ["paddingAll"] = "10px",
        ["action"] = Message(label, command),
        ["contents"] = new JsonArray
        {
            Badge(icons, icon, Danger, "24px", "14px"),
            Text(label, size: "sm", weight: "bold", color: Danger)
        }
    };

    /// <summary>
    /// ข้อความสั้นใน box ที่ flex 0 (ป้าย · ปุ่ม): ต้องไม่ตัดบรรทัดและกว้างตามข้อความ ·
    /// ข้อความที่ตัดบรรทัดได้ใน box flex 0 จะขยายกินทั้งแถว แล้วบีบช่องข้างๆ จนแคบเหลือตัวอักษรละบรรทัด (เจอบน LINE จริง)
    /// </summary>
    private static JsonObject NoWrap(JsonObject node)
    {
        node["wrap"] = false;
        node["flex"] = 0;
        return node;
    }

    private static JsonObject Message(string label, string text) =>
        new() { ["type"] = "message", ["label"] = label, ["text"] = text };

    private static JsonObject Uri(string label, string uri) =>
        new() { ["type"] = "uri", ["label"] = label, ["uri"] = uri };

    /// <summary>ไอคอนดำจางหน้าข้อความบรรทัดเดียว (baseline ให้ไอคอนเสมอแนวตัวอักษร)</summary>
    private static JsonObject IconLine(string icons, string icon, string text, string size, bool bold = false) => new()
    {
        ["type"] = "box",
        ["layout"] = "baseline",
        ["spacing"] = "sm",
        ["contents"] = new JsonArray
        {
            new JsonObject { ["type"] = "icon", ["url"] = icons + icon, ["size"] = size },
            Text(text, size: size, color: Muted, weight: bold ? "bold" : null)
        }
    };

    /// <summary>ไอคอน + ป้ายกว้างตามข้อความ (flex 0) ค่าได้พื้นที่ที่เหลือชิดขวา · ค่ายาวจึงไม่ตกบรรทัดเพราะป้ายกินที่ครึ่งการ์ด</summary>
    private static JsonObject Row(string icons, string icon, string label, string value, string color = Ink, bool bold = false,
        string? margin = null)
    {
        var valueNode = Text(value, size: "sm", color: color, weight: bold ? "bold" : null);
        valueNode["flex"] = 1;
        valueNode["align"] = "end";
        var labelNode = Text(label, size: "sm", color: Muted);
        labelNode["flex"] = 0;
        labelNode["wrap"] = false;
        var row = new JsonObject
        {
            ["type"] = "box",
            ["layout"] = "baseline",
            ["spacing"] = "sm",
            ["contents"] = new JsonArray
            {
                new JsonObject { ["type"] = "icon", ["url"] = icons + icon, ["size"] = "sm" },
                labelNode,
                valueNode
            }
        };
        if (margin is not null) row["margin"] = margin;
        return row;
    }

    private static JsonObject Text(string text, string size, string color, string? weight = null, string? margin = null,
        string? align = null)
    {
        var node = new JsonObject
        {
            ["type"] = "text",
            ["text"] = string.IsNullOrWhiteSpace(text) ? "-" : text,
            ["size"] = size,
            ["color"] = color,
            ["wrap"] = true
        };
        if (weight is not null) node["weight"] = weight;
        if (margin is not null) node["margin"] = margin;
        if (align is not null) node["align"] = align;
        return node;
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..(max - 1)] + "…";
}
