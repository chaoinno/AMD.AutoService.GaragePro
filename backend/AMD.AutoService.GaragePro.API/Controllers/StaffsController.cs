using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Staff;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>จัดการพนักงานและบัญชีผู้ใช้เฉพาะสาขาจาก JWT รวมถึงผู้ดูแลระบบ</summary>
[ApiController]
[Route("api/v1/staffs")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class StaffsController(IStaffService service) : ControllerBase
{
    /// <summary>ค้นหาและแบ่งหน้ารายการพนักงานเฉพาะสาขาที่เข้าสู่ระบบ</summary>
    [HttpGet]
    public async Task<IActionResult> Search([FromQuery] StaffSearchModel model, CancellationToken ct) =>
        Render(await service.SearchAsync(model.ToQuery(), ct));

    /// <summary>รายละเอียดพนักงานในสาขาที่เข้าสู่ระบบ; ต่างสาขาคืน 404</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken ct) => Render(await service.GetAsync(id, ct));

    /// <summary>ตัวอย่างรหัสพนักงานและบัญชีใหม่สำหรับสาขาที่เข้าสู่ระบบเท่านั้น</summary>
    /// <param name="branchId">เว้นว่างเพื่อใช้สาขาจาก JWT; ส่งสาขาอื่นคืน 403 แม้เป็นผู้ดูแลระบบ</param>
    /// <param name="ct">Cancellation token</param>
    [HttpGet("code-preview")]
    public async Task<IActionResult> Preview([FromQuery] int? branchId, CancellationToken ct) =>
        Render(await service.PreviewCodeAsync(branchId, ct));

    /// <summary>เพิ่มพนักงานและบัญชีผู้ใช้ โดยใช้สาขาจาก JWT อัตโนมัติ (รูปไม่บังคับ)</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Create([FromForm] StaffForm form, CancellationToken ct) =>
        Render(await service.CreateAsync(form.ToRequest(), await ReadImage(form.Image, ct), ct), created: true);

    /// <summary>แก้ไขพนักงานในสาขาที่เข้าสู่ระบบเท่านั้น ไม่อนุญาตให้ย้ายสาขา</summary>
    [HttpPut("{id:long}")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Update(long id, [FromForm] StaffForm form, CancellationToken ct) =>
        Render(await service.UpdateAsync(id, form.ToRequest(), await ReadImage(form.Image, ct), ct));

    /// <summary>เปิด/ปิดใช้งานพนักงานและบัญชีเฉพาะสาขาที่เข้าสู่ระบบ</summary>
    [HttpPatch("{id:long}/status")]
    public async Task<IActionResult> Status(long id, [FromBody] StaffStatusRequest request, CancellationToken ct) =>
        Render(await service.SetStatusAsync(id, request, ct));

    /// <summary>เปิดรูปพนักงานโดยตรวจสิทธิ์สาขาจาก JWT; ต่างสาขาคืน 404</summary>
    [HttpGet("{id:long}/image")]
    public async Task<IActionResult> Image(long id, CancellationToken ct)
    {
        var result = await service.OpenImageAsync(id, ct);
        if (!result.Success) return Render(result);
        return PhysicalFile(result.Data!.FullPath, result.Data.ContentType, result.Data.FileName);
    }

    /// <summary>ข้อมูลประกอบฟอร์มพนักงาน; รายการสาขามีเฉพาะสาขาที่เข้าสู่ระบบ</summary>
    [HttpGet("reference-data")]
    public async Task<IActionResult> ReferenceData(CancellationToken ct) => Render(await service.GetReferenceDataAsync(ct));

    private IActionResult Render<T>(Result<T> result, bool created = false)
    {
        if (result.Success)
            return created ? StatusCode(StatusCodes.Status201Created, Envelope.From(result, HttpContext.TraceIdentifier))
                : Ok(Envelope.From(result, HttpContext.TraceIdentifier));
        var status = result.Error!.Code switch
        {
            "STAFF_NOT_FOUND" or "STAFF_IMAGE_NOT_FOUND" => StatusCodes.Status404NotFound,
            "STAFF_BRANCH_FORBIDDEN" or "STAFF_ORGANIZATION_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "STAFF_IMAGE_TOO_LARGE" => StatusCodes.Status413PayloadTooLarge,
            "STAFF_UPDATE_CONFLICT" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity
        };
        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }

    private static async Task<StaffImageUpload?> ReadImage(IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return null;
        await using var input = file.OpenReadStream();
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, ct);
        return new(file.FileName, file.ContentType, buffer.ToArray());
    }
}

public sealed class StaffSearchModel
{
    public string? Keyword { get; set; }
    public int? DepartmentId { get; set; }
    public int? SectorId { get; set; }
    public int? PositionId { get; set; }
    public int? ProvinceId { get; set; }
    public int? AmphureId { get; set; }
    public int? DistrictId { get; set; }
    public bool IncludeInactive { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
    public StaffSearchQuery ToQuery() => new(Keyword,DepartmentId,SectorId,PositionId,ProvinceId,AmphureId,DistrictId,IncludeInactive,Page,PageSize);
}

public sealed class StaffForm
{
    /// <summary>เว้นว่างหรือส่ง 0 เพื่อใช้สาขาจาก JWT; หากส่งค่าต้องตรงกับสาขาที่เข้าสู่ระบบ แม้เป็นผู้ดูแลระบบ</summary>
    public int BranchId { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public int GenderId { get; set; }
    public string? IdCard { get; set; }
    public string? Address1 { get; set; }
    public string? Address2 { get; set; }
    public int? ProvinceId { get; set; }
    public int? AmphureId { get; set; }
    public int? DistrictId { get; set; }
    public string? ZipCode { get; set; }
    public string PhoneNumber1 { get; set; } = string.Empty;
    public string? PhoneNumber2 { get; set; }
    public string? Email { get; set; }
    public string? LineId { get; set; }
    public decimal Salary { get; set; }
    public int? StaffSkillLevelId { get; set; }
    public int ExperienceYear { get; set; }
    public int ExperienceMonth { get; set; }
    public DateTime? StartJobDate { get; set; }
    public DateTime? EndJobDate { get; set; }
    public string? Note { get; set; }
    public int MainSectorId { get; set; }
    public int PositionId { get; set; }
    public int[] AdditionalSectorIds { get; set; } = [];
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public IFormFile? Image { get; set; }
    public StaffUpsertRequest ToRequest() => new(BranchId,FirstName,LastName,GenderId,IdCard,Address1,Address2,ProvinceId,
        AmphureId,DistrictId,ZipCode,PhoneNumber1,PhoneNumber2,Email,LineId,Salary,StaffSkillLevelId,ExperienceYear,
        ExperienceMonth,StartJobDate,EndJobDate,Note,MainSectorId,PositionId,AdditionalSectorIds,UserName,Password);
}
