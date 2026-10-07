using AMD.AutoService.GaragePro.API.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AMD.AutoService.GaragePro.API.Controllers;

/// <summary>
/// กล่องแจ้งเตือนของผู้ใช้ปัจจุบัน (กระดิ่งบนเว็บ) — ผู้รับ/สาขา/บทบาทมาจาก JWT ทั้งหมด
/// client poll <c>unread-count</c> เป็นระยะ (ยังไม่มี realtime) แล้วดึงรายการตอนเปิดกล่อง
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Produces("application/json")]
[Authorize]
[RequireShiftSession]
public sealed class NotificationsController(INotificationService service) : ControllerBase
{
    /// <summary>ใหม่ → เก่า ย้อนหลัง 30 วัน · beforeAt/beforeId = หน้าถัดไป (ส่งคู่กันเสมอ)</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool unreadOnly, [FromQuery] DateTime? beforeAt, [FromQuery] Guid? beforeId,
        [FromQuery] int take, CancellationToken ct) =>
        Render(await service.ListAsync(unreadOnly, beforeAt, beforeId, take, ct));

    /// <summary>จำนวนที่ยังไม่อ่าน (ไม่นับเรื่องที่มีคนดำเนินการแล้ว)</summary>
    [HttpGet("unread-count")]
    public async Task<IActionResult> UnreadCount(CancellationToken ct) =>
        Render(await service.UnreadCountAsync(ct));

    /// <summary>ทำเครื่องหมายว่าอ่านแล้ว — ทำซ้ำได้ · คืนจำนวนที่ยังไม่อ่านล่าสุด</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct) =>
        Render(await service.MarkReadAsync(id, ct));

    /// <summary>กลับเป็นยังไม่อ่าน — ใช้เก็บเรื่องที่ยังต้องกลับมาทำ</summary>
    [HttpDelete("{id:guid}/read")]
    public async Task<IActionResult> MarkUnread(Guid id, CancellationToken ct) =>
        Render(await service.MarkUnreadAsync(id, ct));

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct) =>
        Render(await service.MarkAllReadAsync(ct));

    private IActionResult Render<T>(Result<T> result)
    {
        if (result.Success) return Ok(Envelope.From(result, HttpContext.TraceIdentifier));

        var status = result.Error!.Code switch
        {
            "NOTIFICATION_NOT_FOUND" => StatusCodes.Status404NotFound,
            "NOTIFICATION_NO_STAFF_PROFILE" => StatusCodes.Status403Forbidden,
            "NOTIFICATION_VALIDATION" => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status422UnprocessableEntity
        };

        return StatusCode(status, Envelope.From(result, HttpContext.TraceIdentifier));
    }
}
