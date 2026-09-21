using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.JobChat;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// แชทผูกกับ job — widget มุมล่างขวาของ Job Card · ข้อความ/รูป/reply/mention
/// รูปภาพเป็นไฟล์แนบปกติ (อัปโหลดผ่าน /api/v1/attachments kind="chat" ก่อน แล้วส่ง attachmentId มาผูกตอนสร้างข้อความ)
/// </summary>
[ApiController]
[Route("api/v1/jobs/{jobId:guid}/chat")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class JobChatController(IJobChatService service) : ControllerBase
{
    /// <summary>
    /// ข้อความล่าสุดของหลายจ๊อบในคำขอเดียว — ใช้แสดงจุด "มีข้อความใหม่" บนการ์ดจ๊อบในคิวงาน
    /// โดยไม่ต้องยิงทีละคัน · เส้นทางเต็มเพราะ controller นี้ผูกกับ {jobId} อยู่
    /// จ๊อบที่ยังไม่มีข้อความหรืออยู่นอกสาขาจะไม่อยู่ในผลลัพธ์
    /// </summary>
    [HttpGet("/api/v1/jobs/chat/latest")]
    public async Task<IActionResult> GetLatestPerJob([FromQuery] Guid[] jobIds, CancellationToken ct) =>
        Render(await service.GetLatestPerJobAsync(jobIds ?? [], ct));

    /// <summary>
    /// ไม่ส่ง cursor เลย = หน้าล่าสุด · beforeAt/beforeId = โหลดข้อความเก่ากว่า (เลื่อนขึ้น)
    /// afterAt/afterId = ข้อความใหม่กว่า (poll ต่อ) — ส่งได้แค่ทิศทางเดียวต่อครั้ง
    /// </summary>
    [HttpGet("messages")]
    public async Task<IActionResult> GetMessages(
        Guid jobId,
        [FromQuery] DateTime? beforeAt, [FromQuery] Guid? beforeId,
        [FromQuery] DateTime? afterAt, [FromQuery] Guid? afterId,
        [FromQuery] int take,
        CancellationToken ct) =>
        Render(await service.GetPageAsync(
            jobId, new JobChatPageQuery(beforeAt, beforeId, afterAt, afterId, take == 0 ? 50 : take), ct));

    /// <summary>ส่งข้อความ — body และ/หรือ attachmentIds อย่างน้อยหนึ่งอย่าง</summary>
    [HttpPost("messages")]
    public async Task<IActionResult> Send(
        Guid jobId, [FromBody] SendJobChatMessageRequest request, CancellationToken ct) =>
        Render(await service.SendAsync(jobId, request, ct));

    /// <summary>ลบข้อความ (soft delete) — เจ้าของข้อความเท่านั้น</summary>
    [HttpDelete("messages/{messageId:guid}")]
    public async Task<IActionResult> Delete(Guid jobId, Guid messageId, CancellationToken ct) =>
        Render(await service.DeleteAsync(jobId, messageId, ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "JOB_NOT_FOUND" or "CHAT_MESSAGE_NOT_FOUND" or "CHAT_REPLY_NOT_FOUND" => StatusCodes.Status404NotFound,
            "JOB_OTHER_BRANCH" or "CHAT_FORBIDDEN" => StatusCodes.Status403Forbidden,
            "CHAT_MESSAGE_EMPTY" or "CHAT_MENTION_STAFF_INVALID" or "CHAT_ATTACHMENT_INVALID"
                => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
