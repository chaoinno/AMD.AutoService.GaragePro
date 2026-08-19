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

    /// <summary>token ที่ใช้งานจริง — มี branch/shift/session อยู่ใน claim</summary>
    (string Token, DateTime ExpiresAt) IssueSessionToken(AuthUserDto user, ShiftSession session);
}
