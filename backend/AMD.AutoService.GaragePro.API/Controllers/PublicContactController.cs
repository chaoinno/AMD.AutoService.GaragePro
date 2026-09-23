using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Contact;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// ฟอร์ม "ขอ Demo" ของหน้า landing — endpoint สาธารณะตัวเดียวของระบบที่ไม่ต้องล็อกอิน
/// ส่งต่อเป็นข้อความเข้ากลุ่ม LINE ของทีมขาย ไม่ได้บันทึกลงฐานข้อมูล
/// </summary>
[ApiController]
[Route("api/v1/public/contact-requests")]
[Produces("application/json")]
public sealed class PublicContactController(IContactRequestService service) : ControllerBase
{
    public const string RateLimitPolicy = "public-contact";

    /// <summary>
    /// ส่งข้อมูลติดต่อ — [SECURITY] จำกัดจำนวนต่อ IP (ดู Program.cs) + honeypot `website`
    /// ส่ง `requestId` เดิมซ้ำได้อย่างปลอดภัย (LINE ไม่ส่งข้อความซ้ำ)
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicy)]
    public async Task<IActionResult> Submit([FromBody] ContactRequest request, CancellationToken ct)
    {
        var result = await service.SubmitAsync(request, ct);
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "CONTACT_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            "CONTACT_SEND_FAILED" => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status422UnprocessableEntity,
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
