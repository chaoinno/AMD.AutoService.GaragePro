using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Application.Notifications;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ข้อความถึงทีมงาน/เจ้าของอู่ (docs/17 §5.3–5.4): LINE ข้อความ + รูปใน push เดียว · PDF/ไม่มีคีย์ = ข้อความอย่างเดียว ·
/// retry key คงที่ · ผิดพลาดไม่โยนต่อ · แจ้งเตือนในระบบถึง Manager ผ่าน scope ของ sp1
/// </summary>
public class PackagePaymentNotifierTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 7, 41, 0, DateTimeKind.Utc);

    private sealed class FakeSales : ISelfServeSalesNotifier
    {
        public List<(Guid Key, IReadOnlyList<LineOutboundMessage> Messages)> Sent { get; } = [];
        public bool IsConfigured { get; set; } = true;
        public bool Throws { get; set; }

        public Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct = default) =>
            SendAsync(retryKey, [LineOutboundMessage.FromText(text)], ct);

        public Task<bool> SendAsync(Guid retryKey, IReadOnlyList<LineOutboundMessage> messages, CancellationToken ct = default)
        {
            if (Throws) throw new HttpRequestException("line down");
            Sent.Add((retryKey, messages));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeEmail : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];
        public bool Throws { get; set; }
        public bool IsConfigured => true;

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("smtp down");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotifications : INotificationRepository
    {
        public int Saves { get; private set; }
        public void Add(Notification notification) { }
        public Task<IReadOnlyList<Notification>> GetOpenBySubjectAsync(string shardKey, int branchId, string subjectKey,
            CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Notification>>([]);
        public Task<IReadOnlyList<NotificationRow>> PageAsync(NotificationViewer viewer, DateTime since, bool unreadOnly,
            DateTime? beforeAt, Guid? beforeId, int take, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> CountUnreadAsync(NotificationViewer viewer, DateTime since, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetUnreadIdsAsync(NotificationViewer viewer, DateTime since,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Notification?> GetVisibleAsync(NotificationViewer viewer, DateTime since, Guid id,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<NotificationRead?> GetReadAsync(Guid notificationId, long staffId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public void AddRead(NotificationRead read) { }
        public void RemoveRead(NotificationRead read) { }

        public Task SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeScopes(IServiceProvider provider) : IShardScopeFactory
    {
        public List<ICurrentUser> Users { get; } = [];
        public bool Throws { get; set; }

        public Task<T> RunAsAsync<T>(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task<T>> work,
            CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("db down");
            Users.Add(user);
            return work(provider, ct);
        }

        public Task RunAsAsync(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task> work,
            CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class Harness
    {
        public FakeSales Sales { get; } = new();
        public FakeEmail Email { get; } = new();
        public FakeNotificationPublisher Publisher { get; } = new();
        public FakeNotifications Notifications { get; } = new();
        public FakeScopes Scopes { get; }
        public SelfServeOptions Options { get; } = new();

        public Harness()
        {
            Scopes = new FakeScopes(new MapServiceProvider()
                .Add<INotificationPublisher>(Publisher)
                .Add<INotificationRepository>(Notifications));
            Options.Package.SlipLinkKey = "0123456789abcdef0123456789abcdef-test-key";
        }

        public PackagePaymentNotifier Notifier => new(Sales, Email, Scopes, Microsoft.Extensions.Options.Options.Create(Options),
            new PackageTestClock(Now), NullLogger<PackagePaymentNotifier>.Instance);
    }

    private static PackagePaymentNoticeContext Context(PackagePaymentStatus status = PackagePaymentStatus.Pending,
        string contentType = "image/jpeg") => new(new PackagePayment
        {
            Id = Guid.Parse("3f2a9c1b-0000-4000-8000-000000000001"), Status = status, MonthCount = 3, Amount = 1500,
            ExpectedAmount = 1770, PaidAt = Now.AddMinutes(-10), SlipPath = "sp1/61/package-slip/a/b.jpg", SlipSha256 = "aa",
            SlipContentType = contentType, Note = "โอน\nจากบริษัท", SubmittedByUserName = "SP1042.M01", SubmittedAt = Now,
            RejectReason = status == PackagePaymentStatus.Rejected ? PackagePaymentRejectReason.Duplicate : null,
            ReviewNote = status == PackagePaymentStatus.Rejected ? "สลิปเดียวกับเดือนที่แล้ว" : null
        }, Guid.NewGuid(), 1042, "อู่ช่างเอก", "+66812345678", 61, "owner@example.com");

    private static PackagePaymentSubmittedNotice Submitted(PackagePaymentNoticeContext context,
        IReadOnlyList<PackagePaymentDuplicateDto>? duplicates = null) =>
        new(context, new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc),
            PackageRenewal.Compute(new DateTime(2026, 11, 13, 3, 0, 0, DateTimeKind.Utc), Now, 3), duplicates ?? []);

    [Fact]
    public async Task New_payment_goes_to_line_as_text_plus_slip_image_with_a_stable_retry_key()
    {
        var h = new Harness();
        var context = Context();
        var dup = new PackagePaymentDuplicateDto(Guid.Parse("aaaaaaaa-0000-4000-8000-000000000009"), "SP1041", "approved", Now);

        (await h.Notifier.SubmittedAsync(Submitted(context, [dup]))).Should().BeTrue();

        var (key, messages) = h.Sales.Sent.Single();
        key.Should().Be(SelfServeText.DeterministicGuid($"package-payment:{context.Payment.Id}"));
        messages.Should().HaveCount(2);
        var text = messages[0].Text!;
        text.Should().Contain("อู่ช่างเอก (SP1042)").And.Contain("3 เดือน").And.Contain("1,500.00 บาท")
            .And.Contain("ราคามาตรฐาน 1,770.00 บาท").And.Contain("ไม่ตรงราคา")
            .And.Contain("ถ้าอนุมัติวันนี้ ใช้ได้ถึง 13/02/2570")
            .And.Contain("⚠ สลิปซ้ำกับรายการ aaaaaaaa (SP1041 · approved)")
            .And.Contain("spadmin approve-payment --id 3f2a9c1b --by <ชื่อ>")
            .And.Contain("หมายเหตุ: โอน จากบริษัท", "ข้อความของลูกค้าห้ามขึ้นบรรทัดใหม่");
        text.Should().NotContain("/package-slips/", "รูปส่งเป็นข้อความรูปแยก");
        messages[1].ImageUrl.Should().StartWith("https://gpservice-api.garage-pro.net/api/v1/public/package-slips/");
    }

    [Fact]
    public async Task Pdf_slip_or_missing_key_sends_text_only()
    {
        var pdf = new Harness();
        await pdf.Notifier.SubmittedAsync(Submitted(Context(contentType: "application/pdf")));
        var pdfMessages = pdf.Sales.Sent.Single().Messages;
        pdfMessages.Should().ContainSingle();
        pdfMessages[0].Text.Should().Contain("สลิป PDF: https://");

        var noKey = new Harness();
        noKey.Options.Package.SlipLinkKey = "";
        await noKey.Notifier.SubmittedAsync(Submitted(Context()));
        noKey.Sales.Sent.Single().Messages.Should().ContainSingle().Which.Text.Should().NotContain("package-slips");

        var http = new Harness();
        http.Options.Package.PublicApiBaseUrl = "http://localhost:5080";
        await http.Notifier.SubmittedAsync(Submitted(Context()));
        http.Sales.Sent.Single().Messages.Should().ContainSingle("LINE รับรูปจาก https เท่านั้น")
            .Which.Text.Should().Contain("สลิป: http://localhost:5080");
    }

    [Fact]
    public async Task Line_problems_never_throw()
    {
        var off = new Harness();
        off.Sales.IsConfigured = false;
        (await off.Notifier.SubmittedAsync(Submitted(Context()))).Should().BeFalse();
        off.Sales.Sent.Should().BeEmpty();

        var down = new Harness();
        down.Sales.Throws = true;
        (await down.Notifier.SubmittedAsync(Submitted(Context()))).Should().BeFalse();
        (await down.Notifier.CancelledAsync(Context(PackagePaymentStatus.Cancelled))).Should().BeFalse();
    }

    [Fact]
    public async Task Cancellation_is_told_to_the_team_once()
    {
        var h = new Harness();
        var context = Context(PackagePaymentStatus.Cancelled);
        context.Payment.CancelledBy = "SP1042.M01";

        await h.Notifier.CancelledAsync(context);

        var (key, messages) = h.Sales.Sent.Single();
        key.Should().Be(SelfServeText.DeterministicGuid($"package-payment-cancel:{context.Payment.Id}"));
        messages.Single().Text.Should().Contain("ยกเลิกแจ้งชำระ").And.Contain("3f2a9c1b").And.Contain("โดย SP1042.M01");
    }

    [Fact]
    public async Task Approval_emails_the_owner_and_notifies_managers_in_the_sp1_scope()
    {
        var h = new Harness();
        var context = Context(PackagePaymentStatus.Approved);
        var endsTo = new DateTime(2027, 2, 13, 17, 0, 0, DateTimeKind.Utc);

        var outcome = await h.Notifier.ReviewedAsync(new PackagePaymentReviewedNotice(context, endsTo));

        outcome.Should().Be(new PackageReviewNotifyOutcome(true, true));
        var email = h.Email.Sent.Single();
        email.To.Should().Be("owner@example.com");
        email.Tag.Should().Be(SelfServeEmails.PaymentApprovedTag);

        var scope = h.Scopes.Users.Single();
        scope.ShardKey.Should().Be("sp1");
        scope.BranchId.Should().Be(61);
        var (draft, roles) = h.Publisher.ToRoles.Single();
        roles.Should().Equal(UserRole.Manager);
        draft.Kind.Should().Be("package.payment_approved");
        draft.EntityType.Should().Be("PackagePayment");
        draft.LinkHint.Should().Be("package");
        draft.EntityId.Should().Be(context.Payment.Id);
        draft.TitleTh.Should().Contain("13/02/2570");
        h.Notifications.Saves.Should().Be(1, "publisher ไม่ save เอง ผู้เรียกเป็นคน save");
    }

    [Fact]
    public async Task Rejection_uses_the_rejected_kind_and_reports_each_failure_separately()
    {
        var h = new Harness();
        h.Email.Throws = true;

        var outcome = await h.Notifier.ReviewedAsync(new PackagePaymentReviewedNotice(Context(PackagePaymentStatus.Rejected), null));

        outcome.Should().Be(new PackageReviewNotifyOutcome(false, true));
        var draft = h.Publisher.ToRoles.Single().Draft;
        draft.Kind.Should().Be("package.payment_rejected");
        draft.BodyTh.Should().Contain("สลิปซ้ำกับที่เคยแจ้ง").And.Contain("สลิปเดียวกับเดือนที่แล้ว");

        var dbDown = new Harness();
        dbDown.Scopes.Throws = true;
        (await dbDown.Notifier.ReviewedAsync(new PackagePaymentReviewedNotice(Context(PackagePaymentStatus.Rejected), null)))
            .Should().Be(new PackageReviewNotifyOutcome(true, false));
    }

    [Fact]
    public async Task No_owner_email_means_no_email_but_still_in_app()
    {
        var h = new Harness();
        var context = Context(PackagePaymentStatus.Approved) with { OwnerEmail = null };

        (await h.Notifier.ReviewedAsync(new PackagePaymentReviewedNotice(context, Now.AddDays(30))))
            .Should().Be(new PackageReviewNotifyOutcome(false, true));
    }
}
