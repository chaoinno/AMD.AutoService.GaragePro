using System.Net;
using System.Text;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.Email;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ตัวส่งอีเมลผ่าน Cloudflare Email Service — รูปแบบคำขอตามเอกสาร REST (docs/16) และข้อห้ามด้านความปลอดภัย:
/// ไม่ log เนื้อหา (มี OTP) / ไม่ log ที่อยู่เต็ม / ตั้งค่าไม่ครบต้องซ่อนการสมัครด้วยอีเมลแทนที่จะพัง
/// </summary>
public sealed class CloudflareEmailSenderTests
{
    private const string Otp = "482913";
    private const string Recipient = "owner.eak@example.co.th";

    private static readonly EmailMessage Message = new(
        Recipient, "รหัสยืนยันอีเมล ServicePro", $"รหัสของคุณคือ {Otp}", $"<p>รหัสของคุณคือ <b>{Otp}</b></p>", "otp");

    private static SelfServeOptions Options(Action<SelfServeOptions.EmailOptions>? configure = null)
    {
        var options = new SelfServeOptions();
        options.Email.Provider = "cloudflare";
        options.Email.AccountId = "acc123";
        options.Email.ApiKey = "cf-token";
        options.Email.FromAddress = "no-reply@garage-pro.net";
        options.Email.FromName = "ServicePro";
        configure?.Invoke(options.Email);
        return options;
    }

    private static (CloudflareEmailSender Sender, StubHandler Http, ListLogger Log) Create(
        HttpStatusCode status = HttpStatusCode.OK, string? body = null, Action<SelfServeOptions.EmailOptions>? configure = null)
    {
        var http = new StubHandler(status, body ?? Success(delivered: [Recipient]));
        var log = new ListLogger();
        var sender = new CloudflareEmailSender(Microsoft.Extensions.Options.Options.Create(Options(configure)), log,
            new HttpClient(http));
        return (sender, http, log);
    }

    private static string Success(string[]? delivered = null, string[]? queued = null, string[]? bounced = null) =>
        JsonSerializer.Serialize(new
        {
            success = true,
            errors = Array.Empty<object>(),
            messages = Array.Empty<object>(),
            result = new
            {
                delivered = delivered ?? [],
                permanent_bounces = bounced ?? [],
                queued = queued ?? [],
            },
        });

    private static string Failure(int code, string message) =>
        JsonSerializer.Serialize(new
        {
            success = false,
            errors = new[] { new { code, message } },
            messages = Array.Empty<object>(),
            result = (object?)null,
        });

    [Fact]
    public async Task Posts_the_documented_request_with_bearer_token_and_a_content_length()
    {
        var (sender, http, _) = Create(configure: o => o.ReplyToAddress = "support@garage-pro.net");

        await sender.SendAsync(Message);

        http.Method.Should().Be(HttpMethod.Post);
        http.Uri.Should().Be("https://api.cloudflare.com/client/v4/accounts/acc123/email/sending/send");
        http.Authorization.Should().Be("Bearer cf-token");
        http.ContentType.Should().Be("application/json");
        http.ContentLength.Should().NotBeNull("JsonContent ส่งแบบ chunked ไม่มี Content-Length — ต้องเป็น StringContent");

        using var json = JsonDocument.Parse(http.Body!);
        var root = json.RootElement;
        root.GetProperty("from").GetProperty("address").GetString().Should().Be("no-reply@garage-pro.net");
        root.GetProperty("from").GetProperty("name").GetString().Should().Be("ServicePro");
        root.GetProperty("to").GetString().Should().Be(Recipient);
        root.GetProperty("subject").GetString().Should().Be("รหัสยืนยันอีเมล ServicePro");
        root.GetProperty("text").GetString().Should().Contain(Otp);
        root.GetProperty("html").GetString().Should().Contain(Otp);
        root.GetProperty("reply_to").GetString().Should().Be("support@garage-pro.net");
    }

