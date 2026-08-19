using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class AuthRepository(ServiceDbContext db) : IAuthRepository
{
    public async Task<IReadOnlyList<Shift>> GetShiftsAsync(
        string shardKey, int branchId, CancellationToken ct = default) =>
        await db.Shifts
            .Where(s => s.LegacyShardKey == shardKey && s.LegacyBranchId == branchId && s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ToListAsync(ct);

    public Task<Shift?> GetShiftAsync(Guid shiftId, CancellationToken ct = default) =>
        db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct);

    public async Task AddShiftsAsync(IEnumerable<Shift> shifts, CancellationToken ct = default) =>
        await db.Shifts.AddRangeAsync(shifts, ct);

    public Task<ShiftSession?> GetSessionAsync(Guid sessionId, CancellationToken ct = default) =>
        db.ShiftSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    public async Task AddSessionAsync(ShiftSession session, CancellationToken ct = default) =>
        await db.ShiftSessions.AddAsync(session, ct);

    public async Task CloseOpenSessionsAsync(
        string shardKey, long userId, DateTime closedAt, CancellationToken ct = default)
    {
        var open = await db.ShiftSessions
            .Where(s => s.LegacyShardKey == shardKey
                     && s.LegacyUserId == userId
                     && s.ClosedAt == null)
            .ToListAsync(ct);

        foreach (var session in open)
        {
            session.ClosedAt = closedAt;
            session.ClosedByUserId = userId;
        }
    }

    public Task<UserRoleOverride?> GetRoleOverrideAsync(
        string shardKey, long userId, CancellationToken ct = default) =>
        db.UserRoleOverrides.FirstOrDefaultAsync(
            o => o.LegacyShardKey == shardKey && o.LegacyUserId == userId, ct);

    /// <summary>ตัวเลขงานค้างต่อสาขา — ใช้บนหน้าเลือกสาขา</summary>
    public async Task<BranchWorkload> GetBranchWorkloadAsync(
        string shardKey, int branchId, CancellationToken ct = default)
    {
        var counts = await db.Quotations
            .Where(q => q.LegacyShardKey == shardKey && q.LegacyBranchId == branchId)
            .GroupBy(q => q.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new BranchWorkload(
            PendingQuotations: counts.Where(c => c.Status == QuotationStatus.Draft).Sum(c => c.Count),
            WaitingApproval: counts.Where(c => c.Status == QuotationStatus.Sent).Sum(c => c.Count));
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
