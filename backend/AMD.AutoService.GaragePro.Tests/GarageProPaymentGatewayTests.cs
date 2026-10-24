using System.Net;
using System.Text;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Net.Http.Headers;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ตัวส่งการแจ้งชำระไปยัง API เดิม (docs/17 §8.7.1): ชื่อ/ค่าฟิลด์ multipart · อ่านคำตอบ Success/Message/Detail.Id ·
/// HTTP ผิดปกติ/redirect/อ่านไม่ออก/เชื่อมต่อไม่ได้ = ไม่ทราบผล · ไม่ได้ตั้ง BaseUrl ไม่ยิงอะไร · ใช้ handler จำลอง ไม่ยิงจริง
/// </summary>
public class GarageProPaymentGatewayTests
{
    private const string BaseUrl = "https://legacy-api.example.test/root";

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body, string? ContentType)> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            Calls.Add((request, body, request.Content?.Headers.ContentType?.ToString()));
            return respond(request);
        }
    }

    private static GarageProPaymentGateway Gateway(StubHandler handler, string baseUrl = BaseUrl)
    {
        var options = new SelfServeOptions();
        options.Package.GarageProApi.BaseUrl = baseUrl;
        return new GarageProPaymentGateway(Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<GarageProPaymentGateway>.Instance, new HttpClient(handler));
    }

    private static GarageProAddPaymentRequest Request() => new(2, 227, new DateTime(2026, 10, 21, 14, 30, 5), 3, 501,
        [0xFF, 0xD8, 0xFF, 0x01], "slip-227-20261021074100.jpg", "image/jpeg");

    private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static async Task<Dictionary<string, (string? FileName, string? ContentType, byte[] Body)>> Parts(byte[] body,
        string contentType)
    {
        var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(contentType).Boundary).Value!;
        var reader = new MultipartReader(boundary, new MemoryStream(body));
        var parts = new Dictionary<string, (string?, string?, byte[])>();
        while (await reader.ReadNextSectionAsync() is { } section)
        {
            var disposition = ContentDispositionHeaderValue.Parse(section.ContentDisposition);
            using var buffer = new MemoryStream();
            await section.Body.CopyToAsync(buffer);
            parts[HeaderUtilities.RemoveQuotes(disposition.Name).Value!] =
                (HeaderUtilities.RemoveQuotes(disposition.FileName).Value, section.ContentType, buffer.ToArray());
        }

        return parts;
    }

    [Fact]
    public async Task Sends_the_legacy_multipart_fields_and_reads_the_new_row_id()
    {
        var handler = new StubHandler(_ => Json("{\"Success\":true,\"Message\":\"บันทึกสำเร็จ\",\"Detail\":{\"Id\":146}}"));

        var result = await Gateway(handler).AddPaymentAsync(Request());

        result.Should().Be(new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Accepted, "บันทึกสำเร็จ", 146));
        var (request, body, contentType) = handler.Calls.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("https://legacy-api.example.test/root/api/Branch/AddPayment"));
        contentType.Should().StartWith("multipart/form-data");

        var parts = await Parts(body, contentType!);
        parts.Keys.Should().BeEquivalentTo("Zone", "BranchId", "PayDate", "PayTime", "MonthCount", "CreatedBy", "PaySlip");
        string Text(string name) => Encoding.UTF8.GetString(parts[name].Body);
        Text("Zone").Should().Be("2");
        Text("BranchId").Should().Be("227");
        Text("PayDate").Should().Be("2026-10-21T00:00:00");
        Text("PayTime").Should().Be("14:30:05");
        Text("MonthCount").Should().Be("3");
        Text("CreatedBy").Should().Be("501");
        parts["PaySlip"].FileName.Should().Be("slip-227-20261021074100.jpg");
        parts["PaySlip"].ContentType.Should().Be("image/jpeg");
        parts["PaySlip"].Body.Should().Equal(0xFF, 0xD8, 0xFF, 0x01);
        Encoding.UTF8.GetString(body).Should().NotContain("filename*", "ตัวอ่าน multipart รุ่นเก่าบางตัวไม่รองรับ");
    }

    [Theory]
    [InlineData("{\"Success\":false,\"Message\":\"ไม่พบสาขา\"}", GarageProAddPaymentOutcome.Rejected, "ไม่พบสาขา", null)]
    [InlineData("{\"success\":true,\"detail\":{\"id\":\"147\"}}", GarageProAddPaymentOutcome.Accepted, null, 147L)]
    [InlineData("{\"Success\":true}", GarageProAddPaymentOutcome.Accepted, null, null)]
    [InlineData("{\"Message\":\"ok\"}", GarageProAddPaymentOutcome.Unavailable, null, null)]
    [InlineData("<html>error</html>", GarageProAddPaymentOutcome.Unavailable, null, null)]
    [InlineData("[]", GarageProAddPaymentOutcome.Unavailable, null, null)]
    public async Task Reads_the_legacy_answer(string json, GarageProAddPaymentOutcome outcome, string? message, long? id)
    {
        var result = await Gateway(new StubHandler(_ => Json(json))).AddPaymentAsync(Request());
        result.Outcome.Should().Be(outcome);
        if (outcome != GarageProAddPaymentOutcome.Unavailable)
        {
            result.Message.Should().Be(message);
            result.PaymentId.Should().Be(id);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Non_success_http_is_an_unknown_outcome(HttpStatusCode status)
    {
        var handler = new StubHandler(_ =>
        {
            var response = Json("{\"Success\":true}", status);
            if (status == HttpStatusCode.Found) response.Headers.Location = new Uri("https://elsewhere.example.test/");
            return response;
        });

        (await Gateway(handler).AddPaymentAsync(Request())).Outcome.Should().Be(GarageProAddPaymentOutcome.Unavailable);
        handler.Calls.Should().ContainSingle("ไม่ตาม redirect");
    }

    [Fact]
    public async Task Network_failures_and_timeouts_are_unknown_outcomes()
    {
        var down = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        (await Gateway(down).AddPaymentAsync(Request())).Outcome.Should().Be(GarageProAddPaymentOutcome.Unavailable);

        var slow = new StubHandler(_ => throw new TaskCanceledException("timeout"));
        (await Gateway(slow).AddPaymentAsync(Request())).Outcome.Should().Be(GarageProAddPaymentOutcome.Unavailable);

        var huge = new StubHandler(_ => Json("{\"Success\":true,\"Message\":\"" + new string('a', 70_000) + "\"}"));
        (await Gateway(huge).AddPaymentAsync(Request())).Outcome.Should().Be(GarageProAddPaymentOutcome.Unavailable);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://legacy.example.test")]
    public async Task Without_a_base_url_nothing_is_sent(string baseUrl)
    {
        var handler = new StubHandler(_ => Json("{\"Success\":true}"));
        var gateway = Gateway(handler, baseUrl);

        gateway.IsConfigured.Should().BeFalse();
        (await gateway.AddPaymentAsync(Request())).Outcome.Should().Be(GarageProAddPaymentOutcome.Unavailable);
        handler.Calls.Should().BeEmpty();
    }
}
