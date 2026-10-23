using System.Text.Json;
using AMD.AutoService.GaragePro.API.Internal;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Internal;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// ตรวจสลิป/อนุมัติ/ปฏิเสธการแจ้งชำระของอู่ sp1 จาก GaragePro Admin (docs/17 §8.4 ระยะ B · §8.6)
/// · ยืนยันตัวด้วย <c>X-Internal-Key</c> (<see cref="InternalApiAttribute"/>) · ผู้กระทำมาจาก body (<c>actorName</c>/<c>actorId</c>)
/// · service มีเฉพาะเมื่อตั้งฐาน self-serve → resolve แบบ optional (ไม่ได้ตั้ง = 404 SELFSERVE_NOT_APPLICABLE)
/// · body อ่านเองเป็น JSON (ไม่ผ่าน model binding) ค่าผิดชนิด/รูปแบบได้ 422 envelope เสมอ ไม่ใช่ 400 ProblemDetails
/// · อ่าน/เขียนฐานหรือ FTP ล้ม = 503 <c>INTERNAL_SOURCE_UNAVAILABLE</c> (รายละเอียดใน log)
/// </summary>
[ApiController]
[Route(InternalApi.RoutePrefix + "/package-payments")]
[Produces("application/json")]
[AllowAnonymous]
[InternalApi]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalPackagePaymentsController(
    IServiceProvider services,
    ILogger<InternalPackagePaymentsController> logger) : ControllerBase
{
    /// <summary>body ของ approve/reject มีแค่ข้อความสั้นๆ · กันคำขอที่ส่ง body ใหญ่เกินเหตุ</summary>
    public const long MaxBodyBytes = 16 * 1024;

    /// <summary>ใหม่สุดก่อน · status ค่าเริ่มต้น pending (pending | approved | rejected | cancelled | all) · take ค่าเริ่มต้น 50</summary>
    [HttpGet]
    public Task<IActionResult> List([FromQuery] string? status, [FromQuery] string? take, CancellationToken ct) =>
        Run(s => s.ListAsync(status, take, ct), ct);

    [HttpGet("{id}/slip")]
    public async Task<IActionResult> Slip(string id, CancellationToken ct)
    {
        if (Service is not { } service) return NotApplicable();

        Result<PackageSlipFile> result;
        try
        {
            result = await service.OpenSlipAsync(id, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Unavailable(ex, "เปิดไฟล์สลิป");
        }

        if (!result.Success) return Render(result);
        var file = result.Data!;
        return File(file.Content, file.ContentType, file.FileName);
    }

    [HttpPost("{id}/approve")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task<IActionResult> Approve(string id, CancellationToken ct) =>
        Review((s, body) => s.ApproveAsync(id, body, ct), ct);

    [HttpPost("{id}/reject")]
    [RequestSizeLimit(MaxBodyBytes)]
    public Task<IActionResult> Reject(string id, CancellationToken ct) =>
        Review((s, body) => s.RejectAsync(id, body, ct), ct);

    private InternalPackagePaymentsService? Service => services.GetService<InternalPackagePaymentsService>();

    private async Task<IActionResult> Review(
        Func<InternalPackagePaymentsService, InternalReviewRequest, Task<Result<InternalPackageReviewDto>>> work,
        CancellationToken ct)
    {
        if (Service is null) return NotApplicable();

        var body = await ReadBodyAsync(ct);
        if (!body.Success) return Render(body);
        return await Run(s => work(s, body.Data!), ct);
    }

    private async Task<Result<InternalReviewRequest>> ReadBodyAsync(CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(Request.Body, cancellationToken: ct);
            return InternalPackagePaymentsService.ParseBody(document.RootElement);
        }
        catch (JsonException)
        {
            return Result<InternalReviewRequest>.Fail(InternalPackagePaymentsService.BodyError());
        }
        catch (BadHttpRequestException)
        {
            // เกิน MaxBodyBytes หรือ body ขาดกลางทาง
            return Result<InternalReviewRequest>.Fail(InternalPackagePaymentsService.BodyError());
        }
    }

    private async Task<IActionResult> Run<T>(Func<InternalPackagePaymentsService, Task<Result<T>>> work,
        CancellationToken ct)
    {
        if (Service is not { } service) return NotApplicable();
        try
        {
            return Render(await work(service));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            return Unavailable(ex, "งานแจ้งชำระ");
        }
    }

    private IActionResult Unavailable(Exception ex, string what)
    {
        logger.LogError(ex, "API ภายใน: {What} ไม่สำเร็จ", what);
        return StatusCode(StatusCodes.Status503ServiceUnavailable,
            Envelope.From(Result<object>.Fail(InternalApi.SourceUnavailable()), HttpContext.TraceIdentifier));
    }

    private IActionResult NotApplicable() => Render(Result<object>.Fail(SelfServeErrors.NotApplicable()));

    private IActionResult Render<T>(Result<T> result) => result.Success
        ? Ok(Envelope.From(result, HttpContext.TraceIdentifier))
        : StatusCode(StatusFor(result.Error!.Code), Envelope.From(result, HttpContext.TraceIdentifier));

    /// <summary>
    /// รหัสของ docs/17 §5.1 ตาม <see cref="PackageErrors.StatusCodeFor"/> · อู่ที่เปลี่ยนแพ็กเกจไม่ได้ (purged/provisioning/ไม่มี Branch/
    /// ฐานผิด) 409 · ไม่พบอู่ 404 · ไม่ได้ตั้ง self-serve 404 · อื่นๆ ตาม controller ของ self-serve (ไม่รู้จัก = 422)
    /// </summary>
    public static int StatusFor(string? code) => code switch
    {
        SelfServeAdminErrors.RefusedCode => StatusCodes.Status409Conflict,
        SelfServeAdminErrors.ValidationCode => StatusCodes.Status422UnprocessableEntity,
        InternalApi.AuthRequiredCode => StatusCodes.Status401Unauthorized,
        InternalApi.SourceUnavailableCode => StatusCodes.Status503ServiceUnavailable,
        _ => PackageErrors.StatusCodeFor(code) ?? SelfServeController.StatusFor(code)
    };
}
