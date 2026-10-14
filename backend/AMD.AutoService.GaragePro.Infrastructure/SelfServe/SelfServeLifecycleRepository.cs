using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Legacy;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ข้อมูลของงานรายวัน/endpoint selfserve — sp_* ผ่าน <see cref="SelfServeDbContext"/> · Branch.EndPackageDate ผ่าน Dapper
/// (ตารางรูปร่าง Garage ใน GarageSelfServe) · ใบเสนอราคาผ่าน ServiceDbContext ที่สร้างเองชี้ shard sp1
/// · ไม่ใช้ ServiceDbContext ของ scope: hosted job ไม่มี HttpContext และ scope อาจเป็นของ shard อื่น (แบบ PlanUsageCounter)
/// · "claim" ทุกตัว (job run · notice · extension) พึ่ง unique index / UPDATE แบบมีเงื่อนไขที่ฐานข้อมูล ไม่ใช่การอ่านก่อนเขียน
/// · ไม่มี BeginTransactionAsync (SelfServeDbContext เปิด EnableRetryOnFailure) — แต่ละเมธอดเป็น SaveChanges/statement เดียว
/// </summary>
public sealed class SelfServeLifecycleRepository(
    SelfServeDbContext db,
    IServiceDbConnectionResolver connections,
    IOptions<LegacyShardOptions> legacyOptions,
    IOptions<SelfServeOptions> selfServeOptions) : ISelfServeLifecycleRepository
{
    private readonly LegacyShardOptions _legacy = legacyOptions.Value;
    private readonly SelfServeOptions _options = selfServeOptions.Value;

    // ฐาน GarageSelfServe มีแต่สาขาของอู่ที่สมัครเอง จึงอ่านทั้งตารางได้ (ไม่ต้องส่ง IN @Ids ที่ชนเพดาน 2,100 พารามิเตอร์)
    internal const string BranchEndDatesSql = """
        SELECT b.Id, b.EndPackageDate FROM Branch b WITH (READUNCOMMITTED)
        """;

    private static readonly QuotationStatus[] SentStatuses =
        [QuotationStatus.Sent, QuotationStatus.Partial, QuotationStatus.Approved, QuotationStatus.Rejected];

    public async Task<SelfServeWorkspaceInfo?> FindByBranchAsync(int branchId, CancellationToken ct = default) =>
        (await QueryWorkspaces(db.Workspaces.AsNoTracking().Where(w => w.BranchId == branchId))
            .Take(1).ToListAsync(ct)).FirstOrDefault();

    public async Task<IReadOnlyList<LifecycleWorkspace>> ListForLifecycleAsync(CancellationToken ct = default)
    {
        var workspaces = await QueryWorkspaces(db.Workspaces.AsNoTracking()
                .Where(w => w.BranchId != null && w.Status != WorkspaceStatus.Purged))
            .ToListAsync(ct);
        if (workspaces.Count == 0) return [];

        var notices = await (
                from n in db.WorkspaceNotices.AsNoTracking()
                join w in db.Workspaces.AsNoTracking() on n.WorkspaceId equals w.Id
                where w.Status != WorkspaceStatus.Purged
                select new { n.WorkspaceId, n.Kind })
            .ToListAsync(ct);
        var noticesByWorkspace = notices
            .GroupBy(n => n.WorkspaceId)
            .ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(n => n.Kind).ToHashSet(StringComparer.Ordinal));

        var endDates = await ReadBranchEndDatesAsync(ct);
        var none = (IReadOnlySet<string>)new HashSet<string>();

        return workspaces
            .Select(w => new LifecycleWorkspace(
                w,
                endDates.TryGetValue(w.BranchId!.Value, out var end) ? end : null,
                noticesByWorkspace.GetValueOrDefault(w.Id, none)))
            .ToList();
    }

    public async Task<bool> TryRecordExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime notBefore,
        string actor, string? note, CancellationToken ct = default)
    {
        var rows = await db.Workspaces
            .Where(w => w.Id == workspaceId && (w.ExtensionRequestedAt == null || w.ExtensionRequestedAt < notBefore))
            .ExecuteUpdateAsync(s => s
                .SetProperty(w => w.ExtensionRequestedAt, requestedAt)
                .SetProperty(w => w.UpdatedAt, requestedAt), ct);
        if (rows == 0) return false;

        db.WorkspaceAudits.Add(new WorkspaceAudit
        {
            WorkspaceId = workspaceId,
            Action = "extension-request",
            Actor = Limit(actor, 200) ?? "",
            Note = Limit(note, 1000),
            CreatedAt = requestedAt
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task RevertExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime? previous,
        CancellationToken ct = default)
    {
        var rows = await db.Workspaces
            .Where(w => w.Id == workspaceId && w.ExtensionRequestedAt == requestedAt)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.ExtensionRequestedAt, previous), ct);
        if (rows == 0) return;

        db.WorkspaceAudits.Add(new WorkspaceAudit
        {
            WorkspaceId = workspaceId,
            Action = "extension-request-reverted",
            Actor = "system",
            Note = "ส่งข้อความถึงทีมขายไม่สำเร็จ",
            CreatedAt = requestedAt
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<int> CountQuotationsEverSentAsync(string shardKey, int branchId,
        IReadOnlyCollection<Guid>? excludeQuotationIds = null, CancellationToken ct = default)
    {
        var options = new DbContextOptionsBuilder<ServiceDbContext>()
            .UseSqlServer(connections.Resolve(shardKey), sql => sql.EnableRetryOnFailure(3))
            .Options;
        await using var service = new ServiceDbContext(options);
        var query = QuotationsEverSent(service, shardKey, branchId);
        if (excludeQuotationIds is { Count: > 0 })
        {
            var excluded = excludeQuotationIds.ToList();
            query = query.Where(q => !excluded.Contains(q.Id));
        }
        return await query.CountAsync(ct);
    }

    public async Task<IReadOnlyList<SampleLedgerItem>> GetSampleEntitiesAsync(Guid workspaceId,
        CancellationToken ct = default) =>
        await db.SampleEntities.AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId)
            .Select(e => new SampleLedgerItem(e.EntityType, e.EntityId))
            .ToListAsync(ct);

    /// <summary>แยกเป็น query ล้วนให้เทสต์ตรวจ SQL ที่แปลได้โดยไม่ต้องมีฐาน</summary>
    internal static IQueryable<Domain.Entities.Quotation> QuotationsEverSent(ServiceDbContext service, string shardKey,
        int branchId) =>
        service.Quotations.AsNoTracking()
            .Where(q => q.Job!.LegacyShardKey == shardKey && q.Job.BranchId == branchId)
            .Where(q => q.SentAt != null || SentStatuses.Contains(q.Status));

    public async Task<bool> TryAddNoticeAsync(Guid workspaceId, string kind, DateTime sentAt,
        CancellationToken ct = default)
    {
        var notice = new WorkspaceNotice { WorkspaceId = workspaceId, Kind = kind, SentAt = sentAt };
        db.WorkspaceNotices.Add(notice);
        return await TrySaveClaimAsync(notice, ct);
    }

    public Task RemoveNoticeAsync(Guid workspaceId, string kind, CancellationToken ct = default) =>
        db.WorkspaceNotices.Where(n => n.WorkspaceId == workspaceId && n.Kind == kind).ExecuteDeleteAsync(ct);

    public async Task SetStatusAsync(Guid workspaceId, WorkspaceStatus status, DateTime? purgeAfter, DateTime now,
        string actor, string auditAction, CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
                        ?? throw new InvalidOperationException($"ไม่พบ workspace {workspaceId}");
        var from = workspace.Status;

        workspace.Status = status;
        workspace.PurgeAfter ??= purgeAfter;
        workspace.UpdatedAt = now;
        db.WorkspaceAudits.Add(new WorkspaceAudit
        {
            WorkspaceId = workspaceId,
            Action = auditAction,
            Actor = Limit(actor, 200) ?? "",
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new { from = from.ToString(), to = status.ToString() }),
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> TryClaimJobRunAsync(string jobName, DateOnly runDate, DateTime startedAt,
        string? instanceName, CancellationToken ct = default)
    {
        var run = new JobRun
        {
            JobName = jobName,
            RunDate = runDate,
            StartedAt = startedAt,
            InstanceName = Limit(instanceName, 100)
        };
        db.JobRuns.Add(run);
        return await TrySaveClaimAsync(run, ct);
    }

    public Task CompleteJobRunAsync(string jobName, DateOnly runDate, DateTime finishedAt, string outcome,
        string? error, CancellationToken ct = default)
    {
        var limitedOutcome = Limit(outcome, 200);
        var limitedError = Limit(error, 2000);
        return db.JobRuns.Where(r => r.JobName == jobName && r.RunDate == runDate)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.FinishedAt, finishedAt)
                .SetProperty(r => r.Outcome, limitedOutcome)
                .SetProperty(r => r.Error, limitedError), ct);
    }

    public Task<DateTime?> LastSuccessfulRunAsync(string jobName, CancellationToken ct = default) =>
        db.JobRuns.AsNoTracking()
            .Where(r => r.JobName == jobName && r.FinishedAt != null && r.Outcome != null && !r.Outcome.StartsWith("failed"))
            .MaxAsync(r => r.FinishedAt, ct);

    public async Task<IReadOnlyList<Guid>> ListAbandonedNeedingServicePurgeAsync(CancellationToken ct = default) =>
        await db.Workspaces.AsNoTracking()
            .Where(w => w.Status == WorkspaceStatus.Purged && w.BranchId != null
                        && db.WorkspaceAudits.Any(a => a.WorkspaceId == w.Id
                                                       && a.Action == WorkspaceProvisioner.AbandonedAuditAction)
                        && !db.WorkspaceAudits.Any(a => a.WorkspaceId == w.Id
                                                        && a.Action == WorkspacePurger.AbandonedServicePurgedAction))
            .Select(w => w.Id)
            .ToListAsync(ct);

    public async Task<int> UpsertBlockedDomainsAsync(IReadOnlyCollection<string> domains, string source, DateTime now,
        CancellationToken ct = default)
    {
        var existing = (await db.BlockedDomains.AsNoTracking().Select(d => d.Domain).ToListAsync(ct))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (var chunk in domains
                     .Select(d => d.Trim().ToLowerInvariant())
                     .Where(d => d.Length is > 0 and <= 255 && existing.Add(d))
                     .Chunk(500))
        {
            db.BlockedDomains.AddRange(chunk.Select(d => new BlockedDomain
            {
                Domain = d,
                Source = source,
                Reason = "รายชื่อโดเมนอีเมลชั่วคราว (SelfServe:Blocklist:Url)",
                CreatedAt = now
            }));
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            added += chunk.Length;
        }

        return added;
    }

    private IQueryable<SelfServeWorkspaceInfo> QueryWorkspaces(IQueryable<Workspace> source) =>
        from w in source
        join a in db.Accounts.AsNoTracking() on w.OwnerAccountId equals a.Id into owners
        from a in owners.DefaultIfEmpty()
        select new SelfServeWorkspaceInfo(
            w.Id, w.WorkspaceNo, w.BranchId, w.Name, w.Phone, w.Plan, w.Status,
            w.TrialStartsAt, w.TrialEndsAt, w.SampleDataLoadedAt, w.MobileLoginSeenAt, w.ExtensionRequestedAt,
            w.PurgeAfter, w.CreatedAt, w.OwnerAccountId, a == null ? null : a.Email);

    private async Task<Dictionary<int, DateTime?>> ReadBranchEndDatesAsync(CancellationToken ct)
    {
        if (!_legacy.ConnectionStrings.TryGetValue(_options.ShardKey, out var cs) || string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException(
                $"ไม่รู้จัก shard '{_options.ShardKey}' — ตั้งค่า {LegacyShardOptions.SectionName}:ConnectionStrings:{_options.ShardKey}");

        await using var connection = new SqlConnection(cs);
        var rows = await connection.QueryAsync<(int Id, DateTime? EndPackageDate)>(
            new CommandDefinition(BranchEndDatesSql, cancellationToken: ct));
        return rows.ToDictionary(r => r.Id, r => BranchPackageDates.ToUtc(r.EndPackageDate));
    }

    /// <summary>SaveChanges ที่ชน unique = คนอื่น claim ไปแล้ว → false · ถอดแถวออกจาก ChangeTracker ไม่ให้ SaveChanges ครั้งถัดไปยิงซ้ำ</summary>
    private async Task<bool> TrySaveClaimAsync(object entity, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(entity).State = EntityState.Detached;
            return false;
        }
    }

    private static string? Limit(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;
}
