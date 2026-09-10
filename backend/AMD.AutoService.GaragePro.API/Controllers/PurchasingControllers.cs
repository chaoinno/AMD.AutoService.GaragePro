using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Purchasing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

public abstract class PurchasingApiController : ControllerBase
{
    protected IActionResult Render<T>(Result<T> result, bool created = false) =>
        StatusCode(result.Success ? (created ? 201 : 200) : result.Error!.Code switch
        {
            var code when code.EndsWith("_FORBIDDEN") => 403,
            var code when code.EndsWith("_NOT_FOUND") => 404,
            "PURCHASING_CONFLICT" or "PURCHASING_STATE" or "STOCK_INSUFFICIENT" or "STOCK_OPENING_REQUIRED" => 409,
            _ => 422
        }, Envelope.From(result, HttpContext.TraceIdentifier));
}

[ApiController, Authorize, RequireShiftSession, Produces("application/json")]
[Route("api/v1/purchase-requests")]
public sealed class PurchaseRequestsController(PurchasingService service) : PurchasingApiController
{
    /// <summary>ค้นหาใบขอซื้อ PR ของสาขาที่เข้าสู่ระบบ พร้อมกรองสถานะและแบ่งหน้า</summary>
    /// <param name="q">เลขเอกสารหรือชื่อซัพพลายเออร์</param>
    /// <param name="status">สถานะเอกสาร เช่น draft, pending, approved, cancelled</param>
    /// <param name="page">หน้าที่ต้องการ เริ่มจาก 1</param>
    /// <param name="pageSize">จำนวนต่อหน้า 1–100</param>
    /// <param name="ct">ยกเลิกคำขอ</param>
    [HttpGet]
    public async Task<IActionResult> Search(string? q, string? status, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        Render(await service.SearchAsync("PR", q, status, page, pageSize, ct));

    /// <summary>อ่านรายละเอียดใบขอซื้อ พร้อมรายการสินค้าและ version สำหรับป้องกันแก้ไขทับกัน</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Render(await service.GetAsync("PR", id, ct));

    /// <summary>สร้างใบขอซื้อฉบับร่าง — ออกเลขอัตโนมัติและใช้สาขาจากบัญชีผู้ใช้</summary>
    /// <remarks>จำนวนเป็นจำนวนเต็มบวก ราคาไม่ติดลบ ทศนิยมไม่เกิน 2 ตำแหน่ง ห้ามสินค้าซ้ำ และเลือกได้เฉพาะอะไหล่ที่เปิดใช้งาน ยอดรวมเป็นมูลค่าสินค้าก่อนภาษี</remarks>
    [HttpPost]
    public async Task<IActionResult> Create(PurchaseInput input, CancellationToken ct) => Render(await service.SaveAsync("PR", null, input, ct), true);

    /// <summary>แก้ไขใบขอซื้อเฉพาะฉบับร่าง — ต้องส่ง version จากรายละเอียดล่าสุด</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, PurchaseInput input, CancellationToken ct) => Render(await service.SaveAsync("PR", id, input, ct));

    /// <summary>ส่งเอกสารร่างขออนุมัติ</summary>
    /// <remarks>ส่ง version ปัจจุบันเสมอ การส่งกลับหรือยกเลิกต้องระบุ reason ระบบเก็บผู้ดำเนินการ เวลา และแหล่งที่มา</remarks>
    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, PurchaseActionInput input, CancellationToken ct) =>
        Render(await service.ActionAsync("PR", id, "submit", input, ct));

    /// <summary>ผู้จัดการอนุมัติ PR เพื่อให้แปลงเป็น PO</summary>
    /// <remarks>ส่ง version ปัจจุบันเสมอ การส่งกลับหรือยกเลิกต้องระบุ reason ระบบเก็บผู้ดำเนินการ เวลา และแหล่งที่มา</remarks>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, PurchaseActionInput input, CancellationToken ct) =>
        Render(await service.ActionAsync("PR", id, "approve", input, ct));

    /// <summary>ส่งเอกสารที่รออนุมัติกลับเป็นร่างเพื่อแก้ไข พร้อมเหตุผล</summary>
    /// <remarks>ส่ง version ปัจจุบันเสมอ การส่งกลับหรือยกเลิกต้องระบุ reason ระบบเก็บผู้ดำเนินการ เวลา และแหล่งที่มา</remarks>
    [HttpPost("{id:guid}/return")]
    public async Task<IActionResult> Return(Guid id, PurchaseActionInput input, CancellationToken ct) =>
        Render(await service.ActionAsync("PR", id, "return", input, ct));

