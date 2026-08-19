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
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<IActionResult> Upload(
        [FromForm] IFormFile file,
        [FromForm] long jobId,
        [FromForm] string kind,
        [FromForm] Guid? entityId,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return StatusCode(StatusCodes.Status422UnprocessableEntity, Envelope.From(
                Result<AttachmentDto>.Fail("ATTACHMENT_EMPTY", "ไม่พบไฟล์ที่อัปโหลด"),
                HttpContext.TraceIdentifier));

        await using var stream = file.OpenReadStream();

        var result = await service.UploadAsync(new UploadAttachmentRequest(
            JobId: jobId,
            Kind: kind,
            EntityId: entityId,
            FileName: file.FileName,
            ContentType: file.ContentType,
            Content: stream,
            SizeBytes: file.Length), ct);

        return Render(result);
    }

    /// <summary>ไฟล์แนบทั้งหมดของงาน · กรองด้วย kind ได้</summary>
    [HttpGet]
    public async Task<IActionResult> GetForJob(
        [FromQuery] long jobId, [FromQuery] string? kind, CancellationToken ct) =>
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
