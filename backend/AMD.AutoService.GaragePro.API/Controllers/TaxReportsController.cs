using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Pos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// รายงานภาษีขาย/ภาษีซื้อ + บันทึกใบกำกับภาษีซื้อของผู้ขาย — docs/20 §3–4 · ผู้จัดการ/ธุรการ (ตรวจที่ TaxReportService)
/// เดือนภาษีตามเวลาไทย · ไม่ส่ง year/month = เดือนปัจจุบัน
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class TaxReportsController(ITaxReportService service) : ControllerBase
{
    /// <summary>รายงานภาษีขาย: ส่วนที่ 1 ใบกำกับ IV- + ใบลดหนี้ที่อ้าง IV- (ติดลบ) · ส่วนที่ 2 รับเงินที่คิด VAT แต่ยังไม่ออกใบกำกับ (RC-/SL-/ใบลดหนี้ที่อ้าง RC-)</summary>
    [HttpGet("reports/output-vat")]
    [RequireFeature(PlanFeature.TaxInvoice)]
    public async Task<IActionResult> OutputVat([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct) =>
        Render(await service.OutputVatAsync(year, month, ct));

    /// <summary>รายงานภาษีซื้อตามเดือนที่ใช้สิทธิ์ + ใบที่ยกเลิก + ใบรับสินค้าที่มี VAT ที่ยังไม่บันทึกใบกำกับ (ย้อนหลัง 6 เดือน)</summary>
    [HttpGet("reports/input-vat")]
    [RequireFeature(PlanFeature.Purchasing)]
    public async Task<IActionResult> InputVat([FromQuery] int? year, [FromQuery] int? month, CancellationToken ct) =>
        Render(await service.InputVatAsync(year, month, ct));

    /// <summary>ยอดเติมจากใบรับสินค้า (ผู้ขาย · ยอดก่อนภาษี · VAT ตาม PO) — แก้ได้ก่อนบันทึก</summary>
    [HttpGet("purchase-tax-invoices/prefill")]
    [RequireFeature(PlanFeature.Purchasing)]
    public async Task<IActionResult> Prefill([FromQuery] Guid goodsReceiptId, CancellationToken ct) =>
        Render(await service.PrefillFromGoodsReceiptAsync(goodsReceiptId, ct));

    /// <summary>บันทึกใบกำกับภาษีซื้อ — เลขผู้เสียภาษีผู้ขาย + เลขที่ใบกำกับซ้ำไม่ได้ · RequestId ซ้ำคืนใบเดิม</summary>
    [HttpPost("purchase-tax-invoices")]
    [RequireFeature(PlanFeature.Purchasing)]
    public async Task<IActionResult> Record([FromBody] RecordPurchaseTaxInvoiceRequest request, CancellationToken ct) =>
        Render(await service.RecordPurchaseInvoiceAsync(request, ct));

    /// <summary>ยกเลิกใบกำกับภาษีซื้อพร้อมเหตุผล (ไม่ลบ) — ยกเลิกแล้วบันทึกเลขเดิมใหม่ได้</summary>
    [HttpPost("purchase-tax-invoices/{id:guid}/void")]
    [RequireFeature(PlanFeature.Purchasing)]
    public async Task<IActionResult> Void(Guid id, [FromBody] VoidPurchaseTaxInvoiceRequest request, CancellationToken ct) =>
        Render(await service.VoidPurchaseInvoiceAsync(id, request, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "PURCHASE_TAX_INVOICE_NOT_FOUND" or "GOODS_RECEIPT_NOT_FOUND" or "SUPPLIER_NOT_FOUND" => StatusCodes.Status404NotFound,
            "TAX_REPORT_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "PURCHASE_TAX_INVOICE_DUPLICATE" or "PURCHASE_TAX_INVOICE_CONFLICT" or "PURCHASE_TAX_INVOICE_VOIDED" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
