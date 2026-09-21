using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class WorkIntervalRepository(ServiceDbContext db, ICurrentUser user) : IWorkIntervalRepository
{
    private IQueryable<WorkInterval> Scoped =>
        db.WorkIntervals.Where(x => x.LegacyShardKey == user.ShardKey && x.LegacyBranchId == user.BranchId);

    public Task<WorkInterval?> GetAsync(Guid id, CancellationToken ct = default) =>
        Scoped.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<WorkInterval?> GetOpenByTechnicianAsync(long staffId, CancellationToken ct = default) =>
        Scoped.FirstOrDefaultAsync(x => x.TechnicianStaffId == staffId && x.EndedAt == null, ct);

    public Task<WorkInterval?> GetOpenByTechnicianAsync(
        string shardKey, int branchId, long staffId, CancellationToken ct = default) =>
        db.WorkIntervals.FirstOrDefaultAsync(
            x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId
                 && x.TechnicianStaffId == staffId && x.EndedAt == null, ct);

    // ไม่สโคปด้วย shard/สาขาเพราะ JobId เป็น Guid ที่ชี้เฉพาะเจาะจงอยู่แล้ว และผู้เรียก (hook ใน
    // JobService) ตรวจสิทธิ์ของจ๊อบมาก่อนแล้ว — แบบเดียวกับ PosRepository ที่กรองด้วย JobId ล้วน
    public async Task<IReadOnlyList<WorkInterval>> GetOpenByJobAsync(Guid jobId, CancellationToken ct = default) =>
        await db.WorkIntervals.Where(x => x.JobId == jobId && x.EndedAt == null).ToListAsync(ct);

    public Task<WorkInterval?> GetByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
        db.WorkIntervals.FirstOrDefaultAsync(x => x.RequestId == requestId, ct);

    public async Task<IReadOnlyList<WorkInterval>> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
        await db.WorkIntervals.Where(x => x.JobId == jobId).OrderBy(x => x.StartedAt).ToListAsync(ct);

    public async Task<IReadOnlyList<WorkInterval>> SearchAsync(
        DateTime fromUtc, DateTime toUtc, bool onlyNeedsReview, int take, CancellationToken ct = default)
    {
        var query = Scoped.AsNoTracking().Where(x => x.StartedAt >= fromUtc && x.StartedAt < toUtc);

        // "ต้องตรวจ" = ระบบตัดให้เองเพราะลืมกดหยุด หรือยังเปิดค้างอยู่ — สองกลุ่มนี้คือที่มาของ
        // ข้อมูลที่เชื่อไม่ได้ทั้งหมด (docs/09 §2.4)
        if (onlyNeedsReview) query = query.Where(x => x.IsAutoCapped || x.EndedAt == null);

        return await query.OrderByDescending(x => x.StartedAt).ThenBy(x => x.Id).Take(take).ToListAsync(ct);
    }

    public async Task AddAsync(WorkInterval interval, CancellationToken ct = default) =>
        await db.WorkIntervals.AddAsync(interval, ct);

    public async Task<Shift?> GetShiftForSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        var shiftId = await db.ShiftSessions.Where(s => s.Id == sessionId).Select(s => s.ShiftId).FirstOrDefaultAsync(ct);
        return shiftId == Guid.Empty ? null : await db.Shifts.FirstOrDefaultAsync(s => s.Id == shiftId, ct);
    }

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    /// <summary>
    /// [BIZ] ชน UX_WorkInterval_OneOpenPerTech หรือ unique ของ RequestId จะมาโผล่ที่นี่ — แปลงเป็น
    /// exception ของ Application แล้วให้ service ไป re-read ตัดสินเอาเองว่าชนเพราะอะไร
    /// ที่นี่แยกไม่ได้และไม่ควรพยายามแยก (ดูเหตุผลที่ WorkIntervalConflictException)
    /// </summary>
    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new WorkIntervalConflictException("มีการบันทึกช่วงเวลานี้ไปแล้ว กรุณาโหลดสถานะล่าสุดแล้วลองใหม่");
        }
    }
}
