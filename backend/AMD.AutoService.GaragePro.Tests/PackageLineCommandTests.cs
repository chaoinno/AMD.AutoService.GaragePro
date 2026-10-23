using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// คำสั่ง /SP_ ของการ์ดแจ้งชำระ (docs/17 §8.7.2): token (เซ็น · ตรวจ · แก้ไข · หมดอายุ) · การ์ด Flex (ปุ่ม · ความยาว · ไม่มีคีย์ไม่มีปุ่ม)
/// · endpoint ตัวส่งต่อ (คีย์ · body · กลุ่ม · ผู้อนุมัติ · อนุมัติ/ปฏิเสธ/ยอดไม่ตรง/ตรวจแล้ว/หมดอายุ · ไม่ตอบ 500) · ไม่ยิง LINE จริง
/// </summary>
public class PackageLineCommandTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 2, 0, 0, DateTimeKind.Utc);
    private const string Key = "line-command-key-0123456789abcdef-xyz";
    private const string RelayKey = "relay-key-0123456789abcdef-0123456789";
    private const string SalesGroup = "Csalesgroup000000000000000000000000";
    private const string Approver = "U1234567890abcdef1234567890abcdef";
    private static readonly Guid PaymentId = Guid.Parse("3f2a9c1b-0000-4000-8000-000000000001");

    private static DateTime ThaiMidnight(int y, int m, int d) => new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc).AddHours(-7);

    // ---------- token ----------

    [Fact]
    public void Tokens_round_trip_and_stay_under_the_line_text_limit()
    {
        var approve = PackageLineCommands.Create(Key, PaymentId, LineCommandAction.Approve, null, Now);
        var reject = PackageLineCommands.Create(Key, PaymentId, LineCommandAction.Reject,
            PackagePaymentRejectReason.SlipUnreadable, Now);

        approve.Should().StartWith("/SP_v1.");
        approve.Length.Should().BeLessThanOrEqualTo(300);
        reject.Length.Should().BeLessThanOrEqualTo(300);
        approve.Should().NotBe(reject);

        PackageLineCommands.Verify(approve, Key, Now, out var a).Should().Be(LineCommandCheck.Valid);
        a!.PaymentId.Should().Be(PaymentId);
        a.Action.Should().Be(LineCommandAction.Approve);
        a.Reason.Should().BeNull();
        a.ExpiresUnix.Should().Be(new DateTimeOffset(Now.AddDays(14)).ToUnixTimeSeconds());

        PackageLineCommands.Verify("  " + reject + " ", Key, Now, out var r).Should().Be(LineCommandCheck.Valid);
        r!.Action.Should().Be(LineCommandAction.Reject);
        r.Reason.Should().Be(PackagePaymentRejectReason.SlipUnreadable);
    }

    [Fact]
    public void Tampered_wrong_key_expired_and_non_commands_are_rejected()
    {
        var token = PackageLineCommands.Create(Key, PaymentId, LineCommandAction.Approve, null, Now);
        var reject = PackageLineCommands.Create(Key, PaymentId, LineCommandAction.Reject,
            PackagePaymentRejectReason.NotReceived, Now);

        PackageLineCommands.Verify("สวัสดี", Key, Now, out _).Should().Be(LineCommandCheck.NotCommand);
        PackageLineCommands.Verify(null, Key, Now, out _).Should().Be(LineCommandCheck.NotCommand);
        PackageLineCommands.Verify(token, null, Now, out _).Should().Be(LineCommandCheck.NoKey);
        PackageLineCommands.Verify(token, Key + "x", Now, out _).Should().Be(LineCommandCheck.Invalid);
        PackageLineCommands.Verify("/SP_v1.abc", Key, Now, out _).Should().Be(LineCommandCheck.Invalid);
        PackageLineCommands.Verify("/SP_v2" + token[6..], Key, Now, out _).Should().Be(LineCommandCheck.Invalid);

        // สลับ payload ของคำสั่งอนุมัติเข้ากับลายเซ็นของคำสั่งปฏิเสธ = ปลอมการกระทำ
        var approveParts = token["/SP_".Length..].Split('.');
        var rejectParts = reject["/SP_".Length..].Split('.');
        PackageLineCommands.Verify($"/SP_v1.{approveParts[1]}.{rejectParts[2]}", Key, Now, out _)
            .Should().Be(LineCommandCheck.Invalid);

        // แก้ payload (เปลี่ยน id) โดยไม่มีคีย์
        var forged = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(approveParts[1])))
            .Replace(PaymentId.ToString("N"), Guid.NewGuid().ToString("N"));
        PackageLineCommands.Verify($"/SP_v1.{B64(forged)}.{approveParts[2]}", Key, Now, out _)
            .Should().Be(LineCommandCheck.Invalid);

        PackageLineCommands.Verify(token, Key, Now.AddDays(14).AddSeconds(1), out var expired)
            .Should().Be(LineCommandCheck.Expired);
        expired.Should().BeNull();
        PackageLineCommands.Verify(token, Key, Now.AddDays(13), out _).Should().Be(LineCommandCheck.Valid);
    }

    private static string Pad(string b64Url)
    {
        var s = b64Url.Replace('-', '+').Replace('_', '/');
        return s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
    }

    private static string B64(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    // ---------- การ์ด Flex ----------

    private static PackagePaymentSubmittedNotice Notice(decimal amount = 4278.93m, decimal? expected = 4278.93m)
    {
        var payment = new PackagePayment
        {
            Id = PaymentId, Status = PackagePaymentStatus.Pending, MonthCount = 1, Amount = amount, ExpectedAmount = expected,
            PaidAt = Now.AddHours(-1), SlipPath = "sp1/61/package-slip/a/b.jpg", SlipSha256 = "aa", SlipContentType = "image/jpeg",
            SubmittedByUserName = "SP1042.M01", SubmittedAt = Now
        };
        var context = new PackagePaymentNoticeContext(payment, Guid.NewGuid(), 1042, "อู่ช่างเอก\nปลอมบรรทัด", "+66812345678",
            61, "owner@example.com");
        var end = ThaiMidnight(2026, 11, 14);
        return new PackagePaymentSubmittedNotice(context, end, PackageRenewal.Compute(end, Now, 1), [],
            new PackagePrice(1, 3999m, 279.93m, 4278.93m, 0.07m));
    }

    /// <summary>รูปสลิปในหัวข้อ "หลักฐานการโอน" ของการ์ด (null = ไม่มีรูป) · รูปเต็มความกว้างรูปเดียว ไอคอนเป็นรูปเล็ก</summary>
    internal static JsonObject? SlipImage(LineOutboundMessage card) =>
        Nodes(card.FlexContents).FirstOrDefault(n =>
            n["type"]?.GetValue<string>() == "image" && n["size"]?.GetValue<string>() == "full");

    internal static IEnumerable<JsonObject> Nodes(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject o:
                yield return o;
                foreach (var (_, child) in o)
                foreach (var n in Nodes(child)) yield return n;
                break;
            case JsonArray a:
                foreach (var child in a)
                foreach (var n in Nodes(child)) yield return n;
                break;
        }
    }

    private static List<JsonObject> MessageActions(LineOutboundMessage card) =>
        Nodes(card.FlexContents).Where(n => n["type"]?.GetValue<string>() == "message").ToList();

    [Fact]
    public void Card_has_an_approve_and_three_reject_buttons_whose_commands_verify()
    {
        var options = new SelfServeOptions.PackageOptions { LineCommandKey = Key };

        var card = PackagePaymentFlex.Submitted(Notice(), "https://api.example.test/api/v1/public/package-slips/x?exp=1&sig=s",
            options, Now);

        var actions = MessageActions(card);
        actions.Select(a => a["label"]!.GetValue<string>())
            .Should().Equal("อนุมัติ", "สลิปไม่ชัด", "ยอดไม่ตรง", "ไม่พบเงินเข้า");
        var checks = actions.Select(a =>
        {
            var text = a["text"]!.GetValue<string>();
            text.Length.Should().BeLessThanOrEqualTo(300);
            PackageLineCommands.Verify(text, Key, Now, out var command).Should().Be(LineCommandCheck.Valid);
            return (command!.Action, command.Reason);
        }).ToList();
        checks.Should().Equal(
            (LineCommandAction.Approve, (PackagePaymentRejectReason?)null),
            (LineCommandAction.Reject, PackagePaymentRejectReason.SlipUnreadable),
            (LineCommandAction.Reject, PackagePaymentRejectReason.AmountMismatch),
            (LineCommandAction.Reject, PackagePaymentRejectReason.NotReceived));

        SlipImage(card)!["url"]!.GetValue<string>().Should().StartWith("https://api.example.test/");
        var buttons = Nodes(card.FlexContents).Where(n => n["action"]?["type"]?.GetValue<string>() == "message").ToList();
        buttons[0]["backgroundColor"]!.GetValue<string>().Should().Be("#0E7C66", "ปุ่มอนุมัติเด่นปุ่มเดียว");
        buttons.Skip(1).Should().OnlyContain(b => b["borderColor"] != null && b["backgroundColor"] == null,
            "ปุ่มปฏิเสธเป็นกรอบบางแบบ Cancel ของการ์ดเดิม ไม่แย่งสายตาจากปุ่มอนุมัติ");
        // ป้าย/ปุ่ม (box flex 0): ข้อความที่ตัดบรรทัดได้จะขยายกินทั้งแถวแล้วบีบหัวการ์ดจนสูงผิดปกติ
        Nodes(card.FlexContents).Where(n => n["type"]?.GetValue<string>() == "box" && n["flex"]?.GetValue<int>() == 0)
            .SelectMany(b => b["contents"]!.AsArray().OfType<JsonObject>())
            .Where(t => t["type"]?.GetValue<string>() == "text")
            .Should().OnlyContain(t => t["wrap"]!.GetValue<bool>() == false);
        var json0 = card.FlexContents.ToJsonString(Relaxed);
        foreach (var emoji in new[] { "✅", "❌", "⚠", "💳", "✓" })
            json0.Should().NotContain(emoji, "การ์ดใช้ไอคอนแทน emoji");
        card.AltText.Should().NotContainAny("✅", "❌", "⚠", "💳");
        Nodes(card.FlexContents).Where(n => n["type"]?.GetValue<string>() is "icon" || n["url"]?.GetValue<string>()?.Contains("/2x_web/") == true)
            .Should().NotBeEmpty().And.OnlyContain(n => n["url"]!.GetValue<string>().StartsWith(SelfServeOptions.PackageOptions.DefaultLineIconBaseUrl));
        Nodes(card.FlexContents).Where(n => n["type"]?.GetValue<string>() == "text")
            .Should().OnlyContain(t => t["text"]!.GetValue<string>().Length > 0, "LINE ไม่รับ text ว่าง");
        var json = card.FlexContents.ToJsonString(Relaxed);
        json.Should().Contain("อู่ช่างเอก ปลอมบรรทัด", "ชื่ออู่ห้ามขึ้นบรรทัดใหม่").And.Contain("ใช้ได้ถึง");
        card.AltText!.Length.Should().BeLessThanOrEqualTo(LineOutboundMessage.MaxAltTextLength);

        var line = card.ToLineJson();
        line["type"]!.GetValue<string>().Should().Be("flex");
        line["altText"]!.GetValue<string>().Should().Be(card.AltText);
    }

    [Fact]
    public void Every_card_icon_is_shipped_with_the_web_so_line_can_fetch_it_from_our_domain()
    {
        // LINE ดึงไอคอนจาก https://service.garage-pro.net/line-icons/… · ลืมใส่ไฟล์ = ไอคอนว่างบนการ์ดโดยไม่มีอะไรฟ้อง
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AMD.AutoService.GaragePro.sln"))) dir = dir.Parent;
        if (dir is null) return;   // รันนอก repo (ไม่มีโฟลเดอร์ web ให้ตรวจ)

        var root = Path.Combine(dir.FullName, "web", "public", "line-icons");
        PackagePaymentFlex.Icons.All.Should().OnlyContain(icon => File.Exists(Path.Combine(root, icon)));
        File.Exists(Path.Combine(root, "LICENSE.txt")).Should().BeTrue("Apache-2.0 ต้องแนบสัญญาอนุญาตไปกับไฟล์");
    }

    [Fact]
    public void Without_a_command_key_the_card_has_no_buttons_and_says_where_to_approve()
    {
        var card = PackagePaymentFlex.Submitted(Notice(), null, new SelfServeOptions.PackageOptions(), Now);

        MessageActions(card).Should().BeEmpty();
        SlipImage(card).Should().BeNull();
        card.FlexContents.ToJsonString(Relaxed).Should().Contain("GaragePro Admin").And.Contain("spadmin approve-payment --id 3f2a9c1b");
    }

    [Fact]
    public void Amount_mismatch_card_has_no_approve_button_but_links_admin_review()
    {
        var options = new SelfServeOptions.PackageOptions
        {
            LineCommandKey = Key, AdminReviewUrl = "https://admin.example.test/branches?tab=servicepro"
        };

        var card = PackagePaymentFlex.Submitted(Notice(amount: 3999m), null, options, Now);

        MessageActions(card).Select(a => a["label"]!.GetValue<string>()).Should().NotContain("อนุมัติ")
            .And.Contain("ยอดไม่ตรง");
        var json = card.FlexContents!.ToJsonString(Relaxed);
        json.Should().Contain("ยอดโอนไม่ตรงราคา").And.Contain("https://admin.example.test/branches?tab=servicepro");
        card.AltText.Should().Contain("ไม่ตรงราคา");
    }

    [Fact]
    public void Line_message_objects_are_validated_before_sending()
    {
        var bad = () => LineOutboundMessage.FromFlex("", new JsonObject { ["type"] = "bubble" }).ToLineJson();
        bad.Should().Throw<ArgumentException>();
        var wrongType = () => LineOutboundMessage.FromFlex("alt", new JsonObject { ["type"] = "box" }).ToLineJson();
        wrongType.Should().Throw<ArgumentException>();
        var huge = new JsonObject { ["type"] = "bubble", ["x"] = new string('a', 40_000) };
        var tooBig = () => LineOutboundMessage.FromFlex("alt", huge).ToLineJson();
        tooBig.Should().Throw<ArgumentException>();
        LineOutboundMessage.Join([LineOutboundMessage.FromFlex("การ์ด", new JsonObject { ["type"] = "bubble" })])
            .Should().Be("การ์ด", "ปลายทางที่ส่งได้แค่ข้อความได้ altText");
    }

    private static readonly JsonSerializerOptions Relaxed = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // ---------- ตัวส่งต่อ: service ----------

    private sealed class Harness
    {
        public InMemoryPackagePayments Payments { get; } = new();
        public FakePackageAdminRepository Admin { get; }
        public FakePackageNotifier Notifier { get; } = new();
        public SelfServeOptions Options { get; } = new();
        public PackageTestClock Clock { get; } = new(Now);
        public Workspace Workspace { get; }
        public PackagePayment Payment { get; }

        public Harness(decimal amount = 4278.93m, decimal? expected = 4278.93m)
        {
            Options.Package.LineCommandKey = Key;
            Options.Package.LineRelayKey = RelayKey;
            Admin = new FakePackageAdminRepository(Payments);
            Workspace = new Workspace
            {
                WorkspaceNo = 1042, BranchId = 61, Name = "อู่ช่างเอก", Phone = "+66812345678", Plan = WorkspacePlanKind.Trial,
                Status = WorkspaceStatus.Active, TrialEndsAt = ThaiMidnight(2026, 11, 14), CreatedAt = Now.AddDays(-20)
            };
            Admin.Snapshots[1042] = new AdminWorkspaceSnapshot(Workspace,
                new AdminAccountInfo(Guid.NewGuid(), "owner@example.com", "owner@example.com",
                    SelfServeAccountProvider.Email, SelfServeAccountStatus.Active, "SP1042.M01", 501, Now, Now),
                new BranchPackage(61, 1, Now.AddDays(-20), 30, ThaiMidnight(2026, 11, 14)), []);
            Payments.Workspaces[Workspace.Id] = new PackagePaymentWorkspace(Workspace.Id, 1042, Workspace.Name, 61,
                WorkspacePlanKind.Trial, WorkspaceStatus.Active);
            Payment = Payments.Seed(Workspace.Id, amount: amount, expected: expected, months: 1, id: PaymentId);
        }

        public PackagePaymentAdminService AdminService => new(Payments, Admin, Notifier, Options, Clock);

        public PackageLineCommandService Service(bool withAdmin = true) =>
            new(Options, withAdmin ? AdminService : null, Clock, SalesGroup);

        public string Approve() => PackageLineCommands.Create(Key, PaymentId, LineCommandAction.Approve, null, Now);

        public string Reject(PackagePaymentRejectReason reason = PackagePaymentRejectReason.NotReceived) =>
            PackageLineCommands.Create(Key, PaymentId, LineCommandAction.Reject, reason, Now);
    }

    private static LineRelayRequest Request(string text, string? group = SalesGroup, string? user = Approver,
        string? name = "น้อย ฝ่ายขาย") => new(text, user, name, group);

    private static string ReplyText(LineRelayResponse response)
    {
        response.Handled.Should().BeTrue();
        var message = response.Messages.Should().ContainSingle().Which;
        return message["type"]!.GetValue<string>() == "text"
            ? message["text"]!.GetValue<string>()
            : message["altText"]!.GetValue<string>();
    }

    [Fact]
    public async Task Approve_from_the_sales_group_extends_the_package_and_replies_with_a_card()
    {
        var h = new Harness();

        var response = await h.Service().HandleAsync(Request(h.Approve()));

        response.Messages!.Single()["type"]!.GetValue<string>().Should().Be("flex");
        ReplyText(response).Should().Contain("อนุมัติแล้ว").And.Contain("ใช้งานได้ถึง 13/12/2569");
        var (change, approval) = h.Admin.Approvals.Single();
        change.Actor.Should().Be($"line:น้อย ฝ่ายขาย#{Approver}");
        approval.ReviewedBy.Should().Be($"line:น้อย ฝ่ายขาย#{Approver}");
        h.Payments.Rows.Single().Status.Should().Be(PackagePaymentStatus.Approved);
    }

    [Fact]
    public async Task Reject_uses_the_button_reason_and_the_fixed_note()
    {
        var h = new Harness();

        var response = await h.Service().HandleAsync(Request(h.Reject(PackagePaymentRejectReason.SlipUnreadable)));

        var card = response.Messages!.Single();
        card["type"]!.GetValue<string>().Should().Be("flex");
        ReplyText(response).Should().Contain("ไม่อนุมัติ").And.Contain("สลิปไม่ชัด อ่านไม่ออก");
        card.ToJsonString(Relaxed).Should().Contain("น้อย ฝ่ายขาย (LINE)").And.Contain("แจ้งแล้ว").And.NotContain(Approver);
        var row = h.Payments.Rows.Single();
        row.Status.Should().Be(PackagePaymentStatus.Rejected);
        row.RejectReason.Should().Be(PackagePaymentRejectReason.SlipUnreadable);
        row.ReviewNote.Should().Be("ทีมงานกดผ่าน LINE");
        row.ReviewedBy.Should().StartWith("line:น้อย ฝ่ายขาย#");
    }

    [Fact]
    public async Task Amount_mismatch_cannot_be_approved_in_line()
    {
        var h = new Harness(amount: 3999m);
        h.Options.Package.AdminReviewUrl = "https://admin.example.test/review";

        var reply = ReplyText(await h.Service().HandleAsync(Request(h.Approve())));

        reply.Should().Contain("ไม่ตรงราคา").And.Contain("ต้องใส่หมายเหตุ").And.Contain("https://admin.example.test/review");
        h.Admin.Approvals.Should().BeEmpty();
        h.Payments.Rows.Single().Status.Should().Be(PackagePaymentStatus.Pending);
    }

    [Fact]
    public async Task Pressing_again_tells_who_already_reviewed_it()
    {
        var h = new Harness();
        await h.Service().HandleAsync(Request(h.Approve()));

        var again = ReplyText(await h.Service().HandleAsync(Request(h.Reject(), name: "อีกคน")));

        again.Should().Contain("อนุมัติแล้วโดย น้อย ฝ่ายขาย (LINE)").And.NotContain(Approver, "ไม่โชว์ LINE userId ในกลุ่ม")
            .And.NotContain("line:", "คนในกลุ่มอ่านชื่อ ไม่ใช่รูปแบบที่ระบบเก็บ");
        h.Payments.Rows.Single().Status.Should().Be(PackagePaymentStatus.Approved);

        var cancelled = new Harness();
        cancelled.Payments.Rows.Single().Status = PackagePaymentStatus.Cancelled;
        ReplyText(await cancelled.Service().HandleAsync(Request(cancelled.Approve()))).Should().Contain("ถูกยกเลิกแล้ว");
    }

    [Fact]
    public async Task Invalid_or_expired_tokens_are_answered_politely_without_touching_the_database()
    {
        var h = new Harness();
        var token = h.Approve();
        var reads = h.Payments.Reads;

        ReplyText(await h.Service().HandleAsync(Request(token[..^2] + "xx"))).Should().Contain("คำสั่งนี้ไม่ถูกต้อง");
        h.Clock.Now = Now.AddDays(15);
        ReplyText(await h.Service().HandleAsync(Request(token))).Should().Contain("หมดอายุ");
        h.Payments.Reads.Should().Be(reads);

        h.Clock.Now = Now;
        h.Options.Package.LineCommandKey = "";
        ReplyText(await h.Service().HandleAsync(Request(token))).Should().Contain("ยังไม่ได้เปิดให้อนุมัติผ่าน LINE");
        h.Admin.Approvals.Should().BeEmpty();
    }

    [Fact]
    public async Task Group_and_approver_rules()
    {
        var h = new Harness();

        ReplyText(await h.Service().HandleAsync(Request(h.Approve(), group: "Cothergroup")))
            .Should().Contain("เฉพาะในกลุ่มทีมงาน");
        ReplyText(await h.Service().HandleAsync(Request(h.Approve(), group: null)))
            .Should().Contain("กรุณากดปุ่มในกลุ่มทีมงาน", "ไม่มี groupId และไม่ได้ตั้งรายชื่อผู้อนุมัติ = ปฏิเสธ");
        ReplyText(await h.Service().HandleAsync(Request(h.Approve(), user: null))).Should().Contain("ไม่ทราบผู้กด");
        h.Admin.Approvals.Should().BeEmpty();

        h.Options.Package.LineApproverUserIds = [Approver];
        ReplyText(await h.Service().HandleAsync(Request(h.Approve(), user: "Uother"))).Should().Contain("ไม่มีสิทธิ์");
        h.Admin.Approvals.Should().BeEmpty();

        ReplyText(await h.Service().HandleAsync(Request(h.Approve(), group: null)))
            .Should().Contain("อนุมัติแล้ว", "ผู้อนุมัติในรายชื่อใช้นอกกลุ่มได้");
    }

    [Fact]
    public async Task Non_commands_are_not_handled_and_missing_self_serve_is_explained()
    {
        var h = new Harness();
        var notCommand = await h.Service().HandleAsync(Request("สวัสดีครับ"));
        notCommand.Handled.Should().BeFalse();
        notCommand.Messages.Should().BeEmpty();
        ReplyText(await h.Service(withAdmin: false).HandleAsync(Request(h.Approve()))).Should().Contain("ยังไม่เปิดใช้");
    }

    // ---------- ตัวส่งต่อ: controller ----------

    private static PublicLineCommandController Controller(Harness h, HttpContext context, PackageLineCommandService? service = null) =>
        new(new MapServiceProvider().Add(service ?? h.Service()),
            Microsoft.Extensions.Options.Options.Create(h.Options), NullLogger<PublicLineCommandController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

    private static DefaultHttpContext Http(string body, params string[] keys)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (keys.Length > 0) context.Request.Headers[PublicLineCommandController.KeyHeader] = keys;
        return context;
    }

    private static string Body(string text, string? group = SalesGroup) => JsonSerializer.Serialize(new
    {
        text, lineUserId = Approver, displayName = "น้อย", groupId = group
    });

    private static int Status(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => throw new InvalidOperationException(result.GetType().Name)
    };

    [Fact]
    public async Task Relay_endpoint_checks_the_key_before_anything_else()
    {
        var h = new Harness();
        var body = Body(h.Approve());

        var noKeyConfigured = new Harness();
        noKeyConfigured.Options.Package.LineRelayKey = "too-short";
        (await Controller(noKeyConfigured, Http(body, RelayKey)).Post(CancellationToken.None))
            .Should().BeOfType<NotFoundResult>();

        (await Controller(h, Http(body)).Post(CancellationToken.None)).Should().BeOfType<UnauthorizedResult>();
        (await Controller(h, Http(body, "wrong")).Post(CancellationToken.None)).Should().BeOfType<UnauthorizedResult>();
        (await Controller(h, Http(body, RelayKey, RelayKey)).Post(CancellationToken.None))
            .Should().BeOfType<UnauthorizedResult>();
        h.Admin.Approvals.Should().BeEmpty();

        var ok = await Controller(h, Http(body, RelayKey)).Post(CancellationToken.None);
        Status(ok).Should().Be(200);
        var response = (LineRelayResponse)((ObjectResult)ok).Value!;
        response.Handled.Should().BeTrue();
        h.Admin.Approvals.Should().ContainSingle();
    }

    [Fact]
    public async Task Relay_endpoint_reads_the_body_itself_and_never_returns_500()
    {
        var h = new Harness();

        foreach (var bad in new[] { "not json", "[1,2]" })
        {
            var broken = await Controller(h, Http(bad, RelayKey)).Post(CancellationToken.None);
            Status(broken).Should().Be(200, "สัญญา: ผ่านคีย์แล้วตอบ 200 เสมอ");
            ReplyText((LineRelayResponse)((ObjectResult)broken).Value!).Should().Be("ขออภัย ระบบ ServicePro ขัดข้อง ลองใหม่อีกครั้ง");
        }

        var other = await Controller(h, Http(Body("ขอบคุณครับ"), RelayKey)).Post(CancellationToken.None);
        var otherResponse = (LineRelayResponse)((ObjectResult)other).Value!;
        otherResponse.Handled.Should().BeFalse();
        otherResponse.Messages.Should().BeEmpty();

        var numbers = await Controller(h, Http("{\"text\":123,\"groupId\":true}", RelayKey)).Post(CancellationToken.None);
        ((LineRelayResponse)((ObjectResult)numbers).Value!).Handled.Should().BeFalse("ช่องที่ไม่ใช่ข้อความถือว่าไม่ได้ส่ง");

        // ฐานล่มระหว่างอนุมัติ → 200 พร้อมข้อความบอกกลุ่ม ไม่ใช่ 500
        var failing = new PackageLineCommandService(h.Options, new PackagePaymentAdminService(new ThrowingPayments(),
            h.Admin, h.Notifier, h.Options, h.Clock), h.Clock, SalesGroup);
        var down = await Controller(h, Http(Body(h.Approve()), RelayKey), failing).Post(CancellationToken.None);
        Status(down).Should().Be(200);
        ReplyText((LineRelayResponse)((ObjectResult)down).Value!).Should().Be("ขออภัย ระบบ ServicePro ขัดข้อง ลองใหม่อีกครั้ง");
    }

    [Fact]
    public void Relay_response_serializes_to_the_fixed_shape()
    {
        // ตัวเลือก JSON เดียวกับ Program.cs (camelCase · ตัด null)
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        JsonSerializer.Serialize(PackageLineCommandService.Unavailable(), json).Should()
            .Be("{\"handled\":true,\"messages\":[{\"type\":\"text\",\"text\":\"ขออภัย ระบบ ServicePro ขัดข้อง ลองใหม่อีกครั้ง\"}]}");
        JsonSerializer.Serialize(LineRelayResponse.NotHandled, json).Should().Be("{\"handled\":false,\"messages\":[]}");
    }

    [Fact]
    public void Relay_body_field_names_are_case_insensitive()
    {
        using var doc = JsonDocument.Parse("{\"Text\":\"/SP_x\",\"LINEUSERID\":\"U1\",\"displayName\":\"A\",\"GroupId\":\"C1\"}");
        PublicLineCommandController.Parse(doc.RootElement).Should().Be(new LineRelayRequest("/SP_x", "U1", "A", "C1"));
    }

    [Fact]
    public void Relay_path_has_its_own_rate_limit_bucket()
    {
        API.RateLimiting.RateLimitRules.Classify("POST", "/api/v1/public/line-commands")
            .Should().Be(API.RateLimiting.RateLimitedRoute.PublicLineCommand);
        API.RateLimiting.RateLimitRules.Classify("POST", "/API/V1/Public/Line-Commands/")
            .Should().Be(API.RateLimiting.RateLimitedRoute.PublicLineCommand);
    }

    private sealed class ThrowingPayments : IPackagePaymentRepository
    {
        private static Exception Down() => new InvalidOperationException("db down");
        public Task<PackagePayment?> GetAsync(Guid id, CancellationToken ct = default) => throw Down();
        public Task<PackagePaymentEntry?> GetEntryAsync(Guid id, CancellationToken ct = default) => throw Down();
        public Task<PackagePayment?> FindByRequestAsync(Guid workspaceId, string requestId, CancellationToken ct = default) => throw Down();
        public Task<PackagePayment?> FindPendingAsync(Guid workspaceId, CancellationToken ct = default) => throw Down();
        public Task<IReadOnlyList<PackagePayment>> ListForWorkspaceAsync(Guid workspaceId, int take, CancellationToken ct = default) => throw Down();
        public Task<PackagePaymentInsertResult> TryInsertAsync(PackagePayment payment, PackagePaymentAuditInfo audit, CancellationToken ct = default) => throw Down();
        public Task<bool> TryCancelAsync(Guid id, Guid workspaceId, DateTime now, PackagePaymentAuditInfo audit, CancellationToken ct = default) => throw Down();
        public Task<bool> TryRejectAsync(Guid id, Guid workspaceId, PackagePaymentRejectReason reason, string? reviewNote, DateTime now,
            PackagePaymentAuditInfo audit, CancellationToken ct = default) => throw Down();
        public Task<IReadOnlyList<PackagePaymentEntry>> ListAsync(PackagePaymentStatus? status, int? workspaceNo, int take,
            CancellationToken ct = default) => throw Down();
        public Task<IReadOnlyList<PackagePaymentEntry>> FindByReferenceAsync(string reference, int max, CancellationToken ct = default) => throw Down();
        public Task<IReadOnlyList<PackagePaymentEntry>> FindBySlipHashesAsync(IReadOnlyCollection<string> sha256s,
            CancellationToken ct = default) => throw Down();
        public Task<IReadOnlyDictionary<Guid, PackagePaymentSummary>> SummarizeByWorkspaceAsync(CancellationToken ct = default) =>
            throw Down();
    }
}
