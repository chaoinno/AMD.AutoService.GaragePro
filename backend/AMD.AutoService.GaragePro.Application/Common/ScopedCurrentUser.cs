using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Common;

/// <summary>
/// <see cref="ICurrentUser"/> ที่กำหนดค่าเอง สำหรับโค้ดที่ไม่ได้รันใน HTTP request
/// (signup provisioning · hosted job · CLI spadmin · ตัวสร้างข้อมูลตัวอย่าง · เทสต์)
/// · CurrentUser ของ API อ่านจาก HttpContext และโยน exception เมื่อไม่มี (API/Auth/CurrentUser.cs) จึงใช้ที่นั่นไม่ได้
/// · ใช้คู่กับ <see cref="IShardScopeFactory.RunAsAsync{T}"/> ซึ่งตั้ง <see cref="CurrentUserOverride"/> ให้ทั้ง DI scope
/// · <see cref="Source"/> เริ่มต้นเป็น System — [BIZ] JobStateMachine ไม่อนุญาต transition จาก System
///   ตัวสร้างข้อมูลตัวอย่าง (S3) ต้องส่ง <see cref="EventSource.Web"/> มาเอง
/// · [SECURITY] IsAdministrator เป็น false เสมอ — sp1 ห้ามมี admin (admin เห็น/เปิดกะได้ทุกสาขาใน shard)
/// </summary>
public sealed class ScopedCurrentUser : ICurrentUser
{
    public ScopedCurrentUser(
        string shardKey,
        int branchId,
        long userId,
        string userName,
        UserRole role = UserRole.Manager,
        EventSource source = EventSource.System,
        long? staffId = null,
        PlanKind? plan = null,
        DateTime? planExpiresAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardKey);
        ArgumentNullException.ThrowIfNull(userName);

        ShardKey = shardKey;
        BranchId = branchId;
        UserId = userId;
        UserName = userName;
        Role = role;
        Source = source;
        StaffId = staffId;
        Plan = plan;
        PlanExpiresAt = planExpiresAt;
    }

    public long UserId { get; }
    public string UserName { get; }
    public UserRole Role { get; }
    public string ShardKey { get; }
    public int BranchId { get; }
    public EventSource Source { get; }
    public bool IsAdministrator => false;
    public Guid? SessionId => null;
    public long? StaffId { get; }
    public PlanKind? Plan { get; }
    public DateTime? PlanExpiresAt { get; }

    /// <summary>กฎเดียวกับ default member ของ ICurrentUser — ประกาศซ้ำเพื่อให้เรียกผ่านชนิดนี้ตรงๆ ได้</summary>
    public bool CanSeeCost => Role == UserRole.Manager;
}
