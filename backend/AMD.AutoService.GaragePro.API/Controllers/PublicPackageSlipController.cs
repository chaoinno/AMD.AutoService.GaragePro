using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// รูปสลิปให้ LINE ดึงไปแสดงในกลุ่มทีมงาน (docs/17 §5.3) · ไม่ต้องล็อกอิน แต่ต้องมีลิงก์ที่ระบบเซ็นไว้และยังไม่หมดอายุ
/// · [SECURITY] ผิดทุกกรณี (ไม่ได้ตั้งคีย์ · ลายเซ็นผิด · หมดอายุ · ไม่พบ · อู่ถูกลบ · FTP ล่ม) ตอบ 404 เปล่า ไม่บอกเหตุผล
///   · ตรวจลายเซ็นก่อนแตะฐาน/FTP (ใน service) · ถังจำกัดอัตราของตัวเอง <c>public-package-slip</c> (ไม่แย่งถังฟอร์มติดต่อ)
/// · header: ไม่ให้เดาชนิดไฟล์ · ไม่ส่ง referrer · ไม่ให้เครื่องมือค้นหาเก็บ · cache ส่วนตัว 1 ชม. (LINE ดึงซ้ำได้โดยไม่ยิง FTP ทุกครั้ง)
/// </summary>
[ApiController]
[Route("api/v1/public/package-slips")]
public sealed class PublicPackageSlipController(
    IServiceProvider services,
    ILogger<PublicPackageSlipController> logger) : ControllerBase
{
    [HttpGet("{id}")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Get(string id, [FromQuery] string? exp, [FromQuery] string? sig, CancellationToken ct)
    {
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex";

        if (services.GetService<IPackagePaymentService>() is not { } service) return NotFound();

        PackageSlipFile? file;
        try
        {
            file = await service.OpenSignedSlipAsync(id, exp, sig, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // [SECURITY] ไม่ log query string (มีลายเซ็นที่เปิดดูสลิปได้)
            logger.LogWarning(ex, "เปิดสลิปจากลิงก์สาธารณะไม่สำเร็จ");
            return NotFound();
        }

        if (file is null) return NotFound();

        Response.Headers.CacheControl = "private, max-age=3600";
        return File(file.Content, file.ContentType);
    }
}
