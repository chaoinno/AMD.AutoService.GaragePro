using System.Globalization;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>
/// งานของทีมงานกับรายการแจ้งชำระ (docs/17 §7 · spadmin ระยะแรก · API ภายในของ GaragePro Admin ระยะ B ใช้ตัวเดียวกัน §8.6)
/// · [BIZ] อนุมัติ = ต่อวัน + ปิดรายการ + audit ใน transaction เดียว (ISelfServeAdminRepository.ApprovePaymentAsync)
///   วันหมดใหม่คำนวณตอนอนุมัติจากวันหมดล่าสุด (PackageRenewal) · trial → paid · Expired → Active · ล้าง PurgeAfter
/// · ยอดไม่ตรงราคาอนุมัติได้ (ส่วนลด/โปรโมชัน) แต่ต้องมีหมายเหตุ · ปฏิเสธต้องเลือกเหตุผล (other ต้องมีหมายเหตุ)
/// · อีเมล/แจ้งเตือนในระบบส่งหลัง commit (best effort) ผลส่งกลับให้ผู้เรียกพิมพ์
/// · ไม่มี Console/logging ที่นี่ (CLI/controller เป็นผู้แสดงผล)
/// </summary>
public sealed class PackagePaymentAdminService(
    IPackagePaymentRepository payments,
    ISelfServeAdminRepository admin,
    IPackagePaymentNotifier notifier,
    SelfServeOptions options,
    TimeProvider clock)
{
    public const int DefaultListTake = 100;
    public const int MaxListTake = 500;
    private const int MaxReferenceMatches = 10;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<IReadOnlyList<PackagePaymentAdminItem>>> ListAsync(PackagePaymentStatus? status,
        int? workspaceNo, int? take = null, CancellationToken ct = default)
    {
        var entries = await payments.ListAsync(status, workspaceNo, Math.Clamp(take ?? DefaultListTake, 1, MaxListTake), ct);
        if (entries.Count == 0) return Result<IReadOnlyList<PackagePaymentAdminItem>>.Ok([]);

        var duplicates = await payments.FindBySlipHashesAsync(entries.Select(e => e.Payment.SlipSha256).ToList(), ct);

        // วันหมดปัจจุบันต่ออู่ (อ่าน Branch ครั้งเดียว) · ใช้ทำพรีวิวเฉพาะรายการที่ยังรอตรวจ
        var ends = entries.Any(e => e.Payment.Status == PackagePaymentStatus.Pending)
            ? (await admin.ListAsync(null, null, ct)).ToDictionary(r => r.Id, r => r.EndsAt)
            : new Dictionary<Guid, DateTime?>();

        var now = Now;
        return Result<IReadOnlyList<PackagePaymentAdminItem>>.Ok(entries
            .Select(e => Item(e, duplicates, ends.GetValueOrDefault(e.Workspace.Id), now))
            .ToList());
    }

    public async Task<Result<PackagePaymentAdminDetail>> ShowAsync(string? reference, CancellationToken ct = default)
    {
        var (entry, error) = await ResolveAsync(reference, ct);
        if (error is not null) return Result<PackagePaymentAdminDetail>.Fail(error);

        var snapshot = await admin.GetAsync(entry!.Workspace.WorkspaceNo, 0, ct);
        var duplicates = await payments.FindBySlipHashesAsync([entry.Payment.SlipSha256], ct);
        var now = Now;
        var item = Item(entry, duplicates, snapshot is null ? null : SelfServeAdminService.EndsAt(snapshot), now);

        return Result<PackagePaymentAdminDetail>.Ok(new PackagePaymentAdminDetail(item,
            PackageSlipLinks.Create(options.Package, entry.Payment.Id, now),
            entry.Payment.ReviewedBy, entry.Payment.ReviewNote, entry.Payment.SlipPath));
    }

    /// <param name="until">ทีมงานกำหนดวันสุดท้ายที่ใช้ได้เอง (เช่น แถมวัน) · ต้องหลังจุดเริ่มนับ และไม่เกิน MaxExtendDays</param>
    public async Task<Result<PackagePaymentReviewResult>> ApproveAsync(string? reference, DateOnly? until, string? note,
        string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var (entry, error) = await ResolveAsync(reference, ct);
        if (error is not null) return Fail(error);
        var payment = entry!.Payment;
        if (payment.Status != PackagePaymentStatus.Pending) return Fail(PackageErrors.NotPending());

        var snapshot = await admin.GetAsync(entry.Workspace.WorkspaceNo, 0, ct);
        if (snapshot is null) return Fail(SelfServeLifecycleErrors.WorkspaceNotFound());
        var ws = snapshot.Workspace;

        // กฎข้อ 10 (รหัสเดียวกับฝั่งลูกค้า) ก่อนเหตุผลอื่นของการเปลี่ยนแพ็กเกจ
        if (ws.Plan is WorkspacePlanKind.Converted or WorkspacePlanKind.Suspended)
            return Fail(PackageErrors.NotRenewable(ws.Plan == WorkspacePlanKind.Converted
                ? "อู่นี้ย้ายไปสาขา GaragePro แล้ว (converted) ต่ออายุที่สาขาปลายทาง"
                : "อู่นี้ถูกระงับอยู่ ยกเลิกการระงับก่อน (spadmin set-plan) แล้วค่อยอนุมัติ"));
        if (SelfServeAdminService.ChangeRefusal(snapshot, options.ShardKey, admin.CheckWritableDatabase) is { } refusal)
            return Fail(refusal);

        var current = SelfServeAdminService.EndsAt(snapshot);
        if (current is null && ws.Plan == WorkspacePlanKind.Paid)
            return Fail(PackageErrors.NotRenewable("แพ็กเกจ paid ของอู่นี้ไม่มีวันหมดอายุ ไม่ต้องต่อ"));

        var now = Now;
        var period = PackageRenewal.Compute(current, now, payment.MonthCount);
        var endsAt = period.Ends;
        if (until is { } untilDate)
        {
            endsAt = SelfServeAdminService.UntilToEndsAtUtc(untilDate);
            if (endsAt <= period.Starts)
                return Fail(PackageErrors.Validation(
                    $"วันใช้ได้ถึงต้องหลังวันเริ่มนับ {Iso(SelfServeText.ThaiDate(period.Starts))} " +
                    $"(วันหมดเดิมหรือวันนี้ ถ้าหมดอายุแล้ว) · ไม่กำหนดเอง = ต่อ {payment.MonthCount} เดือนถึง " +
                    $"{Iso(period.LastUsableDate)}", "until"));
            if ((endsAt - now).TotalDays > SelfServeAdminService.MaxExtendDays)
                return Fail(PackageErrors.Validation(
                    $"วันใช้ได้ถึงต้องไม่เกิน {SelfServeAdminService.MaxExtendDays} วันจากวันนี้", "until"));
        }

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote is { Length: > PackagePaymentRules.MaxReviewNoteLength })
            return Fail(PackageErrors.Validation($"หมายเหตุยาวเกิน {PackagePaymentRules.MaxReviewNoteLength} ตัวอักษร", "note"));
        if (payment.ExpectedAmount is { } expected && payment.Amount != expected && trimmedNote is null)
            return Fail(PackageErrors.Validation(
                $"ยอดที่โอน {PackagePaymentMessages.Baht(payment.Amount)} ไม่ตรงราคามาตรฐาน {PackagePaymentMessages.Baht(expected)} " +
                "ต้องใส่หมายเหตุการอนุมัติ เช่น ส่วนลด/โปรโมชัน", "note"));

        var statusTo = SelfServeAdminService.Reactivated(ws.Status, endsAt, now);
        var payload = JsonSerializer.Serialize(new
        {
            paymentId = payment.Id,
            months = payment.MonthCount,
            amount = payment.Amount,
            expectedAmount = payment.ExpectedAmount,
            from = current,
            baseAt = period.Starts,
            to = endsAt,
            until = until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            planFrom = ws.Plan.ToString(),
            planTo = nameof(WorkspacePlanKind.Paid),
            statusFrom = ws.Status.ToString(),
            statusTo = statusTo.ToString()
        });

        var change = new WorkspacePackageChange(ws.Id, ws.BranchId!.Value, SetBranchEnd: true, endsAt,
            WorkspacePlanKind.Paid, statusTo, ClearPurgeAfter: true, actor, PackagePaymentAuditActions.Approve,
            trimmedNote, payload, now);
        var approval = new PackagePaymentApproval(payment.Id, snapshot.Branch!.EndsAtUtc, actor, now, trimmedNote,
            current, period.Starts, endsAt);

        var outcome = await admin.ApprovePaymentAsync(change, approval, ct);
        switch (outcome)
        {
            case ApprovePaymentOutcome.PaymentNotPending: return Fail(PackageErrors.NotPending());
            case ApprovePaymentOutcome.BranchEndChanged: return Fail(PackageErrors.ApprovalConflict());
        }

        var approved = await payments.GetEntryAsync(payment.Id, ct) ?? entry;
        // commit แล้ว: แจ้งเจ้าของอู่ต่อแม้ผู้เรียกยกเลิก (best effort · อีเมล/LINE มี timeout ของตัวเอง)
        var notify = await notifier.ReviewedAsync(new PackagePaymentReviewedNotice(
            NoticeContext(approved, snapshot), endsAt), CancellationToken.None);

        return Result<PackagePaymentReviewResult>.Ok(new PackagePaymentReviewResult(
            Item(approved, [], null, now), current, endsAt, notify.EmailSent, notify.InAppSaved));
    }

    public async Task<Result<PackagePaymentReviewResult>> RejectAsync(string? reference, string? reason, string? note,
        string actor, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (!PackagePaymentRules.TryParseRejectReason(reason, out var rejectReason))
            return Fail(PackageErrors.Validation(
                $"เหตุผลต้องเป็น {string.Join(" | ", PackagePaymentRules.RejectReasonCodes)}", "reason"));
        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (rejectReason == PackagePaymentRejectReason.Other && trimmedNote is null)
            return Fail(PackageErrors.Validation("เหตุผล \"อื่นๆ\" ต้องมีหมายเหตุให้ลูกค้าอ่าน", "note"));
        if (trimmedNote is { Length: > PackagePaymentRules.MaxReviewNoteLength })
            return Fail(PackageErrors.Validation($"หมายเหตุยาวเกิน {PackagePaymentRules.MaxReviewNoteLength} ตัวอักษร", "note"));

        var (entry, error) = await ResolveAsync(reference, ct);
        if (error is not null) return Fail(error);
        var payment = entry!.Payment;
        if (payment.Status != PackagePaymentStatus.Pending) return Fail(PackageErrors.NotPending());

        var now = Now;
        var rejected = await payments.TryRejectAsync(payment.Id, payment.WorkspaceId, rejectReason, trimmedNote, now,
            new PackagePaymentAuditInfo(actor, trimmedNote, JsonSerializer.Serialize(new
            {
                paymentId = payment.Id,
                reason = PackagePaymentRules.RejectReasonCode(rejectReason)
            })), ct);
        if (!rejected) return Fail(PackageErrors.NotPending());

        var updated = await payments.GetEntryAsync(payment.Id, ct) ?? entry;
        var snapshot = await admin.GetAsync(updated.Workspace.WorkspaceNo, 0, ct);
        var notify = await notifier.ReviewedAsync(new PackagePaymentReviewedNotice(
            NoticeContext(updated, snapshot), null), CancellationToken.None);

        return Result<PackagePaymentReviewResult>.Ok(new PackagePaymentReviewResult(
            Item(updated, [], null, now), null, null, notify.EmailSent, notify.InAppSaved));
    }

    /// <summary>id เต็มหรือ ≥ 8 ตัวแรก · สั้นเกิน/ไม่ใช่เลขฐานสิบหก = validation · ตรงหลายรายการ = validation พร้อมรายการที่ตรง</summary>
    public async Task<(PackagePaymentEntry? Entry, ApiError? Error)> ResolveAsync(string? reference, CancellationToken ct)
    {
        if (PackagePaymentReference.Normalize(reference) is null)
            return (null, PackageErrors.Validation(
                $"--id ต้องเป็น id ของรายการ หรือ {PackagePaymentReference.MinPrefixLength} ตัวอักษรแรกขึ้นไป (เลขฐานสิบหก)", "id"));

        var matches = await payments.FindByReferenceAsync(reference!, MaxReferenceMatches, ct);
        return matches.Count switch
        {
            0 => (null, PackageErrors.NotFound()),
            1 => (matches[0], null),
            _ => (null, new ApiError(PackageErrors.ValidationCode,
                $"--id ตรงหลายรายการ ใส่ให้ยาวขึ้น: {string.Join(", ", matches.Select(m =>
                    $"{m.Payment.Id:D} ({SelfServeText.WorkspaceCode(m.Workspace.WorkspaceNo)} · {PackagePaymentRules.StatusCode(m.Payment.Status)})"))}",
                "id", matches.Select(m => m.Payment.Id).ToArray()))
        };
    }

    public static PackagePaymentDuplicateDto Duplicate(PackagePaymentEntry entry) =>
        new(entry.Payment.Id, SelfServeText.WorkspaceCode(entry.Workspace.WorkspaceNo),
            PackagePaymentRules.StatusCode(entry.Payment.Status), entry.Payment.SubmittedAt);

    private PackagePaymentAdminItem Item(PackagePaymentEntry entry, IReadOnlyList<PackagePaymentEntry> withSameHash,
        DateTime? currentEndsAt, DateTime now)
    {
        var p = entry.Payment;
        var duplicates = withSameHash
            .Where(d => d.Payment.Id != p.Id && d.Payment.SlipSha256 == p.SlipSha256)
            .Select(Duplicate)
            .ToList();

        var renewable = p.Status == PackagePaymentStatus.Pending
                        && entry.Workspace.Plan is WorkspacePlanKind.Trial or WorkspacePlanKind.Paid
                        && entry.Workspace.Status is WorkspaceStatus.Active or WorkspaceStatus.Expired
                        && !(entry.Workspace.Plan == WorkspacePlanKind.Paid && currentEndsAt is null);
        PackageRenewalPreviewDto? preview = null;
        if (renewable)
        {
            var period = PackageRenewal.Compute(currentEndsAt, now, p.MonthCount);
            preview = new PackageRenewalPreviewDto(period.Starts, period.Ends, period.LastUsableDate);
        }

        return new PackagePaymentAdminItem(
            PackagePaymentMapper.ToDto(p, includeFinancials: true),
            SelfServeText.WorkspaceCode(entry.Workspace.WorkspaceNo),
            entry.Workspace.Name,
            entry.Workspace.BranchId,
            entry.Workspace.Plan.ToString().ToLowerInvariant(),
            entry.Workspace.Status.ToString().ToLowerInvariant(),
            currentEndsAt,
            duplicates,
            preview,
            p.ReviewedBy,
            p.ReviewNote,
            p.CancelledBy);
    }

    private static PackagePaymentNoticeContext NoticeContext(PackagePaymentEntry entry, AdminWorkspaceSnapshot? snapshot) =>
        new(entry.Payment, entry.Workspace.Id, entry.Workspace.WorkspaceNo, entry.Workspace.Name,
            snapshot?.Workspace.Phone, entry.Workspace.BranchId ?? 0, snapshot?.Owner?.Email);

    private static Result<PackagePaymentReviewResult> Fail(ApiError error) => Result<PackagePaymentReviewResult>.Fail(error);

    /// <summary>ค.ศ. เสมอ (ค่าเดียวกับที่ทีมงานพิมพ์ใน --until) ไม่พึ่ง culture ของเครื่อง (Mac ที่ตั้งภาษาไทยได้ปี พ.ศ.)</summary>
    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
