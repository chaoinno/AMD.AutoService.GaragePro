using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Application.Common;

/// <summary>
/// Staff.Id ของผู้ใช้ปัจจุบัน — claim <c>staff</c> เป็นหลัก · fallback ไปอ่าน legacy เฉพาะ token ที่ออกก่อนจะมี claim นี้
/// (ICurrentUser.StaffId เป็น default member ที่คืน null) · null = ระบุไม่ได้ ผู้เรียกตัดสินเองว่าจะทำอย่างไร
/// </summary>
public static class CurrentStaff
{
    public static async Task<long?> ResolveAsync(
        ICurrentUser user, ILegacyUserReader legacyUsers, CancellationToken ct = default)
    {
        if (user.StaffId is long fromClaim && fromClaim > 0) return fromClaim;

        var legacy = await legacyUsers.FindByIdAsync(user.ShardKey, user.UserId, ct);
        return legacy?.StaffId is long fromLegacy && fromLegacy > 0 ? fromLegacy : null;
    }
}
