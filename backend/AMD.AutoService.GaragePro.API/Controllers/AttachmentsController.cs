using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Attachments;
using AMD.AutoService.GaragePro.Application.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// ไฟล์แนบ — ลายเซ็น รูปรับรถ รูปตรวจเช็ค รูปก่อน/หลัง
/// docs/03-api-contract.md §14
/// </summary>
[ApiController]
[Route("api/v1/attachments")]
[Authorize]
[RequireShiftSession]
public sealed class AttachmentsController(IAttachmentService service) : ControllerBase
{
    /// <summary>อัปโหลดไฟล์แนบ (multipart/form-data)</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] UploadAttachmentForm form, CancellationToken ct)
    {
        if (form.File is null || form.File.Length == 0)
            return StatusCode(StatusCodes.Status422UnprocessableEntity, Envelope.From(
                Result<AttachmentDto>.Fail("ATTACHMENT_EMPTY", "ไม่พบไฟล์ที่อัปโหลด"),
                HttpContext.TraceIdentifier));

        await using var stream = form.File.OpenReadStream();

        var result = await service.UploadAsync(new UploadAttachmentRequest(
            JobId: form.JobId,
            Kind: form.Kind,
            EntityId: form.EntityId,
            FileName: form.File.FileName,
            ContentType: form.File.ContentType,
            Content: stream,
            SizeBytes: form.File.Length), ct);

        return Render(result);
    }

    /// <summary>ไฟล์แนบทั้งหมดของงาน · กรองด้วย kind ได้</summary>
    [HttpGet]
    public async Task<IActionResult> GetForJob(
        [FromQuery] Guid jobId, [FromQuery] string? kind, CancellationToken ct) =>
        Render(await service.GetForJobAsync(jobId, kind, ct));

    /// <summary>เปิดไฟล์จริง — ใช้แสดงรูปลายเซ็นในเอกสาร</summary>
    [HttpGet("file")]
    public async Task<IActionResult> GetFile([FromQuery] string path, CancellationToken ct)
    {
        var result = await service.OpenAsync(path, ct);

        if (!result.Success)
            return StatusCode(
                result.Error!.Code == "ATTACHMENT_OTHER_SCOPE"
                    ? StatusCodes.Status403Forbidden
                    : StatusCodes.Status404NotFound,
                Envelope.From(result, HttpContext.TraceIdentifier));

        var file = result.Data!;
        return PhysicalFile(file.FullPath, file.Meta.ContentType, file.Meta.FileName);
    }

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "ATTACHMENT_NOT_FOUND" or "ATTACHMENT_FILE_MISSING" => StatusCodes.Status404NotFound,
            "ATTACHMENT_OTHER_SCOPE" => StatusCodes.Status403Forbidden,
            "ATTACHMENT_TOO_LARGE" => StatusCodes.Status413PayloadTooLarge,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}

/// <summary>
/// ฟอร์มอัปโหลดไฟล์แนบ
///
/// ต้องรวมทุก field ไว้ใน model เดียว ห้ามใส่ [FromForm] แยกทีละ parameter
/// เพราะ Swashbuckle สร้าง OpenAPI ไม่ได้เมื่อ [FromForm] อยู่คู่กับ IFormFile
/// (อาการ: /swagger/v1/swagger.json คืน 500)
/// </summary>
public sealed class UploadAttachmentForm
{
    public IFormFile? File { get; set; }

    /// <summary>ต้องอยู่สาขาเดียวกับที่เข้าใช้งาน</summary>
    public Guid JobId { get; set; }

    /// <summary>signature | intake | inspection | repair-before | repair-after | qc | document</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>เอกสารที่ไฟล์นี้ผูกอยู่ เช่น QuotationId</summary>
    public Guid? EntityId { get; set; }
}
