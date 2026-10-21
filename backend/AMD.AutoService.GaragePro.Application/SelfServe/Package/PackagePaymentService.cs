using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>endpoint <c>/api/v1/selfserve/package/*</c> ของอู่ที่ login อยู่ (docs/17 §5.1)</summary>
public interface IPackagePaymentService
{
    Task<Result<PackageOverviewDto>> GetOverviewAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<PackagePaymentDto>>> ListPaymentsAsync(int? take, CancellationToken ct = default);
    Task<Result<PackagePaymentDto>> SubmitAsync(PackagePaymentSubmission submission, CancellationToken ct = default);
    Task<Result<PackagePaymentDto>> CancelAsync(string? paymentId, CancellationToken ct = default);
    Task<Result<PackageSlipFile>> OpenSlipAsync(string? paymentId, CancellationToken ct = default);

    /// <summary>ลิงก์มีลายเซ็นที่ LINE ใช้ดึงรูป · null ทุกกรณีที่ไม่ผ่าน (controller ตอบ 404 อย่างเดียว)</summary>
    Task<PackageSlipFile?> OpenSignedSlipAsync(string? paymentId, string? exp, string? sig, CancellationToken ct = default);
}

/// <summary>
/// แจ้งชำระเงินของอู่ที่สมัครเอง (docs/17 §2, §5.1)
/// · shard/สาขา/ผู้แจ้งมาจาก JWT เสมอ ห้ามรับจาก body · shard ≠ sp1 หรือไม่มี workspace = <c>SELFSERVE_NOT_APPLICABLE</c>
/// · [BIZ] แจ้งได้ทั้งตอนใช้งานปกติและตอนอ่านอย่างเดียว (TrialReadOnlyFilter ข้าม /api/v1/selfserve) · ไม่ตรวจ IsReadOnly ที่นี่โดยตั้งใจ
/// · ลำดับของการแจ้ง (กติกาที่เทสต์ล็อกไว้): workspace → Manager → บัญชีรับเงิน → requestId → requestId เดิม = แถวเดิม
///   (ไม่อัปโหลด/ไม่ส่ง LINE ซ้ำ) → ต่ออายุได้ไหม → ค่าในฟอร์ม → ไฟล์ → รอตรวจซ้อน → อัปโหลด → สลิปซ้ำ → INSERT → LINE
///   · อัปโหลดล้ม = ไม่มีแถวถูกบันทึก · INSERT ชน (requestId เดิม/รอตรวจซ้อนจากอีกแท็บ) = ลบไฟล์ที่เพิ่งอัปโหลด (best effort)
/// </summary>
public sealed class PackagePaymentService(
    ICurrentUser user,
    ISelfServeLifecycleRepository workspaces,
    IWorkspacePolicy policy,
    IPackagePaymentRepository payments,
    IPackageSlipStorage slips,
    IPackagePaymentNotifier notifier,
    SelfServeOptions options,
    TimeProvider clock) : IPackagePaymentService
{
    public const int DefaultTake = 20;
    public const int MaxTake = 50;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool IsManager => user.Role == UserRole.Manager;
    private SelfServeOptions.PackageOptions Package => options.Package;

    public async Task<Result<PackageOverviewDto>> GetOverviewAsync(CancellationToken ct = default)
    {
        var workspace = await ResolveWorkspaceAsync(ct);
        if (workspace is null) return Result<PackageOverviewDto>.Fail(SelfServeErrors.NotApplicable());

        var now = Now;
        var plan = await policy.GetAsync(user.ShardKey, workspace.BranchId!.Value, ct);
        var endsAt = plan.ExpiresAt;
        var pending = await payments.FindPendingAsync(workspace.Id, ct);

        var monthOptions = Package.EffectiveMonthOptions.Select(months =>
        {
            var preview = PackageRenewal.Compute(endsAt, now, months);
            return new PackageMonthOptionDto(months, Package.PriceFor(months) is { } price ? PackagePaymentRules.Money(price) : null,
                preview.Ends, preview.LastUsableDate);
        }).ToList();

        var account = Package.PaymentAccount;
        var (code, reason) = CannotSubmit(workspace, endsAt, pending is not null);

        return Result<PackageOverviewDto>.Ok(new PackageOverviewDto(
            SelfServeText.WorkspaceCode(workspace.WorkspaceNo),
            workspace.Plan.ToString().ToLowerInvariant(),
            workspace.Status.ToString().ToLowerInvariant(),
            endsAt,
            endsAt is { } end ? PackageRenewal.LastUsableDay(end) : null,
            PlanAccess.DaysLeft(endsAt, now),
            plan.IsReadOnly,
            monthOptions,
            account.IsConfigured
                ? new PackagePaymentAccountDto(Blank(account.BankName), Blank(account.AccountNo), Blank(account.AccountName),
                    Blank(account.PromptPayId))
                : null,
            pending is null ? null : PackagePaymentMapper.ToDto(pending, IsManager),
            code is null,
            code,
            reason,
            Package.PaidAtMaxPastDays));
    }

    public async Task<Result<IReadOnlyList<PackagePaymentDto>>> ListPaymentsAsync(int? take, CancellationToken ct = default)
    {
        var workspace = await ResolveWorkspaceAsync(ct);
        if (workspace is null) return Result<IReadOnlyList<PackagePaymentDto>>.Fail(SelfServeErrors.NotApplicable());

        var rows = await payments.ListForWorkspaceAsync(workspace.Id, Math.Clamp(take ?? DefaultTake, 1, MaxTake), ct);
        return Result<IReadOnlyList<PackagePaymentDto>>.Ok(rows.Select(p => PackagePaymentMapper.ToDto(p, IsManager)).ToList());
    }

    public async Task<Result<PackagePaymentDto>> SubmitAsync(PackagePaymentSubmission submission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(submission);

        var workspace = await ResolveWorkspaceAsync(ct);
        if (workspace is null) return Fail(SelfServeErrors.NotApplicable());
        if (!IsManager) return Fail(PackageErrors.Forbidden());
        if (!Package.PaymentAccount.IsConfigured) return Fail(PackageErrors.Unavailable());

        var requestId = PackagePaymentRules.ParseRequestId(submission.RequestId);
        if (!requestId.Ok) return Fail(requestId.Error!);

        // ส่งซ้ำ (กดสองครั้ง · เน็ตหลุดแล้วลองใหม่ด้วย requestId เดิม) = แถวเดิม ไม่อัปโหลดและไม่ส่ง LINE อีก
        if (await payments.FindByRequestAsync(workspace.Id, requestId.Value, ct) is { } existing)
            return Ok(existing);

        var now = Now;
        var plan = await policy.GetAsync(user.ShardKey, workspace.BranchId!.Value, ct);
        if (NotRenewableReason(workspace, plan.ExpiresAt) is { } notRenewable)
            return Fail(PackageErrors.NotRenewable(notRenewable));

        var paidAt = PackagePaymentRules.ParsePaidAt(submission.PaidAt, now, Package.PaidAtMaxPastDays);
        if (!paidAt.Ok) return Fail(paidAt.Error!);
        var amount = PackagePaymentRules.ParseAmount(submission.Amount);
        if (!amount.Ok) return Fail(amount.Error!);
        var months = PackagePaymentRules.ParseMonthCount(submission.MonthCount, Package.EffectiveMonthOptions);
        if (!months.Ok) return Fail(months.Error!);
        var note = PackagePaymentRules.ParseNote(submission.Note);
        if (!note.Ok) return Fail(note.Error!);

        if (submission.Slip is not { } slip)
            return Fail(PackageErrors.Validation("กรุณาแนบสลิปการโอนเงิน 1 ไฟล์", "slip"));
        var check = slips.Validate(slip.ContentType, slip.SizeBytes);
        if (!check.IsValid) return Fail(SlipError(check.Code));

        // ตรวจก่อนอัปโหลดให้ผู้ใช้รู้ทันทีโดยไม่เสียเวลาส่งไฟล์ · ด่านจริงคือ filtered unique index ตอน INSERT
        if (await payments.FindPendingAsync(workspace.Id, ct) is not null) return Fail(PackageErrors.PendingExists());

        var paymentId = Guid.NewGuid();
        StoredSlip stored;
        try
        {
            stored = await slips.SaveAsync(slip.Content, user.ShardKey, workspace.BranchId!.Value, paymentId, ct);
        }
        catch (InvalidDataException)
        {
            return Fail(PackageErrors.AttachmentTypeInvalid());
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return Fail(PackageErrors.SlipUploadFailed());
        }

        var payment = new PackagePayment
        {
            Id = paymentId,
            WorkspaceId = workspace.Id,
            RequestId = requestId.Value,
            Status = PackagePaymentStatus.Pending,
            MonthCount = months.Value,
            Amount = amount.Value,
            ExpectedAmount = Package.PriceFor(months.Value),
            PaidAt = paidAt.Value,
            SlipPath = stored.RelativePath,
            SlipSha256 = stored.OriginalSha256,
            SlipContentType = stored.ContentType,
            SlipSizeBytes = stored.SizeBytes,
            Note = note.Value,
            SubmittedByUserId = user.UserId,
            SubmittedByUserName = Limit(user.UserName, 100),
            SubmittedAt = now
        };

        var duplicates = await payments.FindBySlipHashesAsync([stored.OriginalSha256], ct);

        var insert = await payments.TryInsertAsync(payment, new PackagePaymentAuditInfo(
            user.UserName, payment.Note, JsonSerializer.Serialize(new
            {
                paymentId = payment.Id,
                months = payment.MonthCount,
                amount = payment.Amount,
                expectedAmount = payment.ExpectedAmount,
                paidAt = payment.PaidAt,
                duplicateSlips = duplicates.Count
            })), ct);

        if (insert.Outcome != PackagePaymentInsertOutcome.Inserted)
        {
            await TryDeleteSlipAsync(stored.RelativePath);
            return insert.Outcome == PackagePaymentInsertOutcome.DuplicateRequest && insert.Payment is { } same
                ? Ok(same)
                : Fail(PackageErrors.PendingExists());
        }

        // บันทึกแล้ว: แจ้งทีมงานต่อแม้ผู้ใช้ปิดหน้า/เน็ตหลุดระหว่างรอ (ไม่ส่ง ct ของคำขอ · LINE มี timeout ของตัวเอง)
        var context = Context(workspace, payment);
        await notifier.SubmittedAsync(new PackagePaymentSubmittedNotice(context, plan.ExpiresAt,
            PackageRenewal.Compute(plan.ExpiresAt, now, payment.MonthCount),
            duplicates.Where(d => d.Payment.Id != payment.Id).Select(PackagePaymentAdminService.Duplicate).ToList()),
            CancellationToken.None);

        return Ok(payment);
    }

    public async Task<Result<PackagePaymentDto>> CancelAsync(string? paymentId, CancellationToken ct = default)
    {
        var workspace = await ResolveWorkspaceAsync(ct);
        if (workspace is null) return Fail(SelfServeErrors.NotApplicable());
        if (!IsManager) return Fail(PackageErrors.Forbidden());
        if (!Guid.TryParse(paymentId, out var id)) return Fail(PackageErrors.NotFound());

        var current = await payments.GetAsync(id, ct);
        if (current is null || current.WorkspaceId != workspace.Id) return Fail(PackageErrors.NotFound());
        if (current.Status != PackagePaymentStatus.Pending) return Fail(PackageErrors.NotPending());

        var now = Now;
        var cancelled = await payments.TryCancelAsync(id, workspace.Id, now,
            new PackagePaymentAuditInfo(user.UserName, null, JsonSerializer.Serialize(new { paymentId = id })), ct);
        if (!cancelled) return Fail(PackageErrors.NotPending());

        var updated = await payments.GetAsync(id, ct) ?? current;
        await notifier.CancelledAsync(Context(workspace, updated), CancellationToken.None);
        return Ok(updated);
    }

    public async Task<Result<PackageSlipFile>> OpenSlipAsync(string? paymentId, CancellationToken ct = default)
    {
        var workspace = await ResolveWorkspaceAsync(ct);
        if (workspace is null) return Result<PackageSlipFile>.Fail(SelfServeErrors.NotApplicable());
        if (!IsManager) return Result<PackageSlipFile>.Fail(PackageErrors.Forbidden());
        if (!Guid.TryParse(paymentId, out var id)) return Result<PackageSlipFile>.Fail(PackageErrors.NotFound());

        var payment = await payments.GetAsync(id, ct);
        if (payment is null || payment.WorkspaceId != workspace.Id)
            return Result<PackageSlipFile>.Fail(PackageErrors.NotFound());

        var file = await OpenFileAsync(payment, ct);
        return file is null
            ? Result<PackageSlipFile>.Fail(PackageErrors.NotFound())
            : Result<PackageSlipFile>.Ok(file);
    }

    public async Task<PackageSlipFile?> OpenSignedSlipAsync(string? paymentId, string? exp, string? sig,
        CancellationToken ct = default)
    {
        // [SECURITY] ตรวจลายเซ็นก่อนแตะฐาน/FTP · คำขอปลอมเสียแค่ HMAC หนึ่งครั้ง
        if (!Guid.TryParse(paymentId, out var id)
            || !long.TryParse(exp, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                out var expires)
            || !PackageSlipLinks.Verify(Package.EffectiveSlipLinkKey, id, expires, sig, Now))
            return null;

        var entry = await payments.GetEntryAsync(id, ct);
        if (entry is null || entry.Workspace.Status == WorkspaceStatus.Purged) return null;
        return await OpenFileAsync(entry.Payment, ct);
    }

    // ---------- ตัวช่วย ----------

    /// <summary>
    /// ลำดับเหตุผลที่แสดง (เหตุผลแรกที่เข้าเงื่อนไข): ต่ออายุไม่ได้ → ยังไม่ตั้งบัญชีรับเงิน → ไม่ใช่ผู้จัดการ → มีรายการรอตรวจ
    /// · ต่ออายุไม่ได้มาก่อนเพราะเป็นเรื่องของอู่ (ใครก็แก้ไม่ได้) · รอตรวจมาท้ายสุด: ผู้จัดการเห็นสถานะรายการในการ์ดอยู่แล้ว
    /// </summary>
    private (string? Code, string? ReasonTh) CannotSubmit(SelfServeWorkspaceInfo workspace, DateTime? endsAt, bool hasPending)
    {
        if (NotRenewableReason(workspace, endsAt) is { } notRenewable) return ("not_renewable", notRenewable);
        if (!Package.PaymentAccount.IsConfigured) return ("unavailable", PackageErrors.Unavailable().MessageTh);
        if (!IsManager) return ("not_manager", "เฉพาะผู้จัดการอู่เท่านั้นที่แจ้งชำระเงินได้");
        if (hasPending) return ("pending_exists", PackageErrors.PendingExists().MessageTh);
        return (null, null);
    }

    /// <summary>[BIZ] กฎข้อ 10: converted · suspended · paid ที่ไม่มีวันหมด ต่ออายุไม่ได้</summary>
    internal static string? NotRenewableReason(SelfServeWorkspaceInfo workspace, DateTime? endsAt) => workspace.Plan switch
    {
        WorkspacePlanKind.Converted => "อู่นี้ย้ายไปใช้งานเป็นสาขา GaragePro แล้ว ต่ออายุที่สาขาใหม่แทน",
        WorkspacePlanKind.Suspended => $"อู่นี้ถูกระงับการใช้งาน กรุณาติดต่อ {SelfServeText.ContactLine}",
        WorkspacePlanKind.Paid when endsAt is null => "แพ็กเกจของอู่นี้ไม่มีวันหมดอายุ ไม่ต้องต่ออายุ",
        _ => null
    };

    private async Task<PackageSlipFile?> OpenFileAsync(PackagePayment payment, CancellationToken ct)
    {
        var stream = await slips.OpenReadAsync(payment.SlipPath, ct);
        if (stream is null) return null;
        var extension = payment.SlipContentType == "application/pdf" ? "pdf" : "jpg";
        return new PackageSlipFile(stream, payment.SlipContentType,
            $"slip-{PackagePaymentRules.ShortId(payment.Id)}.{extension}");
    }

    private async Task TryDeleteSlipAsync(string relativePath)
    {
        try { await slips.DeleteAsync(relativePath, CancellationToken.None); }
        catch (Exception) { /* best effort: ไฟล์กำพร้าไม่มีแถวอ้าง ไม่มีใครเปิดดูได้ · purge ไม่เห็น (ยอมรับได้) */ }
    }

    private ApiError SlipError(string? code) => code switch
    {
        PackageErrors.AttachmentEmptyCode => PackageErrors.AttachmentEmpty(),
        PackageErrors.AttachmentTooLargeCode => PackageErrors.AttachmentTooLarge(slips.MaxSizeBytes),
        _ => PackageErrors.AttachmentTypeInvalid()
    };

    private PackagePaymentNoticeContext Context(SelfServeWorkspaceInfo workspace, PackagePayment payment) =>
        new(payment, workspace.Id, workspace.WorkspaceNo, workspace.Name, workspace.Phone, workspace.BranchId!.Value,
            workspace.OwnerEmail);

    private async Task<SelfServeWorkspaceInfo?> ResolveWorkspaceAsync(CancellationToken ct)
    {
        if (!SelfServeText.IsSelfServeShard(user.ShardKey, options)) return null;
        var workspace = await workspaces.FindByBranchAsync(user.BranchId, ct);
        return workspace is { BranchId: not null, Status: not WorkspaceStatus.Purged } ? workspace : null;
    }

    private Result<PackagePaymentDto> Ok(PackagePayment payment) =>
        Result<PackagePaymentDto>.Ok(PackagePaymentMapper.ToDto(payment, IsManager));

    private static Result<PackagePaymentDto> Fail(ApiError error) => Result<PackagePaymentDto>.Fail(error);

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Limit(string value, int max) => value.Length > max ? value[..max] : value;
}
