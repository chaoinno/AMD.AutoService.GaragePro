namespace AMD.AutoService.GaragePro.Application.Dtos;

// ---------- อ่านจาก Garage DB เดิม ----------

/// <summary>ผู้ใช้ใน dbo.User ของ Garage DB เดิม พร้อมข้อมูลพนักงานที่ผูกอยู่</summary>
public sealed record LegacyUserDto(
    long UserId,
    string UserName,
    string StoredPassword,
    bool IsAdministrator,
    bool IsStaff,
    long? StaffId,
    string? StaffName,
    int? BranchId,
    string? BranchName,
    string? PositionName,
    string? SectorName,
    string? DepartmentName,
    string? SkillLevel);

public sealed record LegacyBranchSummaryDto(
    int BranchId,
    string Name,
    string? Address,
    string? Phone);

/// <summary>บัญชีปลายทางของการสลับสาขา — UserId ใช้ภายใน backend เท่านั้น ห้ามส่งให้ client</summary>
public sealed record LegacySwitchableAccountDto(
    long UserId,
    int BranchId,
    string Name,
    string? Address);

// ---------- คำขอ ----------

public sealed record LoginRequest(string UserName, string Password);

public sealed record OpenShiftRequest(int BranchId, Guid ShiftId);

public sealed record SwitchBranchRequest(int BranchId);

// ---------- คำตอบ ----------

/// <summary>
/// ผลการเข้าสู่ระบบ — token ผูก Staff.BranchId และเรียก API งานได้ทันที
/// Branches ยังส่งไว้ชั่วคราวเพื่อรองรับ Mobile flow เดิม
/// </summary>
public sealed record LoginResultDto(
    string AccessToken,
    DateTime ExpiresAt,
    AuthUserDto User,
    int BranchId,
    string BranchName,
    IReadOnlyList<BranchOptionDto> Branches,
    bool RequiresShiftSelection);

public sealed record AuthUserDto(
    long UserId,
    string UserName,
    string DisplayName,
    string Role,
    string RoleLabelTh,
    bool IsAdministrator,
    string ShardKey,
    long? StaffId,
    string? PositionName,
    string? DepartmentName,
    bool CanSeeCost,
    bool CanCloseShift);

/// <summary>สาขาที่เลือกได้ พร้อมตัวเลขงานค้าง — ตรงกับหน้าเลือกสาขาใน design</summary>
public sealed record BranchOptionDto(
    int BranchId,
    string Name,
    string? Address,
    string? Phone,
    int PendingQuotationCount,
    int WaitingApprovalCount);

/// <summary>สาขาที่ผู้ดูแลระบบสลับไปได้ — ไม่มี UserId ของบัญชีปลายทาง (server หาใหม่ทุกครั้งที่สลับ)</summary>
public sealed record SwitchableBranchDto(
    int BranchId,
    string Name,
    string? Address,
    bool IsCurrent);

public sealed record ShiftOptionDto(
    Guid ShiftId,
    string Name,
    string StartTime,
    string EndTime,
    string? SupervisorName,
    bool IsCurrent);

/// <summary>token ที่ใช้งานได้จริง — มี branch + shift อยู่ใน claim แล้ว</summary>
public sealed record ShiftSessionDto(
    Guid SessionId,
    string AccessToken,
    DateTime ExpiresAt,
    AuthUserDto User,
    int BranchId,
    string BranchName,
    Guid ShiftId,
    string ShiftName,
    DateTime OpenedAt);

public sealed record MeDto(
    AuthUserDto User,
    Guid? SessionId,
    int? BranchId,
    string? BranchName,
    Guid? ShiftId,
    string? ShiftName,
    DateTime? OpenedAt);