    /// <summary>ยกเลิก PR พร้อมเหตุผล (PR ที่แปลงเป็น PO แล้วไม่สามารถยกเลิกได้)</summary>
    /// <remarks>ส่ง version ปัจจุบันเสมอ การส่งกลับหรือยกเลิกต้องระบุ reason ระบบเก็บผู้ดำเนินการ เวลา และแหล่งที่มา</remarks>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, PurchaseActionInput input, CancellationToken ct) =>
        Render(await service.ActionAsync("PR", id, "cancel", input, ct));

    /// <summary>แปลง PR ที่อนุมัติแล้วเป็น PO ฉบับร่าง พร้อมเลือกซัพพลายเออร์</summary>
    /// <remarks>หนึ่ง PR แปลงได้หนึ่ง PO ทั้งรายการและจำนวนตามที่อนุมัติ ระบบป้องกันแปลงซ้ำ และสามารถแก้ราคาซื้อใน PO ฉบับร่าง</remarks>
    [HttpPost("{id:guid}/convert")]
    public async Task<IActionResult> Convert(Guid id, ConvertPurchaseInput input, CancellationToken ct) => Render(await service.ConvertAsync(id, input, ct), true);
}

[ApiController, Authorize, RequireShiftSession, Produces("application/json")]
[Route("api/v1/purchase-orders")]
public sealed class PurchaseOrdersController(PurchasingService service) : PurchasingApiController
{
    /// <summary>ค้นหาใบสั่งซื้อ PO ของสาขาที่เข้าสู่ระบบ พร้อมกรองสถานะและแบ่งหน้า</summary>
    /// <param name="q">เลขเอกสารหรือชื่อซัพพลายเออร์</param>
    /// <param name="status">สถานะเอกสาร เช่น draft, pending, approved, cancelled</param>
    /// <param name="page">หน้าที่ต้องการ เริ่มจาก 1</param>
    /// <param name="pageSize">จำนวนต่อหน้า 1–100</param>
    /// <param name="ct">ยกเลิกคำขอ</param>
    [HttpGet]
    public async Task<IActionResult> Search(string? q, string? status, int page = 1, int pageSize = 25, CancellationToken ct = default) =>
        Render(await service.SearchAsync("PO", q, status, page, pageSize, ct));

    /// <summary>นับจำนวน PO ที่ยังไม่รับครบ/ยังไม่ปิด (ไม่รวม complete และ cancelled) ของสาขาที่เข้าสู่ระบบ — ใช้กับตัวเลขในเมนู</summary>
    [HttpGet("count-open")]
    public async Task<IActionResult> CountOpen(CancellationToken ct) => Render(await service.CountOpenAsync("PO", ct));

    /// <summary>อ่านรายละเอียดใบสั่งซื้อ พร้อมรายการสินค้าและ version สำหรับป้องกันแก้ไขทับกัน</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => Render(await service.GetAsync("PO", id, ct));

    /// <summary>สร้างใบสั่งซื้อฉบับร่าง — ออกเลขอัตโนมัติและใช้สาขาจากบัญชีผู้ใช้</summary>
    /// <remarks>จำนวนเป็นจำนวนเต็มบวก ราคาไม่ติดลบ ทศนิยมไม่เกิน 2 ตำแหน่ง ห้ามสินค้าซ้ำ และเลือกได้เฉพาะอะไหล่ที่เปิดใช้งาน ยอดรวมเป็นมูลค่าสินค้าก่อนภาษี</remarks>
    [HttpPost]
    public async Task<IActionResult> Create(PurchaseInput input, CancellationToken ct) => Render(await service.SaveAsync("PO", null, input, ct), true);

