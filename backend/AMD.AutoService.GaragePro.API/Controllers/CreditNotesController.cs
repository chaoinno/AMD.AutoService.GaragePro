using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Pos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// ใบลดหนี้ CN- (อ้างใบกำกับ IV- หรือใบเสร็จ RC-) · คืนเงิน · รายงาน · ค่าตั้งภาษีของสาขา — docs/20
/// ออกใบลดหนี้/คืนเงิน/แก้ค่าตั้ง = ผู้จัดการเท่านั้น (ตรวจที่ CreditNoteService)
/// </summary>
[ApiController]
[Route("api/v1")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class CreditNotesController(ICreditNoteService service) : ControllerBase
{
    /// <summary>บิลที่อ้างได้ (IV- ถ้ามี ไม่งั้น RC-) · ยอดคงเหลือที่ลดได้ต่อบรรทัด · ใบลดหนี้ที่ออกแล้ว · เหตุผลที่ยังออกไม่ได้</summary>
    [HttpGet("jobs/{jobId:guid}/credit-notes")]
    [RequireFeature(PlanFeature.TaxInvoice)]
    public async Task<IActionResult> GetState(Guid jobId, CancellationToken ct) =>
        Render(await service.GetStateAsync(jobId, ct));

    /// <summary>ออกใบลดหนี้ (ผู้จัดการ) — ต้องเลือกบรรทัดของบิลอย่างน้อย 1 รายการ · RequestId ซ้ำคืนใบเดิม · บันทึกคืนเงินพร้อมกันได้</summary>
    [HttpPost("jobs/{jobId:guid}/credit-notes")]
    [RequireFeature(PlanFeature.TaxInvoice)]
    public async Task<IActionResult> Issue(Guid jobId, [FromBody] IssueCreditNoteRequest request, CancellationToken ct) =>
        Render(await service.IssueAsync(jobId, request, ct));

    /// <summary>บันทึกคืนเงินตามใบลดหนี้ (ผู้จัดการ) — รวมไม่เกินยอดใบลดหนี้ · RequestId ซ้ำไม่บันทึกซ้ำ · ลบไม่ได้</summary>
    [HttpPost("credit-notes/{creditNoteId:guid}/refunds")]
    [RequireFeature(PlanFeature.TaxInvoice)]
    public async Task<IActionResult> Refund(Guid creditNoteId, [FromBody] RecordCreditNoteRefundRequest request, CancellationToken ct) =>
        Render(await service.RecordRefundAsync(creditNoteId, request, ct));

    /// <summary>รายงานใบลดหนี้ของสาขาตามวันที่ออก (เวลาไทย · ค่าเริ่มต้นต้นเดือนถึงวันนี้ · ไม่เกิน 366 วัน) — ผู้จัดการ/ธุรการ</summary>
    [HttpGet("reports/credit-notes")]
    [RequireFeature(PlanFeature.TaxInvoice)]
    public async Task<IActionResult> Report([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        Render(await service.ReportAsync(from, to, ct));

    /// <summary>สถานประกอบการของสาขาสำหรับเอกสารภาษี ("00000" = สำนักงานใหญ่) — ไม่เคยตั้ง = สำนักงานใหญ่</summary>
    [HttpGet("branch-tax-profile")]
    public async Task<IActionResult> GetProfile(CancellationToken ct) => Render(await service.GetProfileAsync(ct));

    /// <summary>แก้สถานประกอบการของสาขา (ผู้จัดการ) — มีผลกับใบกำกับ/ใบลดหนี้ที่ออกหลังจากนี้ ใบเดิมคงค่าเดิม</summary>
    [HttpPut("branch-tax-profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateBranchTaxProfileRequest request, CancellationToken ct) =>
        Render(await service.UpdateProfileAsync(request, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "JOB_NOT_FOUND" or "CREDIT_NOTE_NOT_FOUND" => StatusCodes.Status404NotFound,
            "JOB_OTHER_BRANCH" or "POS_FORBIDDEN" or "CREDIT_NOTE_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "CREDIT_NOTE_CONFLICT" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
