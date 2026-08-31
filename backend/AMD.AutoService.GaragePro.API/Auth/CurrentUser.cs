using System.Security.Claims;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// บริบทผู้ใช้ของ request ปัจจุบัน — อ่านจาก JWT claim ทั้งหมด
///
/// [SECURITY] เดิม P0 อ่านจาก header X-User-* ซึ่งปลอมได้และ audit log เชื่อถือไม่ได้
/// ตอนนี้ทุกค่ามาจาก token ที่เซ็นแล้ว client แก้ไม่ได้
/// เหลือ X-Client-Source เป็น header เพราะเป็นข้อมูลบริบท ไม่ใช่ตัวตน
/// </summary>
public sealed class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    private IHeaderDictionary Headers => accessor.HttpContext?.Request.Headers
        ?? throw new InvalidOperationException("ไม่มี HttpContext");

    public long UserId =>
        long.TryParse(Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? Principal?.FindFirstValue("sub"), out var v) ? v : 0;

    public string UserName =>
        Principal?.FindFirstValue(GarageClaims.DisplayName)
        ?? Principal?.FindFirstValue(ClaimTypes.Name)
        ?? "ไม่ระบุชื่อ";

    public UserRole Role =>
        Enum.TryParse<UserRole>(Principal?.FindFirstValue(ClaimTypes.Role), ignoreCase: true, out var r)
            ? r
            : UserRole.FrontDesk;

    /// <summary>[BIZ] Legacy Id ไม่ unique ข้าม shard — ต้องพกไปทุก query (docs/05 §1)</summary>
    public string ShardKey =>
        Principal?.FindFirstValue(GarageClaims.ShardKey) is { Length: > 0 } shard ? shard : "db2";

    public int BranchId =>
        int.TryParse(Principal?.FindFirstValue(GarageClaims.BranchId), out var v) ? v : 0;

    public Guid? SessionId =>
        Guid.TryParse(Principal?.FindFirstValue(GarageClaims.SessionId), out var v) ? v : null;

    public bool IsAdministrator =>
        bool.TryParse(Principal?.FindFirstValue(GarageClaims.IsAdministrator), out var value) && value;

    /// <summary>[BIZ] ทุก ActivityEvent ต้องรู้ว่ามาจากมือถือหรือเว็บ</summary>
    public EventSource Source =>
        Headers["X-Client-Source"].FirstOrDefault()?.ToLowerInvariant() switch
        {
            "mobile" => EventSource.Mobile,
            "system" => EventSource.System,
            _ => EventSource.Web
        };
}
