using AMD.AutoService.GaragePro.Application.Contact;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class ContactRequestServiceTests
{
    private sealed class FakeNotifier : IContactNotifier
    {
        public bool IsConfigured { get; set; } = true;
        public bool Succeeds { get; set; } = true;
        public List<(Guid Key, string Text)> Sent { get; } = [];

        public Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct)
        {
            Sent.Add((retryKey, text));
            return Task.FromResult(Succeeds);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static readonly DateTimeOffset Now = new(2026, 9, 23, 3, 30, 0, TimeSpan.Zero);

    private static ContactRequest Valid(Guid? id = null) => new(
        id ?? Guid.NewGuid(), "คุณทดสอบ", "อู่ทดสอบ", "081-234-5678", "@test", "30–100 คัน", "มีสองสาขา");

    private static (ContactRequestService Service, FakeNotifier Notifier) Create()
    {
        var notifier = new FakeNotifier();
        return (new ContactRequestService(notifier, new FixedClock(Now)), notifier);
    }

    [Fact]
    public async Task Sends_formatted_message_with_request_id_as_retry_key()
    {
        var (service, notifier) = Create();
        var request = Valid();

        var result = await service.SubmitAsync(request, default);

        result.Success.Should().BeTrue();
        notifier.Sent.Should().ContainSingle();
        notifier.Sent[0].Key.Should().Be(request.RequestId);
        notifier.Sent[0].Text.Should().Contain("ชื่อผู้ติดต่อ: คุณทดสอบ")
            .And.Contain("เบอร์โทร: 081-234-5678")
            .And.Contain("ขนาดอู่: 30–100 คัน")
            .And.Contain("เวลา: 23/09/2569 10:30 น.") // UTC+7
            .And.NotContain("แพ็กเกจ");
    }

    [Fact]
    public async Task Honeypot_is_silently_accepted_without_sending()
    {
        var (service, notifier) = Create();

        var result = await service.SubmitAsync(Valid() with { Website = "http://spam" }, default);

        result.Success.Should().BeTrue();
        notifier.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("garage")]
    [InlineData("phone")]
    public async Task Rejects_missing_required_fields(string field)
    {
        var (service, notifier) = Create();
        var request = field switch
        {
            "name" => Valid() with { Name = " " },
            "garage" => Valid() with { Garage = null },
            _ => Valid() with { Phone = "" },
        };

        var result = await service.SubmitAsync(request, default);

        result.Error!.Code.Should().Be("CONTACT_VALIDATION");
        result.Error.Field.Should().Be(field);
        notifier.Sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("081-234-5678 โทรหาหน่อย")]
    public async Task Rejects_invalid_phone(string phone)
    {
        var (service, _) = Create();
        var result = await service.SubmitAsync(Valid() with { Phone = phone }, default);
        result.Error!.Field.Should().Be("phone");
    }

    [Fact]
    public async Task Rejects_garage_size_outside_the_fixed_options()
    {
        var (service, _) = Create();
        var result = await service.SubmitAsync(Valid() with { GarageSize = "Enterprise" }, default);
        result.Error!.Field.Should().Be("garageSize");
    }

    [Fact]
    public async Task Strips_newlines_from_single_line_fields_so_lines_cannot_be_forged()
    {
        var (service, notifier) = Create();

        await service.SubmitAsync(Valid() with { Name = "สมชาย\nเบอร์โทร: 099-999-9999" }, default);

        notifier.Sent[0].Text.Split('\n').Count(l => l.StartsWith("เบอร์โทร:")).Should().Be(1);
    }

    [Fact]
    public async Task Reports_unavailable_when_line_is_not_configured()
    {
        var (service, notifier) = Create();
        notifier.IsConfigured = false;

        var result = await service.SubmitAsync(Valid(), default);

        result.Error!.Code.Should().Be("CONTACT_UNAVAILABLE");
        notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Reports_send_failure_from_line()
    {
        var (service, notifier) = Create();
        notifier.Succeeds = false;

        var result = await service.SubmitAsync(Valid(), default);

        result.Error!.Code.Should().Be("CONTACT_SEND_FAILED");
    }
}
