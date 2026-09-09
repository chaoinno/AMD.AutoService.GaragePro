using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class JobRepository(ServiceDbContext db) : IJobRepository
{
    private static readonly JobStatus[] TerminalStatuses =
        Enum.GetValues<JobStatus>().Where(JobStateMachine.IsTerminal).ToArray();

    public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) =>
        db.Jobs.FirstOrDefaultAsync(j => j.Id == jobId, ct);

    public Task<Job?> GetOpenByVehicleAsync(
        string shardKey, int branchId, long vehicleId, CancellationToken ct = default) =>
        db.Jobs.FirstOrDefaultAsync(j =>
            j.LegacyShardKey == shardKey && j.BranchId == branchId && j.VehicleId == vehicleId
            && !TerminalStatuses.Contains(j.Status), ct);

    public async Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default)
    {
        var q = db.Jobs.Where(j => j.LegacyShardKey == query.ShardKey && j.BranchId == query.BranchId);

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var k = query.Keyword.Trim();
            q = q.Where(j =>
                EF.Functions.Like(j.JobNo, $"%{k}%") ||
                EF.Functions.Like(j.VehicleRegistration, $"%{k}%") ||
                EF.Functions.Like(j.CustomerName, $"%{k}%") ||
                (j.CustomerPhone != null && EF.Functions.Like(j.CustomerPhone, $"%{k}%")));
        }

        if (query.JobTypeId is not null)
            q = q.Where(j => j.JobTypeId == query.JobTypeId);

        if (query.Status is not null)
            q = q.Where(j => j.Status == query.Status);

        if (query.BeforeCreatedAt is not null && query.BeforeJobId is not null)
        {
            var beforeAt = query.BeforeCreatedAt.Value;
            var beforeId = query.BeforeJobId.Value;
            q = q.Where(j =>
                j.CreatedAt < beforeAt ||
                (j.CreatedAt == beforeAt && j.Id.CompareTo(beforeId) < 0));
        }

        return await q
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id)
            .Take(query.Take)
            .ToListAsync(ct);
    }

    public Task<int> CountOpenAsync(
        string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default)
    {
        var q = db.Jobs.Where(j =>
            j.LegacyShardKey == shardKey && j.BranchId == branchId && !TerminalStatuses.Contains(j.Status));

        if (jobTypeId is not null)
            q = q.Where(j => j.JobTypeId == jobTypeId);

        return q.CountAsync(ct);
    }

    public async Task AddAsync(Job job, CancellationToken ct = default) =>
        await db.Jobs.AddAsync(job, ct);

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
