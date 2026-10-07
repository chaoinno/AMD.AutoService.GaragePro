using System.Linq.Expressions;
using System.Text.RegularExpressions;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Notifications;

/// <summary>ผู้ที่กำลังดูแจ้งเตือน — มาจาก JWT ทั้งหมด ไม่รับจาก request</summary>
public sealed record NotificationViewer(string ShardKey, int BranchId, long StaffId, UserRole Role);

public static partial class NotificationRules
{
    /// <summary>
    /// แสดงย้อนหลังเท่านี้ — ระบบไม่มี background worker จึงยังไม่ลบแถวเก่า (บันทึกเป็นงานค้าง)
    /// การจำกัดช่วงยังกันไม่ให้คนที่เพิ่งได้บทบาทใหม่เจอแจ้งเตือนกลุ่มเก่าย้อนไปหลายเดือน
    /// </summary>
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(30);

    /// <summary>
    /// [SECURITY] แหล่งเดียวของ "ใครเห็นอะไร" — ใช้ทั้ง repository จริง (แปลงเป็น SQL) และ fake ในเทสต์
    /// · สาขาตาม claim · ของตัวเอง หรือ กลุ่มบทบาทที่ตรงกับ role ปัจจุบันโดยไม่ใช่เรื่องที่ตัวเองเป็นคนทำ
    /// </summary>
    public static Expression<Func<Notification, bool>> VisibleTo(NotificationViewer viewer, DateTime since)
    {
        var shard = viewer.ShardKey;
        var branch = viewer.BranchId;
        var staff = viewer.StaffId;
        var bit = NotificationAudience.Bit(viewer.Role);
        return n => n.LegacyShardKey == shard && n.LegacyBranchId == branch && n.CreatedAt >= since
            && (n.RecipientStaffId == staff
                || ((n.AudienceRoles & bit) != 0 && n.ActorStaffId != staff));
    }

    /// <summary>token mention <c>@[41:สมชาย]</c> → <c>@สมชาย</c> — ต้องตรงกับ MENTION_PATTERN ใน web mentionToken.ts</summary>
    public static string PlainText(string body) => MentionToken().Replace(body, "@$2");

    public static string Truncate(string text, int max)
    {
        var flat = Whitespace().Replace(text, " ").Trim();
        return flat.Length <= max ? flat : flat[..(max - 1)].TrimEnd() + "…";
    }

    [GeneratedRegex(@"@\[(\d+):([^\[\]]+)\]")]
    private static partial Regex MentionToken();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
