using System.Globalization;
using AMD.AutoService.GaragePro.API.RateLimiting;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// แพ็กเกจและการแจ้งชำระเงินของอู่ที่สมัครเอง (docs/17 §5.1) · <c>[Authorize]</c> อย่างเดียว (ไม่ผูกกะ) · shard/สาขา/ผู้แจ้งจาก JWT
/// · อยู่ใต้ <c>/api/v1/selfserve</c> ที่ TrialReadOnlyFilter ข้ามอยู่แล้ว: แจ้งชำระได้แม้อู่อ่านอย่างเดียว (กฎข้อ 11)
/// · service ลงทะเบียนเฉพาะเมื่อตั้งฐาน self-serve → resolve แบบ optional (ไม่ได้ตั้ง = 404 SELFSERVE_NOT_APPLICABLE)
/// · POST แบบ multipart อ่านฟอร์มเอง (ไม่ผ่าน model binding) ทุกค่าเป็นข้อความแล้วตรวจที่ service:
///   ค่าผิดรูป/ไฟล์ใหญ่เกินได้ envelope 422/413 เสมอ ไม่ใช่ 400 ProblemDetails · ฟิลด์ใน Swagger จึงไม่แสดง (สัญญาอยู่ที่ docs/03 §16)
/// </summary>
[ApiController]
[Route("api/v1/selfserve/package")]
[Produces("application/json")]
[Authorize]
public sealed class SelfServePackageController(IServiceProvider services) : ControllerBase
{
    /// <summary>สลิป ≤ 15 MB + ฟิลด์ข้อความ · เกินนี้ Kestrel/ตัวอ่าน multipart ปฏิเสธก่อนถึง service (ตอบ 413 envelope)</summary>
    public const long MaxRequestBytes = 16 * 1024 * 1024;

    /// <summary>แพ็กเกจปัจจุบัน · ตัวเลือกจำนวนเดือน + พรีวิววันหมดถ้าอนุมัติวันนี้ · บัญชีรับเงิน · รายการรอตรวจ</summary>
    [HttpGet]
    public async Task<IActionResult> Overview(CancellationToken ct) =>
        Service is { } s ? Render(await s.GetOverviewAsync(ct)) : NotApplicable();

    /// <summary>ประวัติแจ้งชำระ ใหม่สุดก่อน (take ≤ 50 · ค่าเริ่มต้น 20) · ไม่ใช่ผู้จัดการไม่เห็นยอดเงิน/หมายเหตุ/สลิป</summary>
    [HttpGet("payments")]
    public async Task<IActionResult> Payments([FromQuery] string? take, CancellationToken ct) =>
        Service is { } s
            ? Render(await s.ListPaymentsAsync(
                int.TryParse(take, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : null, ct))
            : NotApplicable();

    /// <summary>
    /// แจ้งชำระ (multipart: requestId · paidAt · amount · monthCount · note · slip) · เฉพาะผู้จัดการ · 10 ครั้ง/ชม. ต่ออู่
    /// · requestId เดิมส่งซ้ำได้แถวเดิม (ไม่อัปโหลดซ้ำ ไม่ส่ง LINE ซ้ำ)
    /// </summary>
    [HttpPost("payments")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
    [EnableRateLimiting(RateLimitPolicies.PackagePaymentSubmit)]
    public async Task<IActionResult> Submit(CancellationToken ct)
    {
        if (Service is not { } service) return NotApplicable();
        if (!Request.HasFormContentType)
            return Render(Result<PackagePaymentDto>.Fail(PackageErrors.Validation(
                "ต้องส่งแบบ multipart/form-data พร้อมไฟล์สลิป", "slip")));

        IFormCollection form;
        try
        {
            form = await Request.ReadFormAsync(ct);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return TooLarge();
        }
        catch (InvalidDataException)
        {
            // ตัวอ่าน multipart โยนเมื่อเกิน MultipartBodyLengthLimit หรือรูปแบบ multipart เสีย · ทั้งสองกรณีไฟล์ใช้ไม่ได้
            return TooLarge();
        }

        var file = form.Files.GetFile("slip");
        await using var content = file?.OpenReadStream();
        var submission = new PackagePaymentSubmission(
            Field(form, "requestId"), Field(form, "paidAt"), Field(form, "amount"), Field(form, "monthCount"),
            Field(form, "note"),
            file is null ? null : new PackageSlipUpload(content!, file.ContentType, file.Length, file.FileName));

        return Render(await service.SubmitAsync(submission, ct));
    }

    /// <summary>ยกเลิกรายการที่รอตรวจของอู่ตัวเอง (เฉพาะผู้จัดการ)</summary>
    [HttpPost("payments/{id}/cancel")]
    public async Task<IActionResult> Cancel(string id, CancellationToken ct) =>
        Service is { } s ? Render(await s.CancelAsync(id, ct)) : NotApplicable();

    /// <summary>ไฟล์สลิป (image/jpeg หรือ application/pdf) เฉพาะผู้จัดการของอู่นั้น · ต้องมี Bearer token (ห้ามใช้ &lt;img src&gt; ตรง)</summary>
    [HttpGet("payments/{id}/slip")]
    public async Task<IActionResult> Slip(string id, CancellationToken ct)
    {
        if (Service is not { } service) return NotApplicable();
        var result = await service.OpenSlipAsync(id, ct);
        if (!result.Success) return Render(result);

        var file = result.Data!;
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(file.Content, file.ContentType, file.FileName);
    }

    private IPackagePaymentService? Service => services.GetService<IPackagePaymentService>();

    private static string? Field(IFormCollection form, string name) =>
        form.TryGetValue(name, out var value) ? value.ToString() : null;

    private IActionResult TooLarge() => Render(Result<PackagePaymentDto>.Fail(PackageErrors.AttachmentTooLarge(
        services.GetService<IPackageSlipStorage>()?.MaxSizeBytes ?? 15 * 1024 * 1024)));

    private IActionResult NotApplicable() => Render(Result<object>.Fail(SelfServeErrors.NotApplicable()));

    private IActionResult Render<T>(Result<T> result) => result.Success
        ? Ok(Envelope.From(result, HttpContext.TraceIdentifier))
        : StatusCode(StatusFor(result.Error!.Code), Envelope.From(result, HttpContext.TraceIdentifier));

    public static int StatusFor(string? code) =>
        PackageErrors.StatusCodeFor(code) ?? SelfServeController.StatusFor(code);
}
