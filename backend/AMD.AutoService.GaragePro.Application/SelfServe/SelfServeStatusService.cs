using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>endpoint <c>/api/v1/selfserve/*</c> ของอู่ที่ login อยู่ (shard/สาขาจาก JWT เสมอ — ไม่รับจาก request)</summary>
public interface ISelfServeStatusService
{
    Task<Result<SelfServeStatusDto>> GetStatusAsync(CancellationToken ct = default);
    Task<Result<ExtensionRequestedDto>> RequestExtensionAsync(ExtensionRequestInput input, CancellationToken ct = default);
    Task<Result<SampleDataLoadDto>> LoadSampleDataAsync(CancellationToken ct = default);
    Task<Result<SampleDataClearedDto>> ClearSampleDataAsync(CancellationToken ct = default);
}

/// <summary>
/// สถานะทดลองใช้ · ขอต่ออายุ · โหลด/ล้างข้อมูลตัวอย่าง ของอู่ที่สมัครเอง
/// · [BIZ] shard ≠ sp1 หรือไม่มีแถว sp_Workspace → <c>SELFSERVE_NOT_APPLICABLE</c> (404) — ลูกค้าเดิมไม่มีอะไรให้ดู
/// · ทุก endpoint เรียกได้แม้หมดอายุแล้ว (TrialReadOnlyFilter ข้าม prefix นี้) — การขอต่ออายุต้องทำได้ตอนหมดอายุพอดี
///   ยกเว้นโหลดข้อมูลตัวอย่างซึ่งเป็นการเขียนข้อมูลใหม่ทั้งชุด จึงตรวจอ่านอย่างเดียวเองที่นี่
/// · <see cref="IStarterDataService"/> เป็นของ S3 — resolve แบบ optional (ยังไม่ลงทะเบียน = 503 ข้อความไทย ไม่ใช่ 500)
/// </summary>
public sealed class SelfServeStatusService(
    ICurrentUser user,
    ISelfServeLifecycleRepository repository,
    IWorkspacePolicy policy,
    IPlanUsageCounter usage,
    ISelfServeSalesNotifier sales,
    SelfServeOptions options,
    TimeProvider clock,
    IStarterDataService? starterData = null) : ISelfServeStatusService
{
    /// <summary>[BIZ] ขอต่ออายุได้วันละครั้ง (24 ชม.) — ตรงกับที่เว็บซ่อนปุ่มไว้ (trialUx.isExtensionRecentlyRequested)</summary>
    public static readonly TimeSpan ExtensionCooldown = TimeSpan.FromHours(24);

    public const int MaxNoteLength = 500;
    public const int MaxRequestIdLength = 100;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<SelfServeStatusDto>> GetStatusAsync(CancellationToken ct = default)
    {
        var workspace = await ResolveWorkspaceAsync(ct);
        if (workspace is null) return Result<SelfServeStatusDto>.Fail(SelfServeErrors.NotApplicable());

        var branchId = workspace.BranchId!.Value;
        var plan = await policy.GetAsync(user.ShardKey, branchId, ct);
        var now = Now;

        // นับต่อชนิดทีละตัว (แต่ละตัวเปิด connection ของตัวเอง — ไม่ขนานเพื่อไม่ให้ endpoint เดียวถือ 5 connection พร้อมกัน)
        var staff = await usage.CountAsync(options.ShardKey, branchId, PlanLimitKind.Staff, ct);
        var catalog = await usage.CountAsync(options.ShardKey, branchId, PlanLimitKind.CatalogItems, ct);
        var customers = await usage.CountAsync(options.ShardKey, branchId, PlanLimitKind.Customers, ct);
        var jobs = await usage.CountAsync(options.ShardKey, branchId, PlanLimitKind.Jobs, ct);
        var quotationsSent = await repository.CountQuotationsEverSentAsync(options.ShardKey, branchId, ct);

        var endsAt = plan.ExpiresAt ?? workspace.TrialEndsAt;
        var limits = new SelfServeLimitsDto(
            new SelfServeLimitUsageDto(staff, plan.Limits.For(PlanLimitKind.Staff)),
            new SelfServeLimitUsageDto(catalog, plan.Limits.For(PlanLimitKind.CatalogItems)),
            new SelfServeLimitUsageDto(customers, plan.Limits.For(PlanLimitKind.Customers)),
            new SelfServeLimitUsageDto(jobs, plan.Limits.For(PlanLimitKind.Jobs)));

        return Result<SelfServeStatusDto>.Ok(new SelfServeStatusDto(
            SelfServeText.WorkspaceCode(workspace.WorkspaceNo),
            workspace.Name,
            workspace.Plan.ToString().ToLowerInvariant(),
            workspace.Status.ToString().ToLowerInvariant(),
            endsAt,
            endsAt is { } end ? SelfServeText.DaysLeft(end, now) : null,
            plan.IsReadOnly,
            limits,
            workspace.SampleDataLoadedAt,
            workspace.ExtensionRequestedAt,
            new SelfServeChecklistDto(staff, catalog, jobs, quotationsSent, workspace.MobileLoginSeenAt is not null)));
    }

    public async Task<Result<ExtensionRequestedDto>> RequestExtensionAsync(ExtensionRequestInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var workspace = await ResolveWorkspaceAsync(ct);
        if (workspace is null) return Result<ExtensionRequestedDto>.Fail(SelfServeErrors.NotApplicable());

        var requestId = input.RequestId?.Trim();
        if (string.IsNullOrEmpty(requestId) || requestId.Length > MaxRequestIdLength)
            return Result<ExtensionRequestedDto>.Fail(SelfServeLifecycleErrors.Validation(
                "คำขอไม่ถูกต้อง — กรุณาปิดหน้าต่างแล้วกดขอต่ออายุอีกครั้ง", "requestId"));
        if (input.Note is { Length: > MaxNoteLength })
            return Result<ExtensionRequestedDto>.Fail(SelfServeLifecycleErrors.Validation(
                $"หมายเหตุยาวเกิน {MaxNoteLength} ตัวอักษร", "note"));

        var now = Now;
        var notBefore = now - ExtensionCooldown;
        if (workspace.ExtensionRequestedAt is { } last && last >= notBefore)
            return Result<ExtensionRequestedDto>.Fail(SelfServeErrors.ExtensionAlreadyRequested());

        var note = string.IsNullOrWhiteSpace(input.Note) ? null : SelfServeText.SingleLine(input.Note, MaxNoteLength);

        // claim ก่อนส่ง LINE (UPDATE แบบมีเงื่อนไข) — กดสองแท็บพร้อมกันต้องได้ข้อความเดียว
        if (!await repository.TryRecordExtensionRequestAsync(workspace.Id, now, notBefore, user.UserName, note, ct))
            return Result<ExtensionRequestedDto>.Fail(SelfServeErrors.ExtensionAlreadyRequested());

        // ไม่ได้ตั้ง LINE (dev) = บันทึกคำขอไว้ใน sp_Workspace/sp_WorkspaceAudit อย่างเดียว — ทีมงานเห็นใน spadmin/สรุปรายวัน
        if (sales.IsConfigured)
        {
            var policyPlan = await policy.GetAsync(user.ShardKey, workspace.BranchId!.Value, ct);
            var text = ExtensionMessage(workspace, policyPlan.ExpiresAt ?? workspace.TrialEndsAt, note);
            var retryKey = Guid.TryParse(requestId, out var parsed)
                ? parsed
                : SelfServeText.DeterministicGuid($"extension:{workspace.Id}:{requestId}");

            bool sent;
            try { sent = await sales.SendAsync(retryKey, text, ct); }
            catch (Exception) when (!ct.IsCancellationRequested) { sent = false; }

            if (!sent)
            {
                await repository.RevertExtensionRequestAsync(workspace.Id, now, workspace.ExtensionRequestedAt,
                    CancellationToken.None);
                return Result<ExtensionRequestedDto>.Fail(SelfServeLifecycleErrors.NotifyFailed());
            }
        }

        return Result<ExtensionRequestedDto>.Ok(new ExtensionRequestedDto(now));
    }

    public async Task<Result<SampleDataLoadDto>> LoadSampleDataAsync(CancellationToken ct = default)
    {
        var workspace = await ResolveWorkspaceAsync(ct);
        if (workspace is null) return Result<SampleDataLoadDto>.Fail(SelfServeErrors.NotApplicable());
        if (user.Role != UserRole.Manager) return Result<SampleDataLoadDto>.Fail(SelfServeErrors.SampleForbidden());
        if (starterData is null) return Result<SampleDataLoadDto>.Fail(SelfServeLifecycleErrors.SampleUnavailable());

        // [BIZ] หมดอายุแล้วสร้างจ๊อบ/ลูกค้าตัวอย่างใหม่ไม่ได้ (เป็นการเขียนทั้งชุด — filter กลางข้าม prefix นี้)
        var plan = await policy.GetAsync(user.ShardKey, workspace.BranchId!.Value, ct);
        if (plan.IsReadOnly) return Result<SampleDataLoadDto>.Fail(PlanErrors.TrialExpired(options.Trial.Days));

        if (workspace.SampleDataLoadedAt is not null)
            return Result<SampleDataLoadDto>.Ok(new SampleDataLoadDto(true, true, [], null));

        var result = await starterData.LoadAsync(workspace.Id, ct);
        return result.Loaded
            ? Result<SampleDataLoadDto>.Ok(new SampleDataLoadDto(true, false, result.Technicians, result.Error))
            : Result<SampleDataLoadDto>.Fail(SelfServeLifecycleErrors.SampleFailed());
    }

    public async Task<Result<SampleDataClearedDto>> ClearSampleDataAsync(CancellationToken ct = default)
    {
        var workspace = await ResolveWorkspaceAsync(ct);
        if (workspace is null) return Result<SampleDataClearedDto>.Fail(SelfServeErrors.NotApplicable());
        if (user.Role != UserRole.Manager) return Result<SampleDataClearedDto>.Fail(SelfServeErrors.SampleForbidden());
        if (starterData is null) return Result<SampleDataClearedDto>.Fail(SelfServeLifecycleErrors.SampleUnavailable());

        // ล้างได้แม้หมดอายุ — ลดข้อมูล ไม่ได้สร้างงานใหม่ (และ purge จะลบอยู่แล้วเมื่อครบกำหนด)
        var result = await starterData.ClearAsync(workspace.Id, ct);
        return result.Success
            ? Result<SampleDataClearedDto>.Ok(new SampleDataClearedDto(true))
            : Result<SampleDataClearedDto>.Fail(result.Error!);
    }

    /// <summary>"🔁 ขอต่ออายุ/สนใจแพ็กเกจ: อู่ X (SP1042) · โทร … · หมดอายุ dd/MM/yyyy · หมายเหตุ: …" (บรรทัดเดียว)</summary>
    public static string ExtensionMessage(SelfServeWorkspaceInfo workspace, DateTime? expiresAt, string? note) =>
        $"🔁 ขอต่ออายุ/สนใจแพ็กเกจ: อู่ {SelfServeText.SingleLine(workspace.Name, 100)} " +
        $"({SelfServeText.WorkspaceCode(workspace.WorkspaceNo)}) · โทร {SelfServeText.DisplayPhone(workspace.Phone)} · " +
        $"หมดอายุ {(expiresAt is { } e ? SelfServeText.FormatThaiDate(e) : "-")} · " +
        $"หมายเหตุ: {(string.IsNullOrWhiteSpace(note) ? "-" : SelfServeText.SingleLine(note, MaxNoteLength))}";

    private async Task<SelfServeWorkspaceInfo?> ResolveWorkspaceAsync(CancellationToken ct)
    {
        if (!SelfServeText.IsSelfServeShard(user.ShardKey, options)) return null;

        var workspace = await repository.FindByBranchAsync(user.BranchId, ct);
        return workspace is { BranchId: not null, Status: not WorkspaceStatus.Purged } ? workspace : null;
    }
}
