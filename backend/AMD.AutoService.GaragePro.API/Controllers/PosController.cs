using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Pos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// ชำระเงิน/ใบเสร็จ — MVP: บันทึกยอดเดียวต่อครั้ง ไม่มี split/EDC/QR gateway จริง ไม่มีใบกำกับภาษี/reprint/void
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class PosController(IPosService service) : ControllerBase
{
    /// <summary>ยอดสรุป: ยอดรวม/ชำระแล้ว/คงเหลือ + รายการชำระเงิน + ใบเสร็จถ้าออกแล้ว</summary>
    [HttpGet("jobs/{jobId:guid}/payment-summary")]
    public async Task<IActionResult> GetSummary(Guid jobId, CancellationToken ct) =>
        Render(await service.GetSummaryAsync(jobId, ct));

    /// <summary>บันทึกการชำระเงิน 1 รายการ — ต้องมี RequestId กันบันทึกซ้ำเมื่อ retry</summary>
    [HttpPost("jobs/{jobId:guid}/payments")]
    public async Task<IActionResult> RecordPayment(
        Guid jobId, [FromBody] RecordPaymentRequest request, CancellationToken ct) =>
        Render(await service.RecordPaymentAsync(jobId, request, ct));

    /// <summary>ลบรายการชำระเงินที่บันทึกผิด — ทำได้เฉพาะก่อนออกใบเสร็จเท่านั้น</summary>
    [HttpDelete("jobs/{jobId:guid}/payments/{paymentId:guid}")]
    public async Task<IActionResult> RemovePayment(
        Guid jobId, Guid paymentId, [FromQuery] string? reason, CancellationToken ct) =>
        Render(await service.RemovePaymentAsync(jobId, paymentId, reason, ct));

    /// <summary>ออกใบเสร็จ — ต้องยอดคงเหลือเป็นศูนย์ก่อน · ออกได้ใบเดียวต่องาน (เรียกซ้ำคืนใบเดิม)</summary>
    [HttpPost("jobs/{jobId:guid}/receipt")]
    public async Task<IActionResult> IssueReceipt(Guid jobId, CancellationToken ct) =>
        Render(await service.IssueReceiptAsync(jobId, ct));

    /// <summary>เปลี่ยนว่างานนี้คิด VAT หรือไม่ — ล็อกทันทีที่เริ่มบันทึกชำระเงิน/ออกใบเสร็จแล้ว</summary>
    [HttpPut("jobs/{jobId:guid}/payment-vat")]
    public async Task<IActionResult> SetVatIncluded(
        Guid jobId, [FromBody] SetVatIncludedRequest request, CancellationToken ct) =>
        Render(await service.SetVatIncludedAsync(jobId, request.Included, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "JOB_NOT_FOUND" or "POS_PAYMENT_NOT_FOUND" => StatusCodes.Status404NotFound,
            "JOB_OTHER_BRANCH" or "POS_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "POS_RECEIPT_ISSUED" or "POS_VAT_LOCKED" => StatusCodes.Status409Conflict,
            "POS_VALIDATION" or "POS_NO_QUOTATION" or "POS_BALANCE_NOT_SETTLED" or "POS_CONFLICT"
                => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
