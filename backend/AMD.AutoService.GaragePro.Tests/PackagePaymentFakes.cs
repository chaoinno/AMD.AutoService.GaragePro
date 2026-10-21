using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

// ของจำลองที่ใช้ร่วมกันระหว่าง PackagePaymentServiceTests กับ PackagePaymentAdminServiceTests (ไม่ต่อฐาน/FTP/LINE)

public sealed class PackageTestClock(DateTime now) : TimeProvider
{
    public DateTime Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => new(Now);
}

public sealed class PackageTestUser(UserRole role = UserRole.Manager, string shard = "sp1", int branchId = 61)
    : ICurrentUser
{
    public long UserId => 501;
    public string UserName => "SP1042.M01";
    public UserRole Role { get; set; } = role;
    public string ShardKey => shard;
    public int BranchId => branchId;
    public EventSource Source => EventSource.Web;
    public bool IsAdministrator => false;
    public Guid? SessionId => null;
}

/// <summary>
/// sp_PackagePayment ในหน่วยความจำ — จำลอง filtered unique index (รอตรวจได้ครั้งละหนึ่ง) และ (workspace, requestId) ที่ฐานจริงบังคับ
/// · <see cref="RaceOnInsert"/> จำลองอีกแท็บ INSERT ตัดหน้าระหว่าง pre-check กับ INSERT
/// </summary>
public sealed class InMemoryPackagePayments : IPackagePaymentRepository
{
    public List<PackagePayment> Rows { get; } = [];
    public Dictionary<Guid, PackagePaymentWorkspace> Workspaces { get; } = [];
    public List<(string Action, PackagePaymentAuditInfo Audit)> Audits { get; } = [];
    public PackagePayment? RaceOnInsert { get; set; }
    public int Reads { get; private set; }

    public PackagePayment Seed(Guid workspaceId, PackagePaymentStatus status = PackagePaymentStatus.Pending,
        string? requestId = null, string sha = "aa", decimal amount = 590m, decimal? expected = 590m, int months = 1,
        DateTime? submittedAt = null, Guid? id = null)
    {
        var p = new PackagePayment
        {
            Id = id ?? Guid.NewGuid(),
            WorkspaceId = workspaceId,
            RequestId = requestId ?? Guid.NewGuid().ToString(),
            Status = status,
            MonthCount = months,
            Amount = amount,
            ExpectedAmount = expected,
            PaidAt = new DateTime(2026, 10, 20, 3, 0, 0, DateTimeKind.Utc),
            SlipPath = $"sp1/61/package-slip/{Guid.NewGuid():N}/{Guid.NewGuid():N}.jpg",
            SlipSha256 = sha,
            SlipContentType = "image/jpeg",
            SlipSizeBytes = 1234,
            Note = "หมายเหตุลูกค้า",
            SubmittedByUserId = 501,
            SubmittedByUserName = "SP1042.M01",
            SubmittedAt = submittedAt ?? new DateTime(2026, 10, 20, 4, 0, 0, DateTimeKind.Utc)
        };
        Rows.Add(p);
        return p;
    }

    private PackagePaymentEntry Entry(PackagePayment p) => new(Copy(p), Workspaces[p.WorkspaceId]);

