using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>อ่านผู้ใช้และสิทธิ์สาขาจาก Garage DB เดิม — read-only</summary>
public interface ILegacyUserReader
{
    Task<LegacyUserDto?> FindByUserNameAsync(string shardKey, string userName, CancellationToken ct = default);
    Task<LegacyUserDto?> FindByIdAsync(string shardKey, long userId, CancellationToken ct = default);

    /// <summary>
    /// สาขาที่ผู้ใช้เข้าได้ — พนักงานทั่วไปได้สาขาตัวเอง ผู้ดูแลระบบได้ทุกสาขาใน shard
    /// </summary>
    Task<IReadOnlyList<LegacyBranchSummaryDto>> GetAccessibleBranchesAsync(
        string shardKey, LegacyUserDto user, CancellationToken ct = default);

    /// <summary>
    /// บัญชีผู้ดูแลระบบในสาขาอื่นที่ "เป็นคนเดียวกัน" กับพนักงาน <paramref name="staffId"/> — ใช้สลับสาขาบนเว็บ
    /// (คำขอผู้ใช้ 2026-10-12) ระบบเดิมสร้าง User+Staff แยกต่อสาขา จึงจับคู่ด้วย Staff.FirstName
    /// คืนสาขาละไม่เกินหนึ่งบัญชี รวมบัญชีของตัวเองด้วยถ้าเข้าเงื่อนไข
    /// · default member เพื่อไม่ต้องแก้ fake ในเทสต์ที่ไม่เกี่ยวข้อง
    /// </summary>
    Task<IReadOnlyList<LegacySwitchableAccountDto>> GetSwitchableAccountsAsync(
        string shardKey, long staffId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LegacySwitchableAccountDto>>([]);
}

public sealed record BranchWorkload(int PendingQuotations, int WaitingApproval);

public interface IAuthRepository
{
    Task<IReadOnlyList<Shift>> GetShiftsAsync(string shardKey, int branchId, CancellationToken ct = default);
    Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken ct = default);
    Task AddShiftsAsync(IEnumerable<Shift> shifts, CancellationToken ct = default);

    Task<ShiftSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default);
    Task AddSessionAsync(ShiftSession session, CancellationToken ct = default);
    Task CloseOpenSessionsAsync(string shardKey, long userId, DateTime closedAt, CancellationToken ct = default);

    Task<UserRoleOverride?> GetRoleOverrideAsync(string shardKey, long userId, CancellationToken ct = default);

    Task<BranchWorkload> GetBranchWorkloadAsync(string shardKey, int branchId, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>ออก JWT — แยก interface ไว้เพื่อให้ Application ไม่ผูกกับ library ของ token</summary>
public interface ITokenIssuer
{
    /// <summary>token ก่อนเลือกสาขา/กะ — เรียกได้เฉพาะ endpoint ของการเลือกสาขา</summary>
    (string Token, DateTime ExpiresAt) IssuePreSessionToken(AuthUserDto user);

    /// <summary>token ที่ใช้งานได้ทันที โดยผูกสาขาจาก Staff.BranchId แต่ไม่สร้างกะ</summary>
    (string Token, DateTime ExpiresAt) IssueBranchToken(AuthUserDto user, int branchId);

    /// <summary>
    /// token ของแอปมือถือ — claim เดียวกับ <see cref="IssueBranchToken"/> แต่อายุยาวจนแทบไม่หมด
    /// (คำขอผู้ใช้ 2026-10-05: ช่างไม่ต้อง login ใหม่ทุก 12 ชม.) การตัดสิทธิ์อาศัย
    /// <c>IAuthService.GetSessionRevocationReasonAsync</c> ที่ตรวจทุกคำขอแทนวันหมดอายุ
    /// · default member เพื่อไม่ต้องแก้ fake ในเทสต์ที่ไม่เกี่ยวข้อง
    /// </summary>
    (string Token, DateTime ExpiresAt) IssueMobileToken(AuthUserDto user, int branchId) =>
        IssueBranchToken(user, branchId);

    /// <summary>token ที่ใช้งานจริง — มี branch/shift/session อยู่ใน claim</summary>
    (string Token, DateTime ExpiresAt) IssueSessionToken(AuthUserDto user, ShiftSession session);
}
