using System.Text;
using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>จัดการลูกค้าใน Garage DB พร้อมกรองสิทธิ์ตามสาขาและ IsCustomerDataPrivate</summary>
[ApiController]
[Route("api/v1/customers")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class CustomersController(ICustomerVehicleService service) : ControllerBase
{
    /// <summary>รายการลูกค้าแบบ filter, sort และ server-side pagination</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] CustomerSearchModel model, CancellationToken ct) =>
        Render(await service.SearchCustomersAsync(model.ToQuery(), ct));

    /// <summary>รายละเอียดลูกค้าพร้อมรถที่เป็นเจ้าของ</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct) =>
        Render(await service.GetCustomerAsync(id, ct));

    /// <summary>เพิ่มลูกค้า; ถ้าชื่อ+นามสกุล+เบอร์ซ้ำจะคืน 409 CUSTOMER_DUPLICATE</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CustomerUpsertRequest request, CancellationToken ct) =>
        Render(await service.CreateCustomerAsync(request, ct), created: true);

    /// <summary>แก้ไขลูกค้าที่มองเห็นได้ตามนโยบายสาขา</summary>
    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] CustomerUpsertRequest request, CancellationToken ct) =>
        Render(await service.UpdateCustomerAsync(id, request, ct));

    /// <summary>ลบลูกค้าแบบ soft-delete (Status=0)</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct) =>
        Render(await service.DeleteCustomerAsync(id, ct));

    /// <summary>ส่งออกรายการตาม filter ปัจจุบันเป็น UTF-8 CSV (สูงสุด 10,000 แถว)</summary>
    [HttpGet("export")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export([FromQuery] CustomerSearchModel model, CancellationToken ct)
    {
        var result = await service.ExportCustomersAsync(model.ToQuery(), ct);
        if (!result.Success) return Render(result);
        var lines = new List<string> { "รหัสลูกค้า,ชื่อ-นามสกุล,เลขบัตรประชาชน,โทรศัพท์ 1,โทรศัพท์ 2,อีเมล,จังหวัด,Blacklist,สถานะ,จำนวนรถ" };
        lines.AddRange(result.Data!.Select(x => string.Join(',',
            Csv(x.Code), Csv(x.FullName), Csv(x.IdCard), Csv(x.PhoneNumber1), Csv(x.PhoneNumber2),
            Csv(x.Email), Csv(x.ProvinceName), Csv(x.IsBlacklist ? x.BlacklistRemark ?? "ใช่" : "ไม่"),
            Csv(x.IsDeleted ? "ลบแล้ว" : "ใช้งาน"), x.VehicleCount)));
        return File(Utf8Bom(string.Join("\r\n", lines)), "text/csv; charset=utf-8", $"customers-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
    }

    private IActionResult Render<T>(Result<T> result, bool created = false)
    {
        if (result.Success)
            return created
                ? StatusCode(StatusCodes.Status201Created, Envelope.From(result, HttpContext.TraceIdentifier))
                : Ok(Envelope.From(result, HttpContext.TraceIdentifier));
        var status = result.Error!.Code switch
        {
            "CUSTOMER_NOT_FOUND" => StatusCodes.Status404NotFound,
            "CUSTOMER_DUPLICATE" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }

    private static string Csv(object? value) => $"\"{value?.ToString()?.Replace("\"", "\"\"") ?? string.Empty}\"";
    private static byte[] Utf8Bom(string value) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(value)).ToArray();
}

public sealed class CustomerSearchModel
{
    public string? Keyword { get; set; }
    public int? BrandId { get; set; }
    public int? ModelId { get; set; }
    public int? ProvinceId { get; set; }
    public int? AmphureId { get; set; }
    public int? DistrictId { get; set; }
    public bool IncludeDeleted { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? SortBy { get; set; }
    public CustomerSearchQuery ToQuery() => new(Keyword, BrandId, ModelId, ProvinceId, AmphureId,
        DistrictId, IncludeDeleted, Page, PageSize, SortBy);
}
