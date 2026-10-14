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

    /// <summary>
    /// ตั้ง/แก้บทบาทรายคน — หนึ่งแถวต่อ (shard, User.Id) ตาม unique index ของ svc_UserRoleOverride
    /// · มีแล้ว = แก้ Role/สาขา/ผู้ตั้ง/เวลา ในแถวเดิม (ไม่ลบแล้วสร้างใหม่ — Id เดิมคงอยู่สำหรับ audit)
    /// · ไม่ SaveChanges เอง: ผู้เรียกบันทึกพร้อมงานอื่นของ scope เดียวกัน (เหมือนเมธอดอื่นของ repository นี้)
    ///   ดูทั้งแถวที่ track ไว้แล้วใน context ก่อน เพื่อให้เรียกซ้ำก่อน save ได้โดยไม่ชน unique index
    /// </summary>
    public async Task UpsertRoleOverrideAsync(UserRoleOverride entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var existing = db.UserRoleOverrides.Local.FirstOrDefault(
                           o => o.LegacyShardKey == entry.LegacyShardKey && o.LegacyUserId == entry.LegacyUserId)
                       ?? await db.UserRoleOverrides.FirstOrDefaultAsync(
                           o => o.LegacyShardKey == entry.LegacyShardKey && o.LegacyUserId == entry.LegacyUserId, ct);

        if (existing is null)
        {
            await db.UserRoleOverrides.AddAsync(entry, ct);
            return;
        }

        existing.Role = entry.Role;
        existing.LegacyBranchId = entry.LegacyBranchId;
        existing.SetByUserId = entry.SetByUserId;
        existing.SetAt = entry.SetAt;
        existing.Note = entry.Note ?? existing.Note;
    }

    /// <summary>ตัวเลขงานค้างต่อสาขา — ใช้บนหน้าเลือกสาขา</summary>
    public async Task<BranchWorkload> GetBranchWorkloadAsync(
        string shardKey, int branchId, CancellationToken ct = default)
    {
        var counts = await db.Quotations
            .Where(q => q.Job!.LegacyShardKey == shardKey && q.Job.BranchId == branchId)
            .GroupBy(q => q.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return new BranchWorkload(
            PendingQuotations: counts.Where(c => c.Status == QuotationStatus.Draft).Sum(c => c.Count),
            WaitingApproval: counts.Where(c => c.Status == QuotationStatus.Sent).Sum(c => c.Count));
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
