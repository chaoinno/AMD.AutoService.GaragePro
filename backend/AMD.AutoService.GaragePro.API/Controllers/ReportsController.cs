using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>รายงานสำหรับผู้จัดการ/ธุรการ — อ่านอย่างเดียว ไม่มีเอนทิตีใหม่</summary>
[ApiController, Authorize, RequireShiftSession, Produces("application/json")]
[Route("api/v1/reports")]
public sealed class ReportsController(ReportsService service) : ControllerBase
{
    private IActionResult Render<T>(Result<T> result) =>
        StatusCode(result.Success ? 200 : result.Error!.Code.EndsWith("_FORBIDDEN") ? 403 : 422,
            Envelope.From(result, HttpContext.TraceIdentifier));

    /// <summary>ภาพรวมวันนี้: จ๊อบแยกตามสถานะ จ๊อบเกินนัด ยอดรับชำระวันนี้ งานรอ QC/รอชำระเงิน</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(CancellationToken ct) => Render(await service.GetDashboardAsync(ct));

    /// <summary>รอบเวลาต่อขั้นตอนงาน — ค่าเฉลี่ยเวลาต่อสถานะและจ๊อบที่ค้างนานที่สุด</summary>
    /// <param name="fromDate">เริ่มช่วง (UTC) — ค่าเริ่มต้นย้อนหลัง 30 วัน</param>
    /// <param name="toDate">สิ้นสุดช่วง (UTC) — ค่าเริ่มต้นปัจจุบัน</param>
    [HttpGet("cycle-time")]
    public async Task<IActionResult> CycleTime(DateTime? fromDate, DateTime? toDate, CancellationToken ct) =>
        Render(await service.GetCycleTimeAsync(fromDate, toDate, ct));

    /// <summary>ยอดขาย-ต้นทุน-กำไรจากบรรทัดที่ลูกค้าอนุมัติ — ต้นทุน/กำไร strip ตาม role</summary>
    /// <param name="fromDate">เริ่มช่วง (UTC) — ค่าเริ่มต้นวันที่ 1 ของเดือนปัจจุบัน</param>
    /// <param name="toDate">สิ้นสุดช่วง (UTC) — ค่าเริ่มต้นปัจจุบัน</param>
    [HttpGet("sales-margin")]
    public async Task<IActionResult> SalesMargin(DateTime? fromDate, DateTime? toDate, CancellationToken ct) =>
        Render(await service.GetSalesMarginAsync(fromDate, toDate, ct));

    /// <summary>มูลค่าสต็อก อายุสต็อกคงเหลือ (FIFO) และสินค้าเสียหาย</summary>
    [HttpGet("stock")]
    public async Task<IActionResult> Stock(CancellationToken ct) => Render(await service.GetStockAsync(ct));
}
