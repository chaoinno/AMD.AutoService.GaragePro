using System.Globalization;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Admin;

/// <summary>
/// คำสั่งของทีมงานต่ออู่ทดลองใช้ (tools/spadmin) — ดู/ต่ออายุ/เปลี่ยนแพ็กเกจ/ระงับ/ลบ/รันงานรายวัน
/// · [BIZ] วันหมดอายุตัวจริง = Branch.EndPackageDate (PlanPolicy) — ทุกการเลื่อนวันแก้ Branch และสำเนา sp_Workspace.TrialEndsAt
///   พร้อมกันใน transaction เดียว (Workspace.cs) · ทุกการเปลี่ยนลง sp_WorkspaceAudit พร้อมชื่อผู้กระทำ
/// · ผลมีผลกับ API ภายใน ≤60 วิ (cache ของ WorkspacePolicy/SessionRevocationMiddleware) — ไม่มีทางล้าง cache ข้าม process
/// · ไม่มี Console ที่นี่ — CLI เป็นผู้พิมพ์ผล
/// </summary>
public sealed class SelfServeAdminService(
    ISelfServeAdminRepository repository,
    IPlanUsageCounter usage,
    IWorkspacePurger purger,
    ITrialLifecycleService lifecycle,
    SelfServeOptions options,
    TimeProvider clock)
{
    public const int AuditTail = 15;
    public const int MaxExtendDays = 3650;

    public const string ExtendAction = "extend";
    public const string SetPlanAction = "set-plan";
    public const string SuspendAction = "suspend";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>รับ "SP1042" / "sp1042" / "1042" → 1042 (รหัสอู่ตามที่แสดงในอีเมล/LINE หรือเลขล้วน)</summary>
    public static bool TryParseWorkspaceNo(string? value, out int workspaceNo)
    {
        workspaceNo = 0;
        var text = value?.Trim() ?? string.Empty;
        if (text.StartsWith("SP", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out workspaceNo) && workspaceNo > 0;
    }

    /// <summary>
    /// "--until 2027-12-31" = ใช้งานได้ถึง<b>สิ้นวัน</b>นั้นตามเวลาไทย → EndPackageDate = 2028-01-01 00:00 (เวลาไทย) แบบเดียวกับ
    /// ข้อมูลจริงของ Garage (EndPackageDate เป็นเที่ยงคืนของวันถัดจากวันสุดท้ายที่ใช้ได้)
    /// </summary>
    public static DateTime UntilToEndsAtUtc(DateOnly until) =>
        BranchPackageDates.ToUtc(until.AddDays(1).ToDateTime(TimeOnly.MinValue));

    public Task<IReadOnlyList<AdminWorkspaceRow>> ListAsync(WorkspaceStatus? status, WorkspacePlanKind? plan,
        CancellationToken ct = default) => repository.ListAsync(status, plan, ct);

    public async Task<Result<AdminWorkspaceDetail>> ShowAsync(int workspaceNo, CancellationToken ct = default)
    {
        var snapshot = await repository.GetAsync(workspaceNo, AuditTail, ct);
        if (snapshot is null) return Result<AdminWorkspaceDetail>.Fail(SelfServeLifecycleErrors.WorkspaceNotFound());

        var ws = snapshot.Workspace;
        var endsAt = EndsAt(snapshot);
        var warnings = new List<string>();
        if (ws.BranchId is not null && snapshot.Branch is null)
            warnings.Add($"ไม่พบแถว Branch {ws.BranchId} ใน shard {options.ShardKey}");
        if (snapshot.Branch?.EndsAtUtc is { } branchEnd && ws.TrialEndsAt is { } copy && Math.Abs((branchEnd - copy).TotalMinutes) > 1)
            warnings.Add("วันหมดอายุใน Branch ไม่ตรงกับสำเนา sp_Workspace.TrialEndsAt — Branch เป็นตัวจริง (แก้ด้วย extend/set-plan)");

        var limited = ws.Plan is WorkspacePlanKind.Trial or WorkspacePlanKind.Suspended;
        var limits = options.Limits.ToPlanLimits();
        var usages = new List<AdminLimitUsage>();
        if (ws.BranchId is { } branchId && ws.Status != WorkspaceStatus.Purged)
        {
            foreach (var kind in Enum.GetValues<PlanLimitKind>())
                usages.Add(new AdminLimitUsage(kind.ToString(),
                    await usage.CountAsync(options.ShardKey, branchId, kind, ct),
                    limited ? limits.For(kind) : null));
        }

        return Result<AdminWorkspaceDetail>.Ok(new AdminWorkspaceDetail(snapshot, endsAt,
            endsAt is { } e ? SelfServeText.DaysLeft(e, Now) : null, usages, warnings));
    }

    public async Task<Result<AdminChangeResult>> ExtendAsync(int workspaceNo, int days, string? note, string actor,
        CancellationToken ct = default)
    {
        if (days == 0 || Math.Abs(days) > MaxExtendDays)
            return Fail(SelfServeAdminErrors.Validation($"--days ต้องไม่เป็น 0 และไม่เกิน ±{MaxExtendDays} วัน", "days"));

        var (snapshot, error) = await LoadForChangeAsync(workspaceNo, ct);
        if (error is not null) return Fail(error);
        var ws = snapshot!.Workspace;

        var current = EndsAt(snapshot);
        if (current is null && ws.Plan == WorkspacePlanKind.Paid)
            return Fail(SelfServeAdminErrors.Refused(
                "แพ็กเกจ paid นี้ไม่มีวันหมดอายุ — กำหนดวันด้วย set-plan --plan paid --until yyyy-MM-dd แทน"));

        var now = Now;
        var newEnd = (current ?? now).AddDays(days);
        var statusTo = Reactivated(ws.Status, newEnd, now);
        var payload = JsonSerializer.Serialize(new
        {
            days,
            from = current,
            to = newEnd,
            statusFrom = ws.Status.ToString(),
            statusTo = statusTo.ToString()
        });

        await repository.ApplyPackageChangeAsync(new WorkspacePackageChange(ws.Id, ws.BranchId!.Value, true, newEnd,
            ws.Plan, statusTo, ClearPurgeAfter: statusTo != ws.Status, actor, ExtendAction, note, payload, now), ct);

        return Result<AdminChangeResult>.Ok(new AdminChangeResult(ws.WorkspaceNo, ws.Plan, ws.Plan, ws.Status, statusTo,
            current, newEnd));
    }

    /// <summary>
    /// · paid: ทุกฟีเจอร์ ไม่จำกัด (PlanPolicy) · EndPackageDate = <paramref name="until"/> หรือว่าง = ไม่มีกำหนด · ล้าง PurgeAfter
    ///   (งานรายวันไม่ลบ paid อยู่แล้ว แต่ค่าค้างจะทำให้ show อ่านผิด)
    /// · trial: ใช้วันเดิมถ้าไม่ระบุ <paramref name="until"/> (ไม่มีวันเดิม = ต้องระบุ)
    /// · suspended: ล็อกทันที (PlanPolicy) วันที่ไม่เปลี่ยน · ไม่แตะ sp_Account (login ยังหา shard ได้ → ได้ TRIAL_EXPIRED ที่อ่านรู้เรื่อง)
    /// · Expired + วันหมดอายุใหม่ยังไม่ถึง = กลับเป็น Active ทันที (ไม่ต้องรอ job รายวัน)
    /// </summary>
    public async Task<Result<AdminChangeResult>> SetPlanAsync(int workspaceNo, WorkspacePlanKind plan, DateOnly? until,
        string? note, string actor, CancellationToken ct = default) =>
        await ChangePlanAsync(workspaceNo, plan, until, note, actor, SetPlanAction, ct);

    public async Task<Result<AdminChangeResult>> SuspendAsync(int workspaceNo, string reason, string actor,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Fail(SelfServeAdminErrors.Validation("ต้องระบุเหตุผลการระงับ (--reason)", "reason"));
        return await ChangePlanAsync(workspaceNo, WorkspacePlanKind.Suspended, null, reason.Trim(), actor, SuspendAction, ct);
    }

    private async Task<Result<AdminChangeResult>> ChangePlanAsync(int workspaceNo, WorkspacePlanKind plan,
        DateOnly? until, string? note, string actor, string action, CancellationToken ct)
    {
        if (plan == WorkspacePlanKind.Converted)
            return Fail(SelfServeAdminErrors.Validation("แพ็กเกจ converted ตั้งได้ด้วยคำสั่ง convert เท่านั้น", "plan"));
        if (plan == WorkspacePlanKind.Suspended && until is not null)
            return Fail(SelfServeAdminErrors.Validation("ระงับอู่ไม่ใช้ --until (วันหมดอายุคงเดิม)", "until"));

        var (snapshot, error) = await LoadForChangeAsync(workspaceNo, ct);
        if (error is not null) return Fail(error);
        var ws = snapshot!.Workspace;

        var now = Now;
        var current = EndsAt(snapshot);
        DateTime? untilUtc = until is { } d ? UntilToEndsAtUtc(d) : null;

        bool setEnd;
        DateTime? end;
        var statusTo = ws.Status;
        var clearPurge = false;
        switch (plan)
        {
            case WorkspacePlanKind.Paid:
                setEnd = true;
                end = untilUtc;
                if (ws.Status == WorkspaceStatus.Expired && (end is null || end > now)) statusTo = WorkspaceStatus.Active;
                clearPurge = true;
                break;
            case WorkspacePlanKind.Trial:
                end = untilUtc ?? current;
                if (end is null)
                    return Fail(SelfServeAdminErrors.Validation("อู่นี้ไม่มีวันหมดอายุเดิม — ต้องระบุ --until yyyy-MM-dd", "until"));
                setEnd = untilUtc is not null;
                statusTo = Reactivated(ws.Status, end.Value, now);
                clearPurge = statusTo != ws.Status;
                break;
            default:
                setEnd = false;
                end = current;
                break;
        }

        var payload = JsonSerializer.Serialize(new
        {
            planFrom = ws.Plan.ToString(),
            planTo = plan.ToString(),
            until = until?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            endsAtFrom = current,
            endsAtTo = end,
            statusFrom = ws.Status.ToString(),
            statusTo = statusTo.ToString()
        });

        await repository.ApplyPackageChangeAsync(new WorkspacePackageChange(ws.Id, ws.BranchId!.Value, setEnd, end, plan,
            statusTo, clearPurge, actor, action, note, payload, now), ct);

        return Result<AdminChangeResult>.Ok(new AdminChangeResult(ws.WorkspaceNo, ws.Plan, plan, ws.Status, statusTo,
            current, end));
    }

    /// <summary>
    /// ลบข้อมูลอู่ทันที (ไม่รอกำหนด) — กติกา/guard อยู่ที่ IWorkspacePurger ทั้งหมด (shard · ฐาน · อู่ที่จ่ายเงิน/ย้ายแล้วต้องยืนยัน)
    /// </summary>
    public async Task<Result<WorkspacePurgeReport>> PurgeAsync(int workspaceNo, bool allowPaid, string actor,
        CancellationToken ct = default)
    {
        var snapshot = await repository.GetAsync(workspaceNo, 0, ct);
        if (snapshot is null) return Result<WorkspacePurgeReport>.Fail(SelfServeLifecycleErrors.WorkspaceNotFound());
        return await purger.PurgeAsync(new WorkspacePurgeRequest(snapshot.Workspace.Id, actor, AllowPaid: allowPaid), ct);
    }

    /// <summary>รันงานรายวันเองโดยไม่ claim (ไม่ลง sp_JobRun) — อีเมล/LINE ยัง dedupe ด้วย notice/retry key ตามปกติ</summary>
    public async Task<Result<LifecycleRunReport>> RunJobsAsync(string task, DateTime? asOfUtc, CancellationToken ct = default)
    {
        if (!TrialLifecycleTasks.IsKnown(task))
            return Result<LifecycleRunReport>.Fail(SelfServeAdminErrors.Validation(
                $"ไม่รู้จักงาน '{task}' (มี {TrialLifecycleTasks.Maintenance}, {TrialLifecycleTasks.Summary})", "task"));
        return Result<LifecycleRunReport>.Ok(await lifecycle.RunAsync(task, asOfUtc, claim: false, ct));
    }

    private async Task<(AdminWorkspaceSnapshot? Snapshot, ApiError? Error)> LoadForChangeAsync(int workspaceNo,
        CancellationToken ct)
    {
        var snapshot = await repository.GetAsync(workspaceNo, 0, ct);
        if (snapshot is null) return (null, SelfServeLifecycleErrors.WorkspaceNotFound());

        var ws = snapshot.Workspace;
        var code = SelfServeText.WorkspaceCode(ws.WorkspaceNo);
        if (ws.Status is WorkspaceStatus.Purged or WorkspaceStatus.Provisioning)
            return (null, SelfServeAdminErrors.Refused($"อู่ {code} อยู่สถานะ {ws.Status} — เปลี่ยนแพ็กเกจไม่ได้"));
        if (ws.Plan == WorkspacePlanKind.Converted)
            return (null, SelfServeAdminErrors.Refused($"อู่ {code} ย้ายไปสาขาจริงแล้ว (converted) — แก้แพ็กเกจที่สาขาปลายทางแทน"));
        if (ws.BranchId is null || snapshot.Branch is null)
            return (null, SelfServeAdminErrors.Refused($"อู่ {code} ไม่มีแถว Branch ใน shard {options.ShardKey}"));
        if (repository.CheckWritableDatabase() is { } mismatch)
            return (null, SelfServeAdminErrors.Refused($"ไม่เขียนฐาน: {mismatch}"));

        return (snapshot, null);
    }

    /// <summary>Branch เป็นตัวจริง · สำเนาใน sp_Workspace ใช้เฉพาะ trial/suspended (paid ที่ไม่มีกำหนด = ไม่มีวันหมด) — กติกาเดียวกับ PlanPolicy</summary>
    private static DateTime? EndsAt(AdminWorkspaceSnapshot snapshot) =>
        snapshot.Branch?.EndsAtUtc
        ?? (snapshot.Workspace.Plan is WorkspacePlanKind.Trial or WorkspacePlanKind.Suspended
            ? snapshot.Workspace.TrialEndsAt
            : null);

    private static WorkspaceStatus Reactivated(WorkspaceStatus status, DateTime newEnd, DateTime now) =>
        status == WorkspaceStatus.Expired && newEnd > now ? WorkspaceStatus.Active : status;

    private static Result<AdminChangeResult> Fail(ApiError error) => Result<AdminChangeResult>.Fail(error);
}
