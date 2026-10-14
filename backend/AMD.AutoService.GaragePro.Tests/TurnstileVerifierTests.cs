using System.Net;
using System.Text;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class TurnstileVerifierTests
{
    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add((request, body));
            return respond(request);
        }
    }

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static (TurnstileVerifier Verifier, FakeHandler Handler) Create(
        string secret, bool production, Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var options = new SelfServeOptions();
        options.Turnstile.SecretKey = secret;
        var handler = new FakeHandler(respond);
        var verifier = new TurnstileVerifier(Options.Create(options), NullLogger<TurnstileVerifier>.Instance,
            new SignupHostEnvironment(production), new HttpClient(handler));
        return (verifier, handler);
    }

    [Fact]
    public async Task Posts_secret_token_and_ip_to_siteverify_and_accepts_success()
    {
        var (verifier, handler) = Create("secret-1", production: true, _ => Json("""{"success":true}"""));

        (await verifier.VerifyAsync("token-1", "203.0.113.9")).Should().BeTrue();

        var (request, body) = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri!.ToString().Should().Be(TurnstileVerifier.SiteVerifyUrl);
        body.Should().Contain("secret=secret-1").And.Contain("response=token-1").And.Contain("remoteip=203.0.113.9");
    }

    [Theory]
    [InlineData("""{"success":false,"error-codes":["invalid-input-response"]}""", HttpStatusCode.OK)]
    [InlineData("""{"success":true}""", HttpStatusCode.InternalServerError)]
    [InlineData("not json", HttpStatusCode.OK)]
    public async Task Rejects_failures_and_broken_responses(string json, HttpStatusCode status)
    {
        var (verifier, _) = Create("secret-1", production: true, _ => Json(json, status));

        (await verifier.VerifyAsync("token-1", null)).Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_when_cloudflare_is_unreachable()
    {
        var (verifier, _) = Create("secret-1", production: false, _ => throw new HttpRequestException("down"));

        (await verifier.VerifyAsync("token-1", null)).Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_missing_token_without_calling_cloudflare()
    {
        var (verifier, handler) = Create("secret-1", production: true, _ => Json("""{"success":true}"""));

        (await verifier.VerifyAsync(null, null)).Should().BeFalse();
        (await verifier.VerifyAsync("  ", null)).Should().BeFalse();
        handler.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_secret_passes_outside_production_and_fails_closed_in_production()
    {
        var (dev, devHandler) = Create("", production: false, _ => Json("""{"success":false}"""));
        (await dev.VerifyAsync(null, null)).Should().BeTrue();
        devHandler.Requests.Should().BeEmpty();

        var (prod, prodHandler) = Create("", production: true, _ => Json("""{"success":true}"""));
        (await prod.VerifyAsync("token-1", null)).Should().BeFalse();
        prodHandler.Requests.Should().BeEmpty();
    }
}