    private static PackagePayment Copy(PackagePayment p) => (PackagePayment)typeof(PackagePayment)
        .GetMethod("MemberwiseClone", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
        .Invoke(p, null)!;

    public Task<PackagePayment?> GetAsync(Guid id, CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult(Rows.FirstOrDefault(p => p.Id == id) is { } p ? Copy(p) : null);
    }

    public Task<PackagePaymentEntry?> GetEntryAsync(Guid id, CancellationToken ct = default)
    {
        Reads++;
        return Task.FromResult(Rows.FirstOrDefault(p => p.Id == id) is { } p ? Entry(p) : null);
    }

    public Task<PackagePayment?> FindByRequestAsync(Guid workspaceId, string requestId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(p => p.WorkspaceId == workspaceId && p.RequestId == requestId) is { } p
            ? Copy(p)
            : null);

    public Task<PackagePayment?> FindPendingAsync(Guid workspaceId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(p => p.WorkspaceId == workspaceId && p.Status == PackagePaymentStatus.Pending)
            is { } p ? Copy(p) : null);

    public Task<IReadOnlyList<PackagePayment>> ListForWorkspaceAsync(Guid workspaceId, int take,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PackagePayment>>(Rows.Where(p => p.WorkspaceId == workspaceId)
            .OrderByDescending(p => p.SubmittedAt).Take(take).Select(Copy).ToList());

    public Task<PackagePaymentInsertResult> TryInsertAsync(PackagePayment payment, PackagePaymentAuditInfo audit,
        CancellationToken ct = default)
    {
        if (RaceOnInsert is { } race)
        {
            Rows.Add(race);
            RaceOnInsert = null;
        }

        if (Rows.FirstOrDefault(p => p.WorkspaceId == payment.WorkspaceId && p.RequestId == payment.RequestId) is { } same)
            return Task.FromResult(new PackagePaymentInsertResult(PackagePaymentInsertOutcome.DuplicateRequest, Copy(same)));
        if (Rows.FirstOrDefault(p => p.WorkspaceId == payment.WorkspaceId && p.Status == PackagePaymentStatus.Pending)
            is { } pending)
            return Task.FromResult(new PackagePaymentInsertResult(PackagePaymentInsertOutcome.PendingExists, Copy(pending)));

        Rows.Add(Copy(payment));
        Audits.Add((PackagePaymentAuditActions.Submit, audit));
        return Task.FromResult(new PackagePaymentInsertResult(PackagePaymentInsertOutcome.Inserted, payment));
    }

    public Task<bool> TryCancelAsync(Guid id, Guid workspaceId, DateTime now, PackagePaymentAuditInfo audit,
        CancellationToken ct = default)
    {
        var row = Rows.FirstOrDefault(p => p.Id == id && p.WorkspaceId == workspaceId && p.Status == PackagePaymentStatus.Pending);
        if (row is null) return Task.FromResult(false);
        row.Status = PackagePaymentStatus.Cancelled;
        row.CancelledAt = now;
        row.CancelledBy = audit.Actor;
        Audits.Add((PackagePaymentAuditActions.Cancel, audit));
        return Task.FromResult(true);
    }

    public bool RejectFails { get; set; }

    public Task<bool> TryRejectAsync(Guid id, Guid workspaceId, PackagePaymentRejectReason reason, string? reviewNote,
        DateTime now, PackagePaymentAuditInfo audit, CancellationToken ct = default)
    {
        var row = Rows.FirstOrDefault(p => p.Id == id && p.WorkspaceId == workspaceId && p.Status == PackagePaymentStatus.Pending);
        if (row is null || RejectFails) return Task.FromResult(false);
        row.Status = PackagePaymentStatus.Rejected;
        row.RejectReason = reason;
        row.ReviewNote = reviewNote;
        row.ReviewedBy = audit.Actor;
        row.ReviewedAt = now;
        Audits.Add((PackagePaymentAuditActions.Reject, audit));
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<PackagePaymentEntry>> ListAsync(PackagePaymentStatus? status, int? workspaceNo, int take,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PackagePaymentEntry>>(Rows
            .Where(p => status is null || p.Status == status)
            .Where(p => workspaceNo is null || Workspaces[p.WorkspaceId].WorkspaceNo == workspaceNo)
            .OrderByDescending(p => p.SubmittedAt).Take(take).Select(Entry).ToList());

    public Task<IReadOnlyList<PackagePaymentEntry>> FindByReferenceAsync(string reference, int max,
        CancellationToken ct = default)
    {
        var hex = PackagePaymentReference.Normalize(reference);
        return Task.FromResult<IReadOnlyList<PackagePaymentEntry>>(hex is null
            ? []
            : Rows.Where(p => p.Id.ToString("N").StartsWith(hex, StringComparison.Ordinal)).Take(max).Select(Entry).ToList());
    }

    public Task<IReadOnlyList<PackagePaymentEntry>> FindBySlipHashesAsync(IReadOnlyCollection<string> sha256s,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<PackagePaymentEntry>>(Rows.Where(p => sha256s.Contains(p.SlipSha256))
            .Select(Entry).ToList());
}

public sealed class FakeSlipStorage : IPackageSlipStorage
{
    public List<(string Shard, int Branch, Guid PaymentId)> Saved { get; } = [];
    public List<string> Deleted { get; } = [];
    public Exception? SaveThrows { get; set; }
    public string Sha { get; set; } = "bb";
    public string ContentType { get; set; } = "image/jpeg";
    public Dictionary<string, byte[]> Files { get; } = [];

    public long MaxSizeBytes => 15 * 1024 * 1024;

    public AttachmentValidation Validate(string? contentType, long sizeBytes) =>
        sizeBytes <= 0 ? new(false, PackageErrors.AttachmentEmptyCode, "ว่าง")
        : sizeBytes > MaxSizeBytes ? new(false, PackageErrors.AttachmentTooLargeCode, "ใหญ่")
        : contentType == "image/gif" ? new(false, PackageErrors.AttachmentTypeInvalidCode, "ชนิด")
        : new(true, null, null);

    public Task<StoredSlip> SaveAsync(Stream content, string shardKey, int branchId, Guid paymentId,
        CancellationToken ct = default)
    {
        if (SaveThrows is { } ex) throw ex;
        Saved.Add((shardKey, branchId, paymentId));
        var path = $"{shardKey}/{branchId}/package-slip/{paymentId:N}/{Guid.NewGuid():N}.jpg";
        Files[path] = [1, 2, 3];
        return Task.FromResult(new StoredSlip(Guid.NewGuid(), path, 3, ContentType, Sha));
    }

    public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default) =>
        Task.FromResult<Stream?>(Files.TryGetValue(relativePath, out var bytes) ? new MemoryStream(bytes) : null);

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        Deleted.Add(relativePath);
        return Task.CompletedTask;
    }
}

public sealed class FakePackageNotifier : IPackagePaymentNotifier
{
    public List<PackagePaymentSubmittedNotice> Submitted { get; } = [];
    public List<PackagePaymentNoticeContext> Cancelled { get; } = [];
    public List<PackagePaymentReviewedNotice> Reviewed { get; } = [];
    public PackageReviewNotifyOutcome Outcome { get; set; } = new(true, true);

    public Task<bool> SubmittedAsync(PackagePaymentSubmittedNotice notice, CancellationToken ct = default)
    {
        Submitted.Add(notice);
        return Task.FromResult(true);
    }

    public Task<bool> CancelledAsync(PackagePaymentNoticeContext context, CancellationToken ct = default)
    {
        Cancelled.Add(context);
        return Task.FromResult(true);
    }

    public Task<PackageReviewNotifyOutcome> ReviewedAsync(PackagePaymentReviewedNotice notice, CancellationToken ct = default)
    {
        Reviewed.Add(notice);
        return Task.FromResult(Outcome);
    }
}

/// <summary>ใช้แค่ FindByBranchAsync (ส่วนอื่นของงานรายวันไม่เกี่ยว)</summary>
public sealed class PackageTestWorkspaces : ISelfServeLifecycleRepository
{
    public SelfServeWorkspaceInfo? Workspace { get; set; }

    public Task<SelfServeWorkspaceInfo?> FindByBranchAsync(int branchId, CancellationToken ct = default) =>
        Task.FromResult(Workspace?.BranchId == branchId ? Workspace : null);

    public Task<IReadOnlyList<LifecycleWorkspace>> ListForLifecycleAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<bool> TryRecordExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime notBefore,
        string actor, string? note, CancellationToken ct = default) => throw new NotSupportedException();

    public Task RevertExtensionRequestAsync(Guid workspaceId, DateTime requestedAt, DateTime? previous,
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task<int> CountQuotationsEverSentAsync(string shardKey, int branchId,
        IReadOnlyCollection<Guid>? excludeQuotationIds = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<SampleLedgerItem>> GetSampleEntitiesAsync(Guid workspaceId, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<bool> TryAddNoticeAsync(Guid workspaceId, string kind, DateTime sentAt, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task RemoveNoticeAsync(Guid workspaceId, string kind, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task SetStatusAsync(Guid workspaceId, WorkspaceStatus status, DateTime? purgeAfter, DateTime now, string actor,
        string auditAction, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<bool> TryClaimJobRunAsync(string jobName, DateOnly runDate, DateTime startedAt, string? instanceName,
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task CompleteJobRunAsync(string jobName, DateOnly runDate, DateTime finishedAt, string outcome, string? error,
        CancellationToken ct = default) => throw new NotSupportedException();

    public Task<DateTime?> LastSuccessfulRunAsync(string jobName, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> ListAbandonedNeedingServicePurgeAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<int> UpsertBlockedDomainsAsync(IReadOnlyCollection<string> domains, string source, DateTime now,
        CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>
/// ISelfServeAdminRepository จำลอง — <see cref="ApprovePaymentAsync"/> ทำผลกับ <see cref="Payments"/> ตาม <see cref="NextOutcome"/>
/// (Applied = เปลี่ยนรายการเป็น Approved + บันทึก change ไว้ตรวจ)
/// </summary>
public sealed class FakePackageAdminRepository(InMemoryPackagePayments payments) : ISelfServeAdminRepository
{
    public InMemoryPackagePayments Payments { get; } = payments;
    public Dictionary<int, AdminWorkspaceSnapshot> Snapshots { get; } = [];
    public string? Mismatch { get; set; }
    public ApprovePaymentOutcome NextOutcome { get; set; } = ApprovePaymentOutcome.Applied;
    public List<(WorkspacePackageChange Change, PackagePaymentApproval Approval)> Approvals { get; } = [];

    public Task<IReadOnlyList<AdminWorkspaceRow>> ListAsync(WorkspaceStatus? status, WorkspacePlanKind? plan,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<AdminWorkspaceRow>>(Snapshots.Values.Select(s => new AdminWorkspaceRow(
            s.Workspace.Id, s.Workspace.WorkspaceNo, s.Workspace.BranchId, s.Workspace.Name, s.Workspace.Phone,
            s.Workspace.Plan, s.Workspace.Status, SelfServeAdminService.EndsAt(s), s.Workspace.CreatedAt, s.Owner?.Email,
            s.Workspace.PurgeAfter, null)).ToList());

    public Task<AdminWorkspaceSnapshot?> GetAsync(int workspaceNo, int auditTail, CancellationToken ct = default) =>
        Task.FromResult(Snapshots.GetValueOrDefault(workspaceNo));

    public string? CheckWritableDatabase() => Mismatch;

    public Task ApplyPackageChangeAsync(WorkspacePackageChange change, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<ApprovePaymentOutcome> ApprovePaymentAsync(WorkspacePackageChange change, PackagePaymentApproval approval,
        CancellationToken ct = default)
    {
        Approvals.Add((change, approval));
        if (NextOutcome == ApprovePaymentOutcome.Applied)
        {
            var row = Payments.Rows.Single(p => p.Id == approval.PaymentId);
            row.Status = PackagePaymentStatus.Approved;
            row.ReviewedBy = approval.ReviewedBy;
            row.ReviewedAt = approval.ReviewedAt;
            row.ReviewNote = approval.ReviewNote;
            row.PrevEndsAt = approval.PrevEndsAt;
            row.PeriodStartsAt = approval.PeriodStartsAt;
            row.PeriodEndsAt = approval.PeriodEndsAt;
        }

        return Task.FromResult(NextOutcome);
    }
}
