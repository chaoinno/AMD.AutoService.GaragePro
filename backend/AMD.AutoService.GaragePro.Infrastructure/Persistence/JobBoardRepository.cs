using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class JobBoardRepository(ServiceDbContext db) : IJobBoardRepository
{
    private const int InShopTypeId = 9;

    private static readonly JobStatus[] TerminalStatuses =
        Enum.GetValues<JobStatus>().Where(JobStateMachine.IsTerminal).ToArray();

    public async Task<IReadOnlyList<Job>> GetOpenInShopJobsAsync(
        string shardKey, int branchId, string? keyword, int take, CancellationToken ct = default)
    {
        var q = db.Jobs.AsNoTracking().Where(j =>
            j.LegacyShardKey == shardKey && j.BranchId == branchId
            && j.JobTypeId == InShopTypeId && !TerminalStatuses.Contains(j.Status));

        // คำค้นชุดเดียวกับ JobRepository.SearchAsync — ผู้ใช้สลับรายการ/บอร์ดแล้วได้ผลตรงกัน
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var k = keyword.Trim();
            q = q.Where(j =>
                EF.Functions.Like(j.JobNo, $"%{k}%") ||
                EF.Functions.Like(j.VehicleRegistration, $"%{k}%") ||
                EF.Functions.Like(j.CustomerName, $"%{k}%") ||
                (j.CustomerPhone != null && EF.Functions.Like(j.CustomerPhone, $"%{k}%")));
        }

        return await q.OrderBy(j => j.CreatedAt).ThenBy(j => j.Id).Take(take).ToListAsync(ct);
    }

    public async Task<IReadOnlyDictionary<Guid, DateTime>> GetLastStatusChangeAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default)
    {
        // เหตุการณ์ชุดเดียวกับ "จ๊อบที่ค้างนานที่สุด" ของรายงานรอบเวลา (ReportsRepository.GetJobLifecycleEventsAsync)
        var rows = await db.ActivityEvents.AsNoTracking()
            .Where(e => e.JobId != null && jobIds.Contains(e.JobId.Value)
                && (e.EventType == "job.opened" || e.EventType == "job.status.changed"))
            .GroupBy(e => e.JobId!.Value)
            .Select(g => new { JobId = g.Key, At = g.Max(e => e.OccurredAt) })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.JobId, r => r.At);
    }

    public async Task<IReadOnlyList<WorkInterval>> GetOpenWorkIntervalsAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default) =>
        await db.WorkIntervals.AsNoTracking()
            .Where(w => jobIds.Contains(w.JobId) && w.EndedAt == null && w.VoidedAt == null)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Quotation>> GetSentQuotationsAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default) =>
        await db.Quotations.AsNoTracking()
            .Include(q => q.Lines)
            .Include(q => q.Approval)
            .Where(q => jobIds.Contains(q.JobId) && q.Status == QuotationStatus.Sent)
            .AsSplitQuery()
            .ToListAsync(ct);
}
