using System.Text;
using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>จัดการรถและเจ้าของรถใน Garage DB เฉพาะสาขาจาก JWT</summary>
[ApiController]
[Route("api/v1/vehicles")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class VehiclesController(ICustomerVehicleService service) : ControllerBase
{
    /// <summary>รายการรถเฉพาะสาขาที่เข้าสู่ระบบ พร้อม filter, เลือกช่องค้นหา และ pagination</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] VehicleSearchModel model, CancellationToken ct) =>
        Render(await service.SearchVehiclesAsync(model.ToQuery(), ct));

    /// <summary>รายละเอียดรถและเจ้าของเฉพาะสาขาที่เข้าสู่ระบบ; ต่างสาขาคืน 404</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct) => Render(await service.GetVehicleAsync(id, ct));

    /// <summary>เพิ่มรถและผูกกับลูกค้าในสาขาที่เข้าสู่ระบบเท่านั้น (multipart/form-data, รูปไม่บังคับ)</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] VehicleForm form, CancellationToken ct) =>
        Render(await service.CreateVehicleAsync(form.ToRequest(), await ReadImage(form.Image, ct), ct), created: true);

    /// <summary>แก้ไขรถ เจ้าของ และรูปเฉพาะสาขาที่เข้าสู่ระบบ ไม่รับลูกค้าต่างสาขา</summary>
    [HttpPut("{id:long}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Update(long id, [FromForm] VehicleForm form, CancellationToken ct) =>
        Render(await service.UpdateVehicleAsync(id, form.ToRequest(), await ReadImage(form.Image, ct), ct));

    /// <summary>ลบรถเฉพาะสาขาที่เข้าสู่ระบบแบบ soft-delete (Status=0)</summary>
    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id, CancellationToken ct) => Render(await service.DeleteVehicleAsync(id, ct));

    /// <summary>เปิดรูปรถโดยตรวจสิทธิ์สาขาก่อนทุกครั้ง</summary>
    [HttpGet("{id:long}/image")]
    public async Task<IActionResult> Image(long id, CancellationToken ct)
    {
        var result = await service.OpenVehicleImageAsync(id, ct);
        if (!result.Success) return Render(result);
        return PhysicalFile(result.Data!.FullPath, result.Data.ContentType, result.Data.FileName);
    }

    /// <summary>ส่งออกรถเฉพาะสาขาที่เข้าสู่ระบบตาม filter เป็น UTF-8 CSV (สูงสุด 10,000 แถว)</summary>
    [HttpGet("export")]
    [Produces("text/csv")]
    public async Task<IActionResult> Export([FromQuery] VehicleSearchModel model, CancellationToken ct)
    {
        var result = await service.ExportVehiclesAsync(model.ToQuery(), ct);
        if (!result.Success) return Render(result);
        var lines = new List<string> { "ทะเบียน,จังหวัด,ยี่ห้อ,รุ่น,โฉม,ชนิด,ปี,สีหลัก,ชื่อลูกค้า,โทรศัพท์,สถานะ" };
        lines.AddRange(result.Data!.Select(x => string.Join(',', Csv(x.Registration), Csv(x.ProvinceName),
            Csv(x.BrandName), Csv(x.ModelName), Csv(x.Nickname), Csv(x.CarTypeName), Csv(x.Year),
            Csv(x.PrimaryColorName), Csv(x.OwnerName), Csv(x.OwnerPhone), Csv(x.IsDeleted ? "ลบแล้ว" : "ใช้งาน"))));
        return File(Utf8Bom(string.Join("\r\n", lines)), "text/csv; charset=utf-8", $"vehicles-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
    }

    private IActionResult Render<T>(Result<T> result, bool created = false)
    {
        if (result.Success)
            return created
                ? StatusCode(StatusCodes.Status201Created, Envelope.From(result, HttpContext.TraceIdentifier))
                : Ok(Envelope.From(result, HttpContext.TraceIdentifier));
        var status = result.Error!.Code switch
        {
            "VEHICLE_NOT_FOUND" or "VEHICLE_IMAGE_NOT_FOUND" or "VEHICLE_CUSTOMER_NOT_FOUND" => StatusCodes.Status404NotFound,
            "VEHICLE_IMAGE_TOO_LARGE" => StatusCodes.Status413PayloadTooLarge,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }

    private static async Task<VehicleImageUpload?> ReadImage(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return null;
        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        return new(file.FileName, file.ContentType, buffer.ToArray());
    }

    private static string Csv(object? value) => $"\"{value?.ToString()?.Replace("\"", "\"\"") ?? string.Empty}\"";
    private static byte[] Utf8Bom(string value) => Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(value)).ToArray();
}

public sealed class VehicleSearchModel
{
    public string? Keyword { get; set; }
    public string[] SearchFields { get; set; } = [];
    public int? BrandId { get; set; }
    public int? ModelId { get; set; }
    public int? NicknameId { get; set; }
    public int? CarTypeId { get; set; }
    public int? YearId { get; set; }
    public int? InsuranceId { get; set; }
    public bool IncludeDeleted { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public string? SortBy { get; set; }
    public VehicleSearchQuery ToQuery() => new(Keyword, SearchFields, BrandId, ModelId, NicknameId,
        CarTypeId, YearId, InsuranceId, IncludeDeleted, Page, PageSize, SortBy);
}

public sealed class VehicleForm
{
    public long CustomerId { get; set; }
    public string Registration { get; set; } = string.Empty;
    public int ProvinceId { get; set; }
    public int BrandId { get; set; }
    public int ModelId { get; set; }
    public int NicknameId { get; set; }
    public int YearId { get; set; }
    public int? PrimaryColorId { get; set; }
    public int? ColorMixId { get; set; }
    public int? GearId { get; set; }
    public int? MachineId { get; set; }
    public int? DriveSystemId { get; set; }
    public string? Vin { get; set; }
    public string? EngineNumber { get; set; }
    public int? InsuranceId { get; set; }
    public DateTime? InsuranceExpiredDate { get; set; }
    public IFormFile? Image { get; set; }
    public VehicleUpsertRequest ToRequest() => new(CustomerId, Registration, ProvinceId, BrandId, ModelId,
        NicknameId, YearId, PrimaryColorId, ColorMixId, GearId, MachineId, DriveSystemId, Vin,
        EngineNumber, InsuranceId, InsuranceExpiredDate);
}
