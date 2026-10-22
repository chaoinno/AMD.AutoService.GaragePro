using System.Globalization;
using System.Text;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>
/// ข้อความ LINE ของทีมงาน (docs/17 §5.3) · ฟังก์ชันล้วน
/// · [SECURITY] ค่าที่ลูกค้าพิมพ์เอง (ชื่ออู่ · หมายเหตุ) ผ่าน <see cref="SelfServeText.SingleLine"/> เสมอ กันปลอมบรรทัดคำสั่งอนุมัติ
///   ซ้อนเข้ามาในข้อความ · ขึ้นบรรทัดใหม่มีเฉพาะที่ระบบใส่เอง
/// · คำสั่ง spadmin ในข้อความใช้ shortId (8 ตัว) ให้ทีมงานคัดลอกไปวางได้ทันที
/// </summary>
public static class PackagePaymentMessages
{
    /// <param name="slipLinkInText">ลิงก์สลิปที่ต้องพิมพ์ในข้อความ (PDF หรือรูปที่ส่งเป็นรูปไม่ได้) · null = ไม่พิมพ์</param>
    public static string Submitted(PackagePaymentSubmittedNotice notice, string? slipLinkInText)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var c = notice.Context;
        var p = c.Payment;
        var shortId = PackagePaymentRules.ShortId(p.Id);
        var sb = new StringBuilder();

        sb.Append("💳 แจ้งชำระแพ็กเกจ: อู่ ").Append(SelfServeText.SingleLine(c.WorkspaceName, 100))
          .Append(" (").Append(SelfServeText.WorkspaceCode(c.WorkspaceNo)).Append(") · โทร ")
          .Append(SelfServeText.DisplayPhone(c.WorkspacePhone)).Append('\n');

        sb.Append("จำนวน ").Append(p.MonthCount.ToString(CultureInfo.InvariantCulture)).Append(" เดือน · ยอดโอน ")
          .Append(Baht(p.Amount));
        if (p.ExpectedAmount is { } expected)
            sb.Append(p.Amount == expected ? " (ตรงราคา)" : $" (ราคามาตรฐาน {Baht(expected)} · ⚠ ไม่ตรงราคา)");
        else
            sb.Append(" (ยังไม่ตั้งราคามาตรฐาน)");
        sb.Append('\n');

        // ราคามาตรฐาน = ยอดรวม VAT · แยกให้ทีมงานเห็นที่มาของตัวเลข (ลูกค้าบางรายโอนเฉพาะราคาก่อน VAT)
        if (notice.Price is { VatRate: { } rate } price && p.ExpectedAmount == price.Total)
            sb.Append("ราคามาตรฐานรวม VAT ").Append(PackagePricing.Percent(rate)).Append(": ")
              .Append(Number(price.Subtotal)).Append(" + VAT ").Append(Number(price.Vat)).Append(" = ")
              .Append(Baht(price.Total)).Append('\n');

        sb.Append("เวลาโอน ").Append(SelfServeText.FormatThaiDateTime(p.PaidAt)).Append('\n');
        sb.Append("ตอนนี้ใช้ได้ถึง ")
          .Append(notice.CurrentEndsAt is { } end
              ? SelfServeText.FormatThaiDate(PackageRenewal.LastUsableDay(end))
              : "ไม่มีกำหนด")
          .Append(" · ถ้าอนุมัติวันนี้ ใช้ได้ถึง ")
          .Append(SelfServeText.FormatThaiDate(notice.PreviewIfApprovedNow.LastUsableDate)).Append('\n');

        if (!string.IsNullOrWhiteSpace(p.Note))
            sb.Append("หมายเหตุ: ").Append(SelfServeText.SingleLine(p.Note, PackagePaymentRules.MaxNoteLength)).Append('\n');

        foreach (var d in notice.Duplicates.Take(3))
            sb.Append("⚠ สลิปซ้ำกับรายการ ").Append(PackagePaymentRules.ShortId(d.PaymentId)).Append(" (")
              .Append(d.WorkspaceNo).Append(" · ").Append(d.Status).Append(')').Append('\n');

        if (slipLinkInText is not null)
            sb.Append(p.SlipContentType == "application/pdf" ? "สลิป PDF: " : "สลิป: ").Append(slipLinkInText).Append('\n');

        sb.Append("อนุมัติ: spadmin approve-payment --id ").Append(shortId).Append(" --by <ชื่อ>\n");
        sb.Append("ปฏิเสธ: spadmin reject-payment --id ").Append(shortId)
          .Append(" --by <ชื่อ> --reason ").Append(string.Join('|', PackagePaymentRules.RejectReasonCodes));
        return sb.ToString();
    }

    public static string Cancelled(PackagePaymentNoticeContext context, string? actor)
    {
        ArgumentNullException.ThrowIfNull(context);
        return $"❎ ยกเลิกแจ้งชำระ: อู่ {SelfServeText.SingleLine(context.WorkspaceName, 100)} " +
               $"({SelfServeText.WorkspaceCode(context.WorkspaceNo)}) · รายการ {PackagePaymentRules.ShortId(context.Payment.Id)} · " +
               $"{context.Payment.MonthCount.ToString(CultureInfo.InvariantCulture)} เดือน {Baht(context.Payment.Amount)}" +
               (string.IsNullOrWhiteSpace(actor) ? "" : $" · โดย {SelfServeText.SingleLine(actor, 100)}");
    }

    /// <summary>1500 → "1,500.00 บาท" (ไม่พึ่ง culture ของเครื่อง)</summary>
    public static string Baht(decimal amount) => Number(amount) + " บาท";

    /// <summary>1500 → "1,500.00"</summary>
    public static string Number(decimal amount) => amount.ToString("N2", CultureInfo.InvariantCulture);
}