    /// <summary>แก้ไขใบสั่งซื้อเฉพาะฉบับร่าง — ต้องส่ง version จากรายละเอียดล่าสุด</summary>
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, PurchaseInput input, CancellationToken ct) => Render(await service.SaveAsync("PO", id, input, ct));

    /// <summary>ส่งเอกสารร่างขออนุมัติ</summary>
    /// <remarks>ส่ง version ปัจจุบันเสมอ การส่งกลับหรือยกเลิกต้องระบุ reason ระบบเก็บผู้ดำเนินการ เวลา และแหล่งที่มา</remarks>
    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, PurchaseActionInput input, CancellationToken ct) =>
        Render(await service.ActionAsync("PO", id, "submit", input, ct));

    /// <summary>อนุมัติ PO — เกินวงเงินที่กำหนดต้องเป็นผู้จัดการ</summary>
    /// <remarks>ส่ง version ปัจจุบันเสมอ การส่งกลับหรือยกเลิกต้องระบุ reason ระบบเก็บผู้ดำเนินการ เวลา และแหล่งที่มา</remarks>
    [HttpPost("{id:guid}/approve")]
    public async Task<IActionResult> Approve(Guid id, PurchaseActionInput input, CancellationToken ct) =>
        Render(await service.ActionAsync("PO", id, "approve", input, ct));

    /// <summary>ส่งเอกสารที่รออนุมัติกลับเป็นร่างเพื่อแก้ไข พร้อมเหตุผล</summary>
    /// <remarks>ส่ง version ปัจจุบันเสมอ การส่งกลับหรือยกเลิกต้องระบุ reason ระบบเก็บผู้ดำเนินการ เวลา และแหล่งที่มา</remarks>
    [HttpPost("{id:guid}/return")]
    public async Task<IActionResult> Return(Guid id, PurchaseActionInput input, CancellationToken ct) =>
        Render(await service.ActionAsync("PO", id, "return", input, ct));

    /// <summary>ยืนยันส่งสั่งซื้อและเพิ่มยอด OnOrder (ยังไม่เพิ่มยอดพร้อมใช้)</summary>
    /// <remarks>ส่ง version ปัจจุบันเสมอ การส่งกลับหรือยกเลิกต้องระบุ reason ระบบเก็บผู้ดำเนินการ เวลา และแหล่งที่มา</remarks>
    [HttpPost("{id:guid}/send")]
    public async Task<IActionResult> Send(Guid id, PurchaseActionInput input, CancellationToken ct) =>
        Render(await service.ActionAsync("PO", id, "send", input, ct));

    /// <summary>ยกเลิก PO พร้อมเหตุผลและลดยอดรอรับที่เหลือ ของที่รับแล้วคงอยู่</summary>
    /// <remarks>ส่ง version ปัจจุบันเสมอ การส่งกลับหรือยกเลิกต้องระบุ reason ระบบเก็บผู้ดำเนินการ เวลา และแหล่งที่มา</remarks>
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, PurchaseActionInput input, CancellationToken ct) =>
        Render(await service.ActionAsync("PO", id, "cancel", input, ct));

    /// <summary>อ่านวงเงินอนุมัติ PO ของธุรการจัดซื้อ — ยอดเกินนี้ต้องให้ผู้จัดการอนุมัติ</summary>
    [HttpGet("approval-threshold")]
    public async Task<IActionResult> Threshold() => Render(await service.PolicyAsync());

    /// <summary>อ่านประวัติใบรับสินค้า GRN ของ PO พร้อมจำนวนของดี ของชำรุด และราคาที่รับ</summary>
    [HttpGet("{id:guid}/receipts")]
    public async Task<IActionResult> Receipts(Guid id, CancellationToken ct) => Render(await service.ReceiptsAsync(id, ct));

    /// <summary>รับสินค้า GRN บางส่วนหรือทั้งหมด — สร้างล็อต FIFO เฉพาะของดีและแยกของชำรุด</summary>
    /// <remarks>
    /// ต้องส่ง RequestId (UUID) เดิมเมื่อ retry เพื่อไม่เพิ่มสต็อกซ้ำ พร้อมเลขใบส่งของและรายการที่รับจริง
    /// ยอดรับรวมของชำรุดต้องไม่เกินยอดค้างรับ ราคาต่างจาก PO ต้องให้ผู้จัดการยืนยันพร้อมเหตุผล
    /// สินค้าที่มียอดเดิมต้องตั้งยอดยกมาก่อน รับครบรวมของชำรุดจะปิด PO
    /// </remarks>
    [HttpPost("{id:guid}/receipts")]
    public async Task<IActionResult> Receive(Guid id, ReceiptInput input, CancellationToken ct) => Render(await service.ReceiveAsync(id, input, ct), true);
}

