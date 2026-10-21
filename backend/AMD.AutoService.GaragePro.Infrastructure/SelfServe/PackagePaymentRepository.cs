using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// sp_PackagePayment ผ่าน <see cref="SelfServeDbContext"/> (docs/17 §3)
/// · เปลี่ยนสถานะด้วย UPDATE แบบมีเงื่อนไข <c>WHERE Status = Pending</c> + audit ใน transaction เดียว
///   ห้าม BeginTransactionAsync ตรงๆ (EnableRetryOnFailure) จึงห่อด้วย execution strategy
/// · retry ของ strategy หลัง commit สำเร็จแต่เน็ตหลุดตอนตอบกลับ: UPDATE รอบสองได้ 0 แถว → ตรวจซ้ำว่าเป็นผลของคำขอนี้เอง
///   (เวลา + ผู้กระทำตรงกัน) แล้วนับว่าสำเร็จ ไม่ใช่ตอบว่า "ไม่ได้รอตรวจแล้ว"
/// </summary>
public sealed class PackagePaymentRepository(SelfServeDbContext db) : IPackagePaymentRepository
{
    public Task<PackagePayment?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.PackagePayments.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);

    public async Task<PackagePaymentEntry?> GetEntryAsync(Guid id, CancellationToken ct = default) =>
        (await Entries(db.PackagePayments.AsNoTracking().Where(p => p.Id == id)).ToListAsync(ct))
        .Select(ToEntry).FirstOrDefault();

    public Task<PackagePayment?> FindByRequestAsync(Guid workspaceId, string requestId, CancellationToken ct = default) =>
        db.PackagePayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.WorkspaceId == workspaceId && p.RequestId == requestId, ct);

    public Task<PackagePayment?> FindPendingAsync(Guid workspaceId, CancellationToken ct = default) =>
        db.PackagePayments.AsNoTracking()
            .FirstOrDefaultAsync(p => p.WorkspaceId == workspaceId && p.Status == PackagePaymentStatus.Pending, ct);

    public async Task<IReadOnlyList<PackagePayment>> ListForWorkspaceAsync(Guid workspaceId, int take,
        CancellationToken ct = default) =>
        await db.PackagePayments.AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId)
            .OrderByDescending(p => p.SubmittedAt).ThenByDescending(p => p.Id)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(ct);

    public async Task<PackagePaymentInsertResult> TryInsertAsync(PackagePayment payment, PackagePaymentAuditInfo audit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(payment);
        ArgumentNullException.ThrowIfNull(audit);

        db.PackagePayments.Add(payment);
        db.WorkspaceAudits.Add(Audit(payment.WorkspaceId, PackagePaymentAuditActions.Submit, audit, payment.SubmittedAt));
        try
        {
            // INSERT สองแถวใน SaveChanges เดียว = transaction ของ EF เอง · ชน unique ทั้งก้อน rollback
            await db.SaveChangesAsync(ct);
            return new PackagePaymentInsertResult(PackagePaymentInsertOutcome.Inserted, payment);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.ChangeTracker.Clear();

            // ชนได้สองแบบ (requestId เดิม · รอตรวจซ้อน) แยกด้วยการอ่านซ้ำ ไม่ parse ชื่อ index จากข้อความ SQL
            var existing = await FindByRequestAsync(payment.WorkspaceId, payment.RequestId, ct);
            if (existing is not null)
                return new PackagePaymentInsertResult(PackagePaymentInsertOutcome.DuplicateRequest, existing);

            var pending = await FindPendingAsync(payment.WorkspaceId, ct);
            if (pending is not null)
                return new PackagePaymentInsertResult(PackagePaymentInsertOutcome.PendingExists, pending);

            throw;
        }
    }

    public Task<bool> TryCancelAsync(Guid id, Guid workspaceId, DateTime now, PackagePaymentAuditInfo audit,
        CancellationToken ct = default) =>
        TransitionAsync(id, workspaceId, PackagePaymentAuditActions.Cancel, audit, now,
            q => q.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PackagePaymentStatus.Cancelled)
                .SetProperty(p => p.CancelledAt, now)
                .SetProperty(p => p.CancelledBy, Limit(audit.Actor, 200)), ct),
            p => p.Status == PackagePaymentStatus.Cancelled && p.CancelledAt == now && p.CancelledBy == Limit(audit.Actor, 200),
            ct);

    public Task<bool> TryRejectAsync(Guid id, Guid workspaceId, PackagePaymentRejectReason reason, string? reviewNote,
        DateTime now, PackagePaymentAuditInfo audit, CancellationToken ct = default)
    {
        var actor = Limit(audit.Actor, 200);
        var note = Limit(reviewNote, 1000);
        return TransitionAsync(id, workspaceId, PackagePaymentAuditActions.Reject, audit, now,
            q => q.ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Status, PackagePaymentStatus.Rejected)
                .SetProperty(p => p.RejectReason, (PackagePaymentRejectReason?)reason)
                .SetProperty(p => p.ReviewNote, note)
                .SetProperty(p => p.ReviewedBy, actor)
                .SetProperty(p => p.ReviewedAt, (DateTime?)now), ct),
            p => p.Status == PackagePaymentStatus.Rejected && p.ReviewedAt == now && p.ReviewedBy == actor,
            ct);
    }

    public async Task<IReadOnlyList<PackagePaymentEntry>> ListAsync(PackagePaymentStatus? status, int? workspaceNo,
        int take, CancellationToken ct = default)
    {
        var payments = db.PackagePayments.AsNoTracking();
        if (status is { } s) payments = payments.Where(p => p.Status == s);

        var query = Entries(payments);
        if (workspaceNo is { } no) query = query.Where(x => x.WorkspaceNo == no);

        return (await query
                .OrderByDescending(x => x.Payment.SubmittedAt).ThenByDescending(x => x.Payment.Id)
                .Take(Math.Clamp(take, 1, 500))
                .ToListAsync(ct))
            .Select(ToEntry).ToList();
    }

    public async Task<IReadOnlyList<PackagePaymentEntry>> FindByReferenceAsync(string reference, int max,
        CancellationToken ct = default)
    {
        var hex = PackagePaymentReference.Normalize(reference);
        if (hex is null) return [];

        if (hex.Length == 32)
            return await GetEntryAsync(Guid.ParseExact(hex, "N"), ct) is { } exact ? [exact] : [];

        // SQL Server แปลง uniqueidentifier เป็นข้อความรูปแบบเดียวกับ Guid.ToString("D") ของ .NET (ตัวพิมพ์ใหญ่)
        var pattern = PackagePaymentReference.ToDisplayPrefix(hex).ToUpperInvariant() + "%";
        return (await Entries(db.PackagePayments.AsNoTracking()
                    .Where(p => EF.Functions.Like(p.Id.ToString(), pattern)))
                .OrderByDescending(x => x.Payment.SubmittedAt)
                .Take(Math.Clamp(max, 1, 50))
                .ToListAsync(ct))
            .Select(ToEntry).ToList();
    }

    public async Task<IReadOnlyList<PackagePaymentEntry>> FindBySlipHashesAsync(IReadOnlyCollection<string> sha256s,
        CancellationToken ct = default)
    {
        var hashes = sha256s.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim().ToLowerInvariant())
            .Distinct().ToList();
        if (hashes.Count == 0) return [];

        return (await Entries(db.PackagePayments.AsNoTracking().Where(p => hashes.Contains(p.SlipSha256)))
                .OrderBy(x => x.Payment.SubmittedAt)
                .ToListAsync(ct))
            .Select(ToEntry).ToList();
    }

    private async Task<bool> TransitionAsync(Guid id, Guid workspaceId, string action, PackagePaymentAuditInfo audit,
        DateTime now, Func<IQueryable<PackagePayment>, Task<int>> update,
        Func<PackagePayment, bool> alreadyAppliedByThisRequest, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(audit);
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var rows = await update(db.PackagePayments.Where(p =>
                p.Id == id && p.WorkspaceId == workspaceId && p.Status == PackagePaymentStatus.Pending));
            if (rows == 0)
            {
                var current = await db.PackagePayments.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == id && p.WorkspaceId == workspaceId, ct);
                return current is not null && alreadyAppliedByThisRequest(current);
            }

            db.WorkspaceAudits.Add(Audit(workspaceId, action, audit, now));
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        });
    }

    private IQueryable<EntryRow> Entries(IQueryable<PackagePayment> payments) =>
        from p in payments
        join w in db.Workspaces.AsNoTracking() on p.WorkspaceId equals w.Id
        select new EntryRow
        {
            Payment = p,
            WorkspaceId = w.Id,
            WorkspaceNo = w.WorkspaceNo,
            Name = w.Name,
            BranchId = w.BranchId,
            Plan = w.Plan,
            Status = w.Status
        };

    private static PackagePaymentEntry ToEntry(EntryRow r) =>
        new(r.Payment, new PackagePaymentWorkspace(r.WorkspaceId, r.WorkspaceNo, r.Name, r.BranchId, r.Plan, r.Status));

    internal static WorkspaceAudit Audit(Guid workspaceId, string action, PackagePaymentAuditInfo audit, DateTime now) => new()
    {
        WorkspaceId = workspaceId,
        Action = action,
        Actor = Limit(audit.Actor, 200) ?? "system",
        Note = Limit(audit.Note, 1000),
        PayloadJson = audit.PayloadJson,
        CreatedAt = now
    };

    private static string? Limit(string? value, int max) =>
        value is { Length: > 0 } && value.Length > max ? value[..max] : value;

    private sealed class EntryRow
    {
        public required PackagePayment Payment { get; init; }
        public Guid WorkspaceId { get; init; }
        public int WorkspaceNo { get; init; }
        public string Name { get; init; } = string.Empty;
        public int? BranchId { get; init; }
        public WorkspacePlanKind Plan { get; init; }
        public WorkspaceStatus Status { get; init; }
    }
}
