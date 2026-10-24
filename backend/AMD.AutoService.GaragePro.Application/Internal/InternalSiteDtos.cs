namespace AMD.AutoService.GaragePro.Application.Internal;

// สัญญา JSON ของ GET /api/v1/internal/sites (docs/17 §8.6) · GaragePro Admin อ่านตามชื่อฟิลด์เหล่านี้ ห้ามเปลี่ยนชื่อ
// · camelCase · ฟิลด์ null ไม่อยู่ใน JSON (WhenWritingNull) · เวลาเป็น UTC ลงท้าย Z · ...Date เป็น "yyyy-MM-dd" ตามปฏิทินไทย

public static class InternalSiteSources
{
    public const string SelfServe = "selfserve";
    public const string GaragePro = "garagepro";
}

/// <summary>ค่าของ <see cref="InternalSiteDto.Usage"/> · คำนวณที่ ServicePro ด้วย PlanPolicy (Admin ห้ามคำนวณเอง)</summary>
public static class InternalSiteUsage
{
    public const string Normal = "normal";
    public const string ReadOnly = "read_only";
    public const string Locked = "locked";
    public const string Provisioning = "provisioning";
    public const string Purged = "purged";

    /// <summary>สาขา GaragePro (db2): ServicePro ไม่บังคับวันหมด (docs/12 ข้อ 12)</summary>
    public const string NotEnforced = "not_enforced";
}

/// <summary>
/// หนึ่ง site · คีย์ของแถว = (<see cref="ShardKey"/>, <see cref="BranchId"/>) เพราะ id สาขาชนกันข้ามฐาน
/// · <see cref="DaysLeft"/> = วันสุดท้ายที่ใช้ได้ (EndPackageDate − 1 tick ตามปฏิทินไทย) − วันนี้ตามปฏิทินไทย
///   (PlanAccess.DaysLeft ค่าเดียวกับ /auth/me) · 0 = วันนี้วันสุดท้าย · ติดลบ = เลยมาแล้วกี่วัน · ไม่มีวันหมด = null
/// · <see cref="WorkspaceStatus"/>/<see cref="PendingPayments"/>/<see cref="LatestPaymentAt"/>/<see cref="ExtensionRequestedAt"/>
///   เฉพาะ selfserve
/// </summary>
public sealed record InternalSiteDto(
    string Source,
    string ShardKey,
    int BranchId,
    string Code,
    string Name,
    string? Phone,
    string Plan,
    string? WorkspaceStatus,
    bool? BranchActive,
    DateTime? StartsAt,
    DateTime? EndsAt,
    DateOnly? LastUsableDate,
    int? DaysLeft,
    string Usage,
    int? PendingPayments,
    DateTime? LatestPaymentAt,
    DateTime? ExtensionRequestedAt,
    int JobsLast30Days,
    DateTime? LastJobAt,
    DateTime? CreatedAt);

public sealed record InternalSitesDto(DateTime GeneratedAt, IReadOnlyList<InternalSiteDto> Sites);