    [Fact]
    public async Task Leaves_out_html_and_reply_to_when_there_is_none()
    {
        var (sender, http, _) = Create();

        await sender.SendAsync(Message with { HtmlBody = null });

        using var json = JsonDocument.Parse(http.Body!);
        json.RootElement.TryGetProperty("html", out _).Should().BeFalse();
        json.RootElement.TryGetProperty("reply_to", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Queued_delivery_counts_as_sent()
    {
        var (sender, _, log) = Create(body: Success(queued: [Recipient]));

        await sender.Invoking(s => s.SendAsync(Message)).Should().NotThrowAsync();
        log.Lines.Should().Contain(l => l.Contains("queued"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 10105, "email.sending.error.authentication.not_entitled")]
    [InlineData(HttpStatusCode.Unauthorized, 10101, "email.sending.error.authentication.unauthorized")]
    [InlineData(HttpStatusCode.TooManyRequests, 10004, "email.sending.error.throttled")]
    [InlineData(HttpStatusCode.BadRequest, 10001, "email.sending.error.invalid_request_schema")]
    public async Task Cloudflare_errors_throw_with_the_code_but_without_the_recipient_or_content(
        HttpStatusCode status, int code, string error)
    {
        var (sender, _, log) = Create(status, Failure(code, error));

        var thrown = await sender.Invoking(s => s.SendAsync(Message)).Should().ThrowAsync<EmailDeliveryException>();

        thrown.Which.Message.Should().Contain(code.ToString()).And.Contain(((int)status).ToString())
            .And.NotContain(Recipient).And.NotContain(Otp);
        log.Lines.Should().Contain(l => l.Contains(code.ToString()) && l.Contains("example.co.th"));
    }

    [Fact]
    public async Task A_non_json_error_page_still_throws_with_the_http_status()
    {
        var (sender, _, _) = Create(HttpStatusCode.BadGateway, "<html>bad gateway</html>");

        var thrown = await sender.Invoking(s => s.SendAsync(Message)).Should().ThrowAsync<EmailDeliveryException>();
        thrown.Which.Message.Should().Contain("502");
    }

    [Fact]
    public async Task A_permanent_bounce_is_a_failure_because_the_user_will_never_get_the_code()
    {
        var (sender, _, _) = Create(body: Success(bounced: [Recipient]));

        await sender.Invoking(s => s.SendAsync(Message)).Should().ThrowAsync<EmailDeliveryException>()
            .WithMessage("*permanent bounce*");
    }

    [Fact]
    public async Task Network_failure_is_wrapped()
    {
        var http = new StubHandler(new HttpRequestException("connection refused"));
        var sender = new CloudflareEmailSender(Microsoft.Extensions.Options.Options.Create(Options()), new ListLogger(), new HttpClient(http));

        await sender.Invoking(s => s.SendAsync(Message)).Should().ThrowAsync<EmailDeliveryException>()
            .WithMessage("*เชื่อมต่อ*");
    }

    [Fact]
    public async Task Never_logs_the_content_the_full_address_or_the_token()
    {
        var (ok, _, okLog) = Create();
        await ok.SendAsync(Message);
        var (failing, _, failLog) = Create(HttpStatusCode.Forbidden, Failure(10102, "email.sending.error.authentication.forbidden"));
        await failing.Invoking(s => s.SendAsync(Message)).Should().ThrowAsync<EmailDeliveryException>();

        foreach (var line in okLog.Lines.Concat(failLog.Lines))
            line.Should().NotContain(Otp).And.NotContain(Recipient).And.NotContain("cf-token");
    }

    [Theory]
    [InlineData("AccountId")]
    [InlineData("ApiKey")]
    [InlineData("FromAddress")]
    public async Task Missing_settings_turn_email_signup_off_instead_of_crashing(string missing)
    {
        var (sender, http, log) = Create(configure: o =>
        {
            if (missing == "AccountId") o.AccountId = "";
            if (missing == "ApiKey") o.ApiKey = " ";
            if (missing == "FromAddress") o.FromAddress = "";
        });

        sender.IsConfigured.Should().BeFalse("signup/config ต้องตอบ emailEnabled=false ใน production");
        log.Lines.Should().ContainSingle(l => l.Contains("SelfServe:Email:" + missing));
        await sender.Invoking(s => s.SendAsync(Message)).Should().ThrowAsync<InvalidOperationException>();
        http.Calls.Should().Be(0);
    }

    // ---------------------------------------------------------------- registration

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSelfServeInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provider_cloudflare_registers_the_cloudflare_sender()
    {
        using var provider = Build(new()
        {
            ["SelfServe:Email:Provider"] = "Cloudflare",
            ["SelfServe:Email:AccountId"] = "acc123",
            ["SelfServe:Email:ApiKey"] = "cf-token",
            ["SelfServe:Email:FromAddress"] = "no-reply@garage-pro.net",
        });

        provider.GetRequiredService<IEmailSender>().Should().BeOfType<CloudflareEmailSender>()
            .Which.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void An_unsupported_provider_fails_loudly_instead_of_falling_back_to_the_log()
    {
        using var provider = Build(new() { ["SelfServe:Email:Provider"] = "brevo" });

        provider.Invoking(p => p.GetRequiredService<IEmailSender>())
            .Should().Throw<InvalidOperationException>().WithMessage("*brevo*");
    }

    // ---------------------------------------------------------------- fakes

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = "";
        private readonly Exception? _throw;

        public StubHandler(HttpStatusCode status, string body) => (_status, _body) = (status, body);
        public StubHandler(Exception toThrow) => _throw = toThrow;

        public int Calls { get; private set; }
        public HttpMethod? Method { get; private set; }
        public string? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? ContentType { get; private set; }
        public long? ContentLength { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Method = request.Method;
            Uri = request.RequestUri?.ToString();
            Authorization = request.Headers.Authorization?.ToString();
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            ContentLength = request.Content?.Headers.ContentLength;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            if (_throw is not null) throw _throw;
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class ListLogger : ILogger<CloudflareEmailSender>
    {
        public List<string> Lines { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Lines.Add(formatter(state, exception) + (exception is null ? "" : " | " + exception.Message));
    }
}
