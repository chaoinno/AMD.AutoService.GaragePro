namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>ใช้ไปแล้ว/สูงสุด — <see cref="Max"/> null = ไม่จำกัด (ถูกตัดจาก JSON เพราะ WhenWritingNull)</summary>
public sealed record SelfServeLimitUsageDto(int Used, int? Max);

/// <summary>ชื่อ property ตรงกับ <c>PlanLimitNames.ToJsonName</c> (staff · catalog · customers · jobs)</summary>
public sealed record SelfServeLimitsDto(
    SelfServeLimitUsageDto Staff,
    SelfServeLimitUsageDto Catalog,
    SelfServeLimitUsageDto Customers,
    SelfServeLimitUsageDto Jobs);

/// <summary>
/// ตัวเลขดิบให้เว็บติ๊กเช็คลิสต์เริ่มต้นเอง (docs/12 §4.3 — "ติ๊กจากข้อมูลจริง ผู้ใช้ไม่ต้องติ๊ก")
/// · staffCount นับเจ้าของอู่ด้วย (เว็บถือว่า > 1 = เพิ่มช่างแล้ว) · quotationSentCount = ใบที่เคยส่งลูกค้าแล้ว
/// </summary>
public sealed record SelfServeChecklistDto(
    int StaffCount,
    int CatalogCount,
    int JobCount,
    int QuotationSentCount,
    bool MobileLoginSeen);

/// <summary>
/// <c>GET /api/v1/selfserve/status</c> — สัญญากับ web/src/api/selfserve.ts
/// · <see cref="Plan"/>/<see cref="Status"/> = ค่าใน sp_Workspace ตัวพิมพ์เล็ก (trial|paid|suspended|converted ·
///   provisioning|active|expired|purged) · <see cref="TrialEndsAt"/> มาจาก Branch.EndPackageDate (ตัวจริง) ก่อนสำเนาใน sp_Workspace
/// · <see cref="ReadOnly"/> = กติกาเดียวกับ TrialReadOnlyFilter (IWorkspacePolicy) — เพิ่มจากสัญญาเดิม เว็บอ่านแบบ optional
/// </summary>
public sealed record SelfServeStatusDto(
    string WorkspaceNo,
    string Name,
    string Plan,
    string Status,
    DateTime? TrialEndsAt,
    int? DaysLeft,
    bool ReadOnly,
    SelfServeLimitsDto Limits,
    DateTime? SampleDataLoadedAt,
    DateTime? ExtensionRequestedAt,
    SelfServeChecklistDto Checklist);

/// <summary>
/// <c>POST /api/v1/selfserve/extension-request</c> · <see cref="RequestId"/> เป็นข้อความ (ไม่ใช่ Guid) เพื่อไม่ให้
/// model binding ตอบ 400 แบบ ProblemDetails เมื่อ client ส่งค่าผิดรูป — ตรวจเองแล้วตอบ envelope ปกติ
/// </summary>
public sealed record ExtensionRequestInput(string? RequestId, string? Note);

public sealed record ExtensionRequestedDto(DateTime RequestedAt);

/// <summary><c>POST /api/v1/selfserve/sample-data</c> — <see cref="Technicians"/> มีรหัสผ่าน แสดงครั้งเดียว</summary>
public sealed record SampleDataLoadDto(
    bool Loaded,
    bool AlreadyLoaded,
    IReadOnlyList<SampleCredential> Technicians,
    string? Error);

public sealed record SampleDataClearedDto(bool Cleared);
