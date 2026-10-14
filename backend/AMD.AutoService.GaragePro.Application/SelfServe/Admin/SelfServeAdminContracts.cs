using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Admin;

/// <summary>
/// หนึ่งแถวของ <c>spadmin list</c> — <see cref="EndsAt"/> = Branch.EndPackageDate (ตัวจริง แปลงเป็น UTC แล้ว)
/// ไม่มีก็ใช้สำเนา sp_Workspace.TrialEndsAt · null = ไม่มีกำหนด (paid แบบไม่มีวันหมด)
/// </summary>
public sealed record AdminWorkspaceRow(
    Guid Id,
    int WorkspaceNo,
    int? BranchId,
    string Name,
    string? Phone,
    WorkspacePlanKind Plan,
    WorkspaceStatus Status,
    DateTime? EndsAt,
    DateTime CreatedAt,
    string? OwnerEmail,
    DateTime? PurgeAfter,
    int? ConvertedToBranchId);

/// <summary>บัญชีเจ้าของอู่ (sp_Account) สำหรับแสดงผล — [SECURITY] ไม่มี PasswordHash/SetupTokenHash/GoogleSubject</summary>
public sealed record AdminAccountInfo(
    Guid Id,
    string? Email,
    string NormalizedEmail,
    SelfServeAccountProvider Provider,
    SelfServeAccountStatus Status,
    string? LoginUserName,
    long? UserId,
    DateTime? EmailVerifiedAt,
    DateTime CreatedAt);

public sealed record AdminAuditEntry(DateTime CreatedAt, string Action, string Actor, string? Note, string? PayloadJson);

/// <summary>ข้อมูลดิบของ <c>spadmin show</c> จาก repository — <see cref="Branch"/> = แถว Branch ของ shard sp1 (null = ไม่พบ)</summary>
public sealed record AdminWorkspaceSnapshot(
    Workspace Workspace,
    AdminAccountInfo? Owner,
    BranchPackage? Branch,
    IReadOnlyList<AdminAuditEntry> Audit);

public sealed record AdminLimitUsage(string Kind, int Used, int? Max);

public sealed record AdminWorkspaceDetail(
    AdminWorkspaceSnapshot Snapshot,
    DateTime? EndsAt,
    int? DaysLeft,
    IReadOnlyList<AdminLimitUsage> Usage,
    IReadOnlyList<string> Warnings);

/// <summary>
/// การเปลี่ยนแพ็กเกจหนึ่งครั้ง — repository เขียน Branch (ถ้า <see cref="SetBranchEnd"/>) + sp_Workspace + sp_WorkspaceAudit
/// ใน transaction เดียว (ฐาน GarageSelfServe เดียวกัน) เพื่อไม่ให้วันใน Branch กับสำเนาใน sp_Workspace เพี้ยนกัน
/// · <see cref="EndsAtUtc"/> null + <see cref="SetBranchEnd"/> = ไม่มีกำหนด (paid แบบเปิด) · สำเนา TrialEndsAt ตามค่าเดียวกัน
/// </summary>
public sealed record WorkspacePackageChange(
    Guid WorkspaceId,
    int BranchId,
    bool SetBranchEnd,
    DateTime? EndsAtUtc,
    WorkspacePlanKind Plan,
    WorkspaceStatus Status,
    bool ClearPurgeAfter,
    string Actor,
    string AuditAction,
    string? Note,
    string PayloadJson,
    DateTime Now);

/// <summary>ผลของ extend/set-plan/suspend สำหรับพิมพ์สรุป</summary>
public sealed record AdminChangeResult(
    int WorkspaceNo,
    WorkspacePlanKind PlanFrom,
    WorkspacePlanKind PlanTo,
    WorkspaceStatus StatusFrom,
    WorkspaceStatus StatusTo,
    DateTime? EndsAtFrom,
    DateTime? EndsAtTo);

/// <summary>
/// ข้อมูลของเครื่องมือทีมงาน (sp_* + แถว Branch ของ shard sp1) — Infrastructure implement
/// · ทุกการเขียนต้องตรวจก่อนว่าค่าตั้งชี้ฐาน GarageSelfServe จริง (<see cref="CheckWritableDatabase"/>) — ห้ามแตะ Branch ของ Garage จริง
/// </summary>
public interface ISelfServeAdminRepository
{
    Task<IReadOnlyList<AdminWorkspaceRow>> ListAsync(WorkspaceStatus? status, WorkspacePlanKind? plan,
        CancellationToken ct = default);

    Task<AdminWorkspaceSnapshot?> GetAsync(int workspaceNo, int auditTail, CancellationToken ct = default);

    /// <summary>null = ปลอดภัย · ข้อความ = ค่าตั้งชี้ผิดฐาน ห้ามเขียน</summary>
    string? CheckWritableDatabase();

    Task ApplyPackageChangeAsync(WorkspacePackageChange change, CancellationToken ct = default);
}