[ApiController, Authorize, RequireShiftSession, Produces("application/json")]
[Route("api/v1/inventory")]
public sealed class StockFIFOController(PurchasingService service) : PurchasingApiController
{
    /// <summary>ค้นหาสต็อกอะไหล่ของสาขาปัจจุบัน — พร้อมใช้ = คงเหลือ − จอง (ไม่รวมรอรับและชำรุด)</summary>
    /// <remarks>สูงสุด 200 รายการต่อการค้นหา มูลค่า FIFO แสดงเฉพาะผู้จัดการ สินค้าที่ยังไม่ตั้งยอดยกมาจะไม่มีมูลค่า FIFO</remarks>
    [HttpGet("items")]
    public async Task<IActionResult> Items(string? q, CancellationToken ct) => Render(await service.StockAsync(q, ct));

    /// <summary>รายละเอียดสต็อก ล็อต FIFO เรียงเก่าก่อน และการเคลื่อนไหวล่าสุด 200 รายการ</summary>
    /// <remarks>ยอดก่อน/หลังในประวัติเป็นยอดของดีทั้งสาขา ต้นทุนล็อตและต้นทุนเบิกแสดงเฉพาะผู้จัดการ</remarks>
    [HttpGet("items/{id:guid}")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct) => Render(await service.StockDetailAsync(id, ct));

    /// <summary>ตั้งยอดยกมา FIFO จากจำนวนเดิมของสินค้า — ผู้จัดการยืนยันคลัง ต้นทุน และเหตุผลได้ครั้งเดียว</summary>
    /// <remarks>ส่ง ExpectedOnHand/ExpectedDamaged ตามที่ตรวจนับจากหน้าสินค้า ยอดรวมเดิมไม่เพิ่มซ้ำ สร้างล็อตเฉพาะของดี หลังเริ่มใช้จะล็อกการแก้ยอดโดยตรงในหน้าสินค้า</remarks>
    [HttpPost("opening-balances")]
    public async Task<IActionResult> Opening(StockOpeningInput input, CancellationToken ct) => Render(await service.OpeningAsync(input, ct), true);

    /// <summary>เบิกสินค้าโดยตัดล็อต FIFO เก่าก่อนในคลังที่เลือก พร้อมคำนวณต้นทุนตามล็อตจริง</summary>
    /// <remarks>ระบุ RequestId (UUID) เดิมเมื่อ retry พร้อมจำนวนเต็มบวกและเหตุผล เบิกได้ไม่เกินยอดพร้อมใช้ที่หักยอดจองแล้ว ไม่อนุญาตสต็อกติดลบ บันทึกประวัติแยกแต่ละล็อตภายใต้เลข ISS เดียวกัน</remarks>
    [HttpPost("issues")]
    public async Task<IActionResult> Issue(StockIssueInput input, CancellationToken ct) => Render(await service.IssueAsync(input, ct), true);

    /// <summary>สร้างใบเบิกสินค้าหลายรายการในเอกสารเดียว ระบุผู้เบิก (พนักงาน) และผูกกับงาน (job) ได้</summary>
    /// <remarks>ระบุ RequestId (UUID) เดิมเมื่อ retry พร้อมรายการสินค้า 1–100 รายการ เหตุผล และผู้เบิก เบิกได้ไม่เกินยอดพร้อมใช้ต่อสินค้า ออกเลขใบเบิก WD เดียวกันทุกบรรทัด</remarks>
    [HttpPost("withdrawals")]
    public async Task<IActionResult> Withdraw(StockWithdrawalInput input, CancellationToken ct) => Render(await service.WithdrawAsync(input, ct), true);

    /// <summary>อ่านใบเบิกสินค้าที่สร้างแล้วด้วยเลข operation เพื่อแสดง/พิมพ์ซ้ำ</summary>
    [HttpGet("withdrawals/{operationId:guid}")]
    public async Task<IActionResult> WithdrawalDetail(Guid operationId, CancellationToken ct) => Render(await service.WithdrawalDetailAsync(operationId, ct));

    /// <summary>รายการใบเบิกสินค้าที่ผูกกับ job นี้ ล่าสุดก่อน</summary>
    [HttpGet("withdrawals/by-job/{jobId:guid}")]
    public async Task<IActionResult> WithdrawalsByJob(Guid jobId, CancellationToken ct) => Render(await service.WithdrawalsByJobAsync(jobId, ct));
}

