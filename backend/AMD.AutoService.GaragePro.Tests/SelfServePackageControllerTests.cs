using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// controller ของแพ็กเกจ (docs/17 §5.1/§5.3): อ่าน multipart เองแล้วส่งเป็นข้อความทั้งหมด · HTTP status ตามสัญญา ·
/// ไม่ได้ตั้ง self-serve = 404 · ลิงก์สาธารณะผิดทุกกรณี = 404 เปล่าพร้อม header กันแคช/ดัชนี
/// </summary>
public class SelfServePackageControllerTests
{
    private sealed class FakeService : IPackagePaymentService
    {
        public PackagePaymentSubmission? Last { get; private set; }
        public Result<PackagePaymentDto>? SubmitResult { get; set; }
        public PackageSlipFile? Signed { get; set; }

        public Task<Result<PackageOverviewDto>> GetOverviewAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Result<IReadOnlyList<PackagePaymentDto>>> ListPaymentsAsync(int? take, CancellationToken ct = default) =>
            Task.FromResult(Result<IReadOnlyList<PackagePaymentDto>>.Ok([]));

        public Task<Result<PackagePaymentDto>> SubmitAsync(PackagePaymentSubmission submission, CancellationToken ct = default)
        {
            Last = submission;
            return Task.FromResult(SubmitResult ?? Result<PackagePaymentDto>.Fail(PackageErrors.PendingExists()));
        }

        public Task<Result<PackagePaymentDto>> CancelAsync(string? paymentId, CancellationToken ct = default) =>
            Task.FromResult(Result<PackagePaymentDto>.Fail(PackageErrors.NotFound()));

        public Task<Result<PackageSlipFile>> OpenSlipAsync(string? paymentId, CancellationToken ct = default) =>
            Task.FromResult(Result<PackageSlipFile>.Fail(PackageErrors.Forbidden()));

        public Task<PackageSlipFile?> OpenSignedSlipAsync(string? paymentId, string? exp, string? sig,
            CancellationToken ct = default) => Task.FromResult(Signed);
    }

    private static SelfServePackageController Controller(FakeService? service, HttpContext context) =>
        new(service is null ? new MapServiceProvider() : new MapServiceProvider().Add<IPackagePaymentService>(service))
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

    private static HttpContext Multipart(Dictionary<string, StringValues> fields, IFormFile? file)
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "multipart/form-data; boundary=test";
        var files = new FormFileCollection();
        if (file is not null) files.Add(file);
        context.Features.Set<IFormFeature>(new FormFeature(context.Request) { Form = new FormCollection(fields, files) });
        return context;
    }

    private static int Status(IActionResult result) => result switch
    {
        ObjectResult o => o.StatusCode ?? 200,
        StatusCodeResult s => s.StatusCode,
        _ => throw new InvalidOperationException(result.GetType().Name)
    };

    [Fact]
    public async Task Multipart_fields_are_read_case_insensitively_and_passed_as_text()
    {
        var service = new FakeService();
        var bytes = new byte[] { 1, 2, 3 };
        var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "SLIP", "slip.png")
        {
            Headers = new HeaderDictionary(), ContentType = "image/png"
        };
        var context = Multipart(new Dictionary<string, StringValues>
        {
            ["RequestId"] = "req-1", ["paidAt"] = "2026-10-21T14:30:00+07:00", ["AMOUNT"] = "1,500.00",
            ["monthCount"] = "3", ["note"] = "x"
        }, file);

        var result = await Controller(service, context).Submit(CancellationToken.None);

        Status(result).Should().Be(409, "PACKAGE_PAYMENT_PENDING_EXISTS");
        var s = service.Last!;
        (s.RequestId, s.PaidAt, s.Amount, s.MonthCount, s.Note).Should()
            .Be(("req-1", "2026-10-21T14:30:00+07:00", "1,500.00", "3", "x"));
        s.Slip!.SizeBytes.Should().Be(3);
        s.Slip.ContentType.Should().Be("image/png");
    }

    [Fact]
    public async Task Non_multipart_requests_get_an_envelope_not_a_framework_error()
    {
        var context = new DefaultHttpContext();
        context.Request.ContentType = "application/json";

        var result = await Controller(new FakeService(), context).Submit(CancellationToken.None);

        Status(result).Should().Be(422);
        ((Envelope<PackagePaymentDto>)((ObjectResult)result).Value!).Error!.Field.Should().Be("slip");
    }

    [Fact]
    public async Task Missing_self_serve_is_not_applicable()
    {
        var result = await Controller(null, new DefaultHttpContext()).Payments(null, CancellationToken.None);
        Status(result).Should().Be(404);
    }

    [Theory]
    [InlineData(PackageErrors.ForbiddenCode, 403)]
    [InlineData(PackageErrors.UnavailableCode, 503)]
    [InlineData(PackageErrors.ValidationCode, 422)]
    [InlineData(PackageErrors.PendingExistsCode, 409)]
    [InlineData(PackageErrors.NotRenewableCode, 409)]
    [InlineData(PackageErrors.NotFoundCode, 404)]
    [InlineData(PackageErrors.NotPendingCode, 409)]
    [InlineData(PackageErrors.ApprovalConflictCode, 409)]
    [InlineData(PackageErrors.SlipUploadFailedCode, 503)]
    [InlineData(PackageErrors.AttachmentEmptyCode, 422)]
    [InlineData(PackageErrors.AttachmentTypeInvalidCode, 422)]
    [InlineData(PackageErrors.AttachmentTooLargeCode, 413)]
    [InlineData(SelfServeErrors.NotApplicableCode, 404)]
    public void Error_codes_map_to_the_contract_status(string code, int status)
    {
        SelfServePackageController.StatusFor(code).Should().Be(status);
        SelfServeController.StatusFor(code).Should().Be(status);
    }

    [Fact]
    public async Task Public_slip_link_returns_404_with_privacy_headers_or_the_file()
    {
        var service = new FakeService();
        var services = new MapServiceProvider().Add<IPackagePaymentService>(service);
        var context = new DefaultHttpContext();
        var controller = new PublicPackageSlipController(services, NullLogger<PublicPackageSlipController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context }
        };

        (await controller.Get("x", "1", "sig", CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        context.Response.Headers["X-Robots-Tag"].ToString().Should().Be("noindex");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("no-referrer");
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");

        service.Signed = new PackageSlipFile(new MemoryStream([1]), "image/jpeg", "slip.jpg");
        var ok = await controller.Get("x", "1", "sig", CancellationToken.None);
        ok.Should().BeOfType<FileStreamResult>().Which.ContentType.Should().Be("image/jpeg");
        context.Response.Headers.CacheControl.ToString().Should().Be("private, max-age=3600");
    }
}
