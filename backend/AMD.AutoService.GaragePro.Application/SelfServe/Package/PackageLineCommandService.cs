using System.Text.Json.Nodes;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>ข้อความ LINE ที่ตัวส่งต่อส่งมา (ค่าจาก webhook event) · ทุกช่องเป็นข้อความ ตรวจที่ <see cref="PackageLineCommandService"/></summary>
public sealed record LineRelayRequest(string? Text, string? LineUserId, string? DisplayName, string? GroupId);

/// <summary>
/// คำตอบให้ตัวส่งต่อ (สัญญาตายตัว docs/03 §16) · <see cref="Handled"/> false = ไม่ใช่คำสั่งของ ServicePro (ไม่ต้องตอบกลุ่ม · messages ว่าง) ·
/// <see cref="Messages"/> = LINE message objects พร้อมส่งด้วย reply token (ตอนนี้ 1 ข้อความ · ไม่เกิน 5)
/// </summary>
public sealed record LineRelayResponse(bool Handled, IReadOnlyList<JsonObject> Messages)
{
    public static LineRelayResponse NotHandled => new(false, []);
}

/// <summary>
/// คำสั่ง <c>/SP_…</c> จากปุ่มบนการ์ดแจ้งชำระที่ webhook ของระบบเดิมส่งต่อมา (docs/17 §8.7.2)
/// · ลำดับการตรวจ (ก่อนแตะฐาน): เป็นคำสั่งไหม → ลายเซ็น/รูปแบบ → วันหมด → กลุ่ม → ผู้กด · แล้วจึงอ่านรายการ
/// · [SECURITY] groupId ที่ส่งมาต้องเป็นกลุ่มทีมงาน · ไม่มี groupId (แชท 1:1 กับบอท) ใช้ได้เฉพาะเมื่อตั้งรายชื่อผู้อนุมัติ
///   และผู้กดอยู่ในรายชื่อ (ตกลงกับผู้ประสานงาน 2026-10-22) · ตั้งรายชื่อไว้ = ต้องอยู่ในรายชื่อเสมอ
/// · อนุมัติ/ปฏิเสธผ่าน <see cref="PackagePaymentAdminService"/> ตัวเดียวกับ spadmin/Admin (transaction · audit · อีเมล · แจ้งเตือน)
///   ผู้กระทำ <c>line:{displayName}#{lineUserId}</c> · ยอดไม่ตรงราคาอนุมัติใน LINE ไม่ได้ (ต้องมีหมายเหตุ)
/// · ทุกกรณีตอบเป็นข้อความสุภาพ ไม่มี exception หลุด (controller ดักซ้ำอีกชั้น)
/// </summary>
public sealed class PackageLineCommandService(
    SelfServeOptions options,
    PackagePaymentAdminService? admin,
    TimeProvider clock,
    string? salesGroupId)
{
    public const string RejectNote = "ทีมงานกดผ่าน LINE";

    private SelfServeOptions.PackageOptions Package => options.Package;

    public async Task<LineRelayResponse> HandleAsync(LineRelayRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!PackageLineCommands.IsCommand(request.Text)) return LineRelayResponse.NotHandled;

        var now = clock.GetUtcNow().UtcDateTime;
        switch (PackageLineCommands.Verify(request.Text, Package.EffectiveLineCommandKey, now, out var command))
        {
            case LineCommandCheck.NoKey:
                return Reply("ยังไม่ได้เปิดให้อนุมัติผ่าน LINE กรุณาอนุมัติใน GaragePro Admin หรือ spadmin");
            case LineCommandCheck.Invalid:
                return Reply("คำสั่งนี้ไม่ถูกต้อง กรุณากดปุ่มบนการ์ดแจ้งชำระโดยตรง ไม่ต้องพิมพ์หรือแก้ข้อความ");
            case LineCommandCheck.Expired:
                return Reply("ปุ่มนี้หมดอายุแล้ว (เกิน 14 วัน) กรุณาตรวจรายการใน GaragePro Admin หรือ spadmin");
        }

        var groupId = request.GroupId?.Trim();
        var userId = Clean(request.LineUserId, 100);
        var approvers = Package.EffectiveLineApproverUserIds;
        var sales = salesGroupId?.Trim();

        if (!string.IsNullOrEmpty(groupId)
            && (string.IsNullOrEmpty(sales) || !string.Equals(groupId, sales, StringComparison.Ordinal)))
            return Reply("คำสั่งนี้ใช้ได้เฉพาะในกลุ่มทีมงานที่ได้รับการ์ดแจ้งชำระ");
        if (string.IsNullOrEmpty(groupId) && approvers.Count == 0)
            return Reply("กรุณากดปุ่มในกลุ่มทีมงาน (คำสั่งนอกกลุ่มใช้ได้เฉพาะผู้อนุมัติที่ตั้งชื่อไว้)");
        if (userId is null)
            return Reply("ไม่ทราบผู้กด กรุณากดใหม่ในกลุ่มทีมงาน");
        if (approvers.Count > 0 && !approvers.Contains(userId, StringComparer.Ordinal))
            return Reply("บัญชี LINE นี้ไม่มีสิทธิ์อนุมัติการชำระ กรุณาให้ผู้อนุมัติกด หรือทำใน GaragePro Admin");

        if (admin is null)
            return Reply("ระบบแพ็กเกจของ ServicePro ยังไม่เปิดใช้ กรุณาติดต่อผู้ดูแลระบบ");

        var cmd = command!;
        var reference = cmd.PaymentId.ToString("N");
        var (entry, error) = await admin.ResolveAsync(reference, ct);
        if (entry is null)
            return Reply(error?.Code == PackageErrors.NotFoundCode
                ? "ไม่พบรายการแจ้งชำระนี้ (อาจถูกลบไปพร้อมอู่แล้ว)"
                : $"ทำรายการไม่สำเร็จ: {error?.MessageTh}");
        if (entry.Payment.Status != PackagePaymentStatus.Pending) return AlreadyReviewed(entry);

        var display = SelfServeText.SingleLine(request.DisplayName, 80) is { Length: > 0 } name ? name : "ไม่ทราบชื่อ";
        var actor = Actor(display, userId);

        if (cmd.Action == LineCommandAction.Approve)
        {
            if (entry.Payment.ExpectedAmount is { } expected && expected != entry.Payment.Amount)
                return Mismatch(entry, expected);

            var approved = await admin.ApproveAsync(reference, null, null, actor, ct);
            if (approved.Success)
                return new LineRelayResponse(true, [PackagePaymentFlex.Approved(approved.Data!, display, Package).ToLineJson()]);
            return await FailureAsync(approved.Error!, reference, ct, entry);
        }

        var reason = cmd.Reason!.Value;
        var rejected = await admin.RejectAsync(reference, PackagePaymentRules.RejectReasonCode(reason), RejectNote, actor, ct);
        if (rejected.Success)
            return new LineRelayResponse(true,
            [
                PackagePaymentFlex.Rejected(entry, reason, display, rejected.Data!.EmailSent || rejected.Data.InAppSaved, Package)
                    .ToLineJson()
            ]);
        return await FailureAsync(rejected.Error!, reference, ct, entry);
    }

    /// <summary>ข้อความตายตัวเมื่อเกิดเรื่องไม่คาดคิด (สัญญากับทีมระบบเดิม docs/03 §16)</summary>
    public const string UnavailableText = "ขออภัย ระบบ ServicePro ขัดข้อง ลองใหม่อีกครั้ง";

    /// <summary>คำตอบเมื่อระบบขัดข้อง (ฐานล่ม · body เสีย ฯลฯ) · controller ใช้แทน 500</summary>
    public static LineRelayResponse Unavailable() => Reply(UnavailableText);

    /// <summary><c>line:{ชื่อ}#{userId}</c> ตัดความยาวให้พอดีคอลัมน์ผู้กระทำ (200)</summary>
    public static string Actor(string displayName, string lineUserId)
    {
        var actor = $"line:{SelfServeText.SingleLine(displayName, 80)}#{SelfServeText.SingleLine(lineUserId, 100)}";
        return actor.Length <= 200 ? actor : actor[..200];
    }

    private async Task<LineRelayResponse> FailureAsync(ApiError error, string reference, CancellationToken ct,
        PackagePaymentEntry before)
    {
        if (error.Code == PackageErrors.NotPendingCode)
        {
            // อีกคนกดตัดหน้า (หรือ spadmin/Admin ทำพร้อมกัน) → บอกว่าใครทำไปแล้ว
            var (now, _) = await admin!.ResolveAsync(reference, ct);
            return AlreadyReviewed(now ?? before);
        }

        if (error.Code == PackageErrors.ValidationCode && error.Field == "note"
                                                       && before.Payment.ExpectedAmount is { } expected)
            return Mismatch(before, expected);

        return Reply($"ทำรายการไม่สำเร็จ: {SelfServeText.SingleLine(error.MessageTh, 300)}");
    }

    private LineRelayResponse Mismatch(PackagePaymentEntry entry, decimal expected)
    {
        var link = PackagePaymentFlex.AdminUrl(Package);
        return Reply($"รายการ {PackagePaymentRules.ShortId(entry.Payment.Id)} ({Shop(entry)}) ยอดโอน " +
                     $"{PackagePaymentMessages.Baht(entry.Payment.Amount)} ไม่ตรงราคา {PackagePaymentMessages.Baht(expected)} " +
                     "อนุมัติผ่าน LINE ไม่ได้ เพราะต้องใส่หมายเหตุ (เช่น ส่วนลด) กรุณาอนุมัติใน GaragePro Admin หรือ spadmin" +
                     (link is null ? "" : $" {link}"));
    }

    private static LineRelayResponse AlreadyReviewed(PackagePaymentEntry entry)
    {
        var p = entry.Payment;
        var head = $"รายการ {PackagePaymentRules.ShortId(p.Id)} ({Shop(entry)})";
        return p.Status switch
        {
            PackagePaymentStatus.Approved => Reply($"{head} อนุมัติแล้วโดย {Who(p.ReviewedBy)}{When(p.ReviewedAt)}"),
            PackagePaymentStatus.Rejected => Reply($"{head} ปฏิเสธแล้วโดย {Who(p.ReviewedBy)}{When(p.ReviewedAt)}" +
                                                   (p.RejectReason is { } r ? $" ({PackagePaymentRules.RejectReasonTh(r)})" : "")),
            PackagePaymentStatus.Cancelled => Reply($"{head} ถูกยกเลิกแล้ว{When(p.CancelledAt)}"),
            _ => Reply($"{head} ไม่ได้อยู่ระหว่างรอตรวจแล้ว")
        };
    }

    /// <summary>
    /// ชื่อผู้ตรวจที่แสดงในกลุ่ม: ตัด <c>#id</c> ท้าย (userId ของ LINE / id ของ Admin) ไม่ต้องโชว์ ·
    /// ตัดคำนำหน้า <c>line:</c>/<c>admin:</c> แล้วบอกช่องทางในวงเล็บแทน (คนในกลุ่มอ่านชื่อ ไม่ใช่รูปแบบที่ระบบเก็บ)
    /// </summary>
    public static string Who(string? actor)
    {
        if (string.IsNullOrWhiteSpace(actor)) return "ทีมงาน";
        var hash = actor.LastIndexOf('#');
        var name = hash > 0 ? actor[..hash] : actor;
        foreach (var (prefix, channel) in ActorChannels)
            if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length)
                return $"{SelfServeText.SingleLine(name[prefix.Length..], 100)} ({channel})";
        return SelfServeText.SingleLine(name, 100);
    }

    private static readonly (string Prefix, string Channel)[] ActorChannels = [("line:", "LINE"), ("admin:", "Admin")];

    private static string When(DateTime? at) => at is { } t ? $" เมื่อ {SelfServeText.FormatThaiDateTime(t)}" : "";

    private static string Shop(PackagePaymentEntry entry) =>
        $"{SelfServeText.SingleLine(entry.Workspace.Name, 100)} {SelfServeText.WorkspaceCode(entry.Workspace.WorkspaceNo)}";

    private static LineRelayResponse Reply(string text) =>
        new(true, [LineOutboundMessage.FromText(text).ToLineJson()]);

    private static string? Clean(string? value, int max)
    {
        var v = value?.Trim();
        return string.IsNullOrEmpty(v) || v.Length > max ? null : v;
    }
}
