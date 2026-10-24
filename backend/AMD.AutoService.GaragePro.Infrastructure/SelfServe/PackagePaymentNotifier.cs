using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Application.Notifications;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// แจ้งเรื่องการชำระเงิน (docs/17 §5.3–5.4) · ทุกช่องทางเป็น best effort: ผิดพลาด log แล้วคืน false ไม่โยนต่อ
/// (ยกเว้นผู้เรียกยกเลิกเอง) เพราะการแจ้งชำระ/อนุมัติบันทึกแล้วก่อนถึงที่นี่
/// · LINE ทีมงาน: แจ้งใหม่ = การ์ด Flex (รูปสลิปเมื่อเป็น JPEG + https · ปุ่ม /SP_ เมื่อตั้ง LineCommandKey · docs/17 §8.7.2)
///   สร้างการ์ดไม่ได้ = ข้อความ + รูปแบบเดิม · retry key คงที่ต่อรายการ ส่งซ้ำ (เน็ตหลุด/ลองใหม่) ไม่ทำให้กลุ่มได้ข้อความซ้ำ
/// · หลังอนุมัติ/ปฏิเสธ: อีเมลถึงเจ้าของ + แจ้งเตือนในระบบถึง Manager ของอู่ ผ่าน scope ของ shard sp1
///   (svc_Notification อยู่ในฐาน GarageSelfServe คนละ DbContext กับ sp_* จึงทำหลัง commit)
/// · [SECURITY] ไม่ log ข้อความ/อีเมลผู้รับ/ลิงก์สลิป (ลิงก์เปิดดูสลิปได้โดยไม่ล็อกอิน)
/// </summary>
public sealed class PackagePaymentNotifier(
    ISelfServeSalesNotifier sales,
    IEmailSender email,
    IShardScopeFactory scopes,
    IOptions<SelfServeOptions> options,
    TimeProvider clock,
    ILogger<PackagePaymentNotifier> logger) : IPackagePaymentNotifier
{
    /// <summary>ชื่อผู้กระทำที่แสดงบนแจ้งเตือนในระบบ (ไม่ใช่ชื่อทีมงานจริง)</summary>
    public const string NotificationActorName = "ทีมงาน ServicePro";

    private readonly SelfServeOptions _options = options.Value;

    public async Task<bool> SubmittedAsync(PackagePaymentSubmittedNotice notice, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notice);
        if (!sales.IsConfigured) return false;

        var payment = notice.Context.Payment;
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var link = PackageSlipLinks.Create(_options.Package, payment.Id, now);
            var messages = Messages(notice, link, now);

            var sent = await sales.SendAsync(
                SelfServeText.DeterministicGuid($"package-payment:{payment.Id}"), messages, ct);
            if (!sent) logger.LogWarning("แจ้ง LINE ทีมงานเรื่องแจ้งชำระ {ShortId} ไม่สำเร็จ", PackagePaymentRules.ShortId(payment.Id));
            return sent;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "แจ้ง LINE ทีมงานเรื่องแจ้งชำระ {ShortId} ไม่สำเร็จ", PackagePaymentRules.ShortId(payment.Id));
            return false;
        }
    }

    /// <summary>
    /// การ์ด Flex พร้อมปุ่ม <c>/SP_</c> (docs/17 §8.7.2) · สร้างการ์ดไม่ได้ (ผิดข้อจำกัดของ LINE) = ข้อความ + รูปแบบเดิม
    /// ดีกว่าทีมงานไม่ได้รับแจ้งเลย
    /// </summary>
    internal List<LineOutboundMessage> Messages(PackagePaymentSubmittedNotice notice, string? link, DateTime now)
    {
        try
        {
            var card = PackagePaymentFlex.Submitted(notice, link, _options.Package, now);
            card.ToLineJson();   // ตรวจข้อจำกัดของ LINE ก่อนส่งจริง
            return [card];
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(ex, "สร้างการ์ด LINE ของแจ้งชำระ {ShortId} ไม่ได้ ส่งเป็นข้อความแทน",
                PackagePaymentRules.ShortId(notice.Context.Payment.Id));
            var asImage = link is not null && notice.Context.Payment.SlipContentType == "image/jpeg"
                                           && link.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            var messages = new List<LineOutboundMessage>
            {
                LineOutboundMessage.FromText(PackagePaymentMessages.Submitted(notice, asImage ? null : link))
            };
            if (asImage) messages.Add(LineOutboundMessage.FromImage(link!));
            return messages;
        }
    }

    public async Task<bool> CancelledAsync(PackagePaymentNoticeContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!sales.IsConfigured) return false;

        try
        {
            return await sales.SendAsync(SelfServeText.DeterministicGuid($"package-payment-cancel:{context.Payment.Id}"),
                PackagePaymentMessages.Cancelled(context, context.Payment.CancelledBy), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "แจ้ง LINE ทีมงานเรื่องยกเลิกแจ้งชำระ {ShortId} ไม่สำเร็จ",
                PackagePaymentRules.ShortId(context.Payment.Id));
            return false;
        }
    }

    public async Task<PackageReviewNotifyOutcome> ReviewedAsync(PackagePaymentReviewedNotice notice,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(notice);
        var emailSent = await SendReviewEmailAsync(notice, ct);
        var inAppSaved = await SaveInAppAsync(notice, ct);
        return new PackageReviewNotifyOutcome(emailSent, inAppSaved);
    }

    private async Task<bool> SendReviewEmailAsync(PackagePaymentReviewedNotice notice, CancellationToken ct)
    {
        var c = notice.Context;
        if (string.IsNullOrWhiteSpace(c.OwnerEmail)) return false;

        var content = BuildReviewEmail(notice, _options.PublicBaseUrl);
        if (content is null) return false;

        try
        {
            await email.SendAsync(content.To(c.OwnerEmail), ct);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "ส่งอีเมลผลการตรวจชำระ {ShortId} ของอู่ {WorkspaceNo} ไม่สำเร็จ",
                PackagePaymentRules.ShortId(c.Payment.Id), c.WorkspaceNo);
            return false;
        }
    }

    private async Task<bool> SaveInAppAsync(PackagePaymentReviewedNotice notice, CancellationToken ct)
    {
        var c = notice.Context;
        var draft = BuildNotification(notice);
        if (draft is null) return false;

        try
        {
            // ServiceDbContext เลือกฐานจาก ICurrentUser.ShardKey ของ scope นี้ = sp1 → GarageSelfServe
            var system = new ScopedCurrentUser(_options.ShardKey, c.BranchId, 0, NotificationActorName);
            return await scopes.RunAsAsync(system, async (sp, token) =>
            {
                await sp.GetRequiredService<INotificationPublisher>().ToRolesAsync(draft, [UserRole.Manager], token);
                await sp.GetRequiredService<INotificationRepository>().SaveChangesAsync(token);
                return true;
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "บันทึกแจ้งเตือนผลการตรวจชำระ {ShortId} ของอู่ {WorkspaceNo} ไม่สำเร็จ",
                PackagePaymentRules.ShortId(c.Payment.Id), c.WorkspaceNo);
            return false;
        }
    }

    internal static EmailContent? BuildReviewEmail(PackagePaymentReviewedNotice notice, string publicBaseUrl)
    {
        var c = notice.Context;
        var p = c.Payment;
        return p.Status switch
        {
            PackagePaymentStatus.Approved when notice.EndsTo is { } end =>
                SelfServeEmails.PaymentApproved(c.WorkspaceName, c.WorkspaceNo, p.MonthCount, end, publicBaseUrl),
            PackagePaymentStatus.Rejected =>
                SelfServeEmails.PaymentRejected(c.WorkspaceName, c.WorkspaceNo, p.MonthCount,
                    PackagePaymentRules.RejectReasonTh(p.RejectReason ?? PackagePaymentRejectReason.Other), p.ReviewNote,
                    publicBaseUrl),
            _ => null
        };
    }

    /// <summary>ข้อความกระดิ่ง · entityType "PackagePayment" + linkHint "package" → เว็บ/แอปพาไปหน้าแพ็กเกจ</summary>
    internal static NotificationDraft? BuildNotification(PackagePaymentReviewedNotice notice)
    {
        var p = notice.Context.Payment;
        return p.Status switch
        {
            PackagePaymentStatus.Approved when notice.EndsTo is { } end => new NotificationDraft(
                PackageNotificationKinds.PaymentApproved,
                $"อนุมัติการชำระเงินแล้ว ใช้งานได้ถึง {SelfServeText.FormatThaiDate(PackageRenewal.LastUsableDay(end))}",
                $"แพ็กเกจ {p.MonthCount} เดือน · ยอด {PackagePaymentMessages.Baht(p.Amount)}",
                PackageNotificationKinds.EntityType, p.Id, LinkHint: PackageNotificationKinds.LinkHint),
            PackagePaymentStatus.Rejected => new NotificationDraft(
                PackageNotificationKinds.PaymentRejected,
                "การแจ้งชำระยังไม่ผ่านการตรวจ",
                $"เหตุผล: {PackagePaymentRules.RejectReasonTh(p.RejectReason ?? PackagePaymentRejectReason.Other)}" +
                (string.IsNullOrWhiteSpace(p.ReviewNote) ? "" : $" · {SelfServeText.SingleLine(p.ReviewNote, 300)}"),
                PackageNotificationKinds.EntityType, p.Id, LinkHint: PackageNotificationKinds.LinkHint),
            _ => null
        };
    }
}
