using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Reports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>รายงานสำหรับผู้จัดการ/ธุรการ — อ่านอย่างเดียว ไม่มีเอนทิตีใหม่ (ยกเว้นประวัติรถที่เปิดทุกบทบาท)</summary>
[ApiController, Authorize, RequireShiftSession, Produces("application/json")]
[Route("api/v1/reports")]
public sealed class ReportsController(ReportsService service) : ControllerBase
{
    private IActionResult Render<T>(Result<T> result) =>
        StatusCode(result.Success ? 200
            : result.Error!.Code.EndsWith("_FORBIDDEN") ? 403
            : result.Error.Code.EndsWith("_NOT_FOUND") ? 404 : 422,
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

    /// <summary>ขายหน้าร้าน: ยอดขาย บิล สินค้าขายดี ช่องทางชำระ โปรโมชัน และบิลที่ยกเลิก — ต้นทุน/กำไร strip ตาม role</summary>
    /// <param name="fromDate">วันที่เริ่ม (ปฏิทินไทย yyyy-MM-dd รวมวันนี้) — ค่าเริ่มต้นวันที่ 1 ของเดือน</param>
    /// <param name="toDate">วันที่สิ้นสุด (ปฏิทินไทย yyyy-MM-dd รวมวันนี้) — ค่าเริ่มต้นวันนี้ · ช่วงยาวสุด 366 วัน</param>
    [HttpGet("retail-sales")]
    public async Task<IActionResult> RetailSales(DateOnly? fromDate, DateOnly? toDate, CancellationToken ct) =>
        Render(await service.GetRetailSalesAsync(fromDate, toDate, ct));

    /// <summary>มูลค่าสต็อก อายุสต็อกคงเหลือ (FIFO) และสินค้าเสียหาย</summary>
    [HttpGet("stock")]
    public async Task<IActionResult> Stock(CancellationToken ct) => Render(await service.GetStockAsync(ct));

    /// <summary>ค้นรถจากทะเบียนหรือเบอร์โทร (ตัดช่องว่าง/ขีดก่อนเทียบ) — ทุกบทบาท · เฉพาะงานในระบบนี้ของสาขาปัจจุบัน</summary>
    /// <param name="q">ทะเบียนรถหรือเบอร์โทร อย่างน้อย 3 ตัวอักษร</param>
    [HttpGet("vehicle-history/search")]
    public async Task<IActionResult> SearchVehicleHistory(string? q, CancellationToken ct) =>
        Render(await service.SearchVehicleHistoryAsync(q, ct));

    /// <summary>ประวัติงานของรถหนึ่งคัน (ใหม่สุดก่อน) — ทุกบทบาท · ช่าง/หัวหน้าช่างไม่เห็นยอดเงิน</summary>
    [HttpGet("vehicle-history/{vehicleId:long}")]
    public async Task<IActionResult> VehicleHistory(long vehicleId, CancellationToken ct) =>
        Render(await service.GetVehicleHistoryAsync(vehicleId, ct));

    /// <summary>รถใกล้ครบรอบบริการ (จากนัดครั้งถัดไปในใบส่งมอบ) — ผู้จัดการ/ธุรการ</summary>
    /// <param name="fromDate">วันนัดตั้งแต่ (ปฏิทินไทย yyyy-MM-dd) — ค่าเริ่มต้น 30 วันก่อน</param>
    /// <param name="toDate">วันนัดถึง (รวมวันนั้น) — ค่าเริ่มต้นอีก 30 วัน · ช่วงยาวสุด 366 วัน</param>
    [HttpGet("service-due")]
    public async Task<IActionResult> ServiceDue(DateOnly? fromDate, DateOnly? toDate, CancellationToken ct) =>
        Render(await service.GetServiceDueAsync(fromDate, toDate, ct));
}
