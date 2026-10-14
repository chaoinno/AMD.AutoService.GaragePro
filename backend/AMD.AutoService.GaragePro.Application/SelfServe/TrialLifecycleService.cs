using System.Globalization;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>ชื่องานรายวัน (sp_JobRun.JobName) — spadmin <c>run-jobs --task</c> ใช้ชื่อเดียวกัน</summary>
public static class TrialLifecycleTasks
{
    public const string Maintenance = "maintenance";
    public const string Summary = "summary";

    /// <summary>รีเฟรช blocklist (ภายใน maintenance · claim แยกเพื่อจำวันที่รีเฟรชล่าสุดตาม Blocklist:RefreshDays)</summary>
    public const string Blocklist = "blocklist";

    public static bool IsKnown(string? task) => task is Maintenance or Summary;
}

/// <summary>ผลของงานหนึ่งรอบ — <see cref="Ran"/> = false เมื่อ instance อื่น/รอบก่อน claim งานของวันนั้นไปแล้ว</summary>
public sealed record LifecycleRunReport(
    string Task,
    DateOnly RunDate,
    bool Ran,
    string Outcome,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<string> Errors);

/// <summary>
/// วงจรชีวิตอู่ทดลองใช้รายวัน (docs/12 §6) — <see cref="RunDueAsync"/> ให้ hosted job เรียกทุก 15 นาที ·
/// <see cref="RunAsync"/> ให้ spadmin สั่งรันเอง (กำหนด "เวลาปัจจุบัน" จำลองได้ และเลือกได้ว่าจะ claim หรือไม่)
/// </summary>
public interface ITrialLifecycleService
{
    Task<IReadOnlyList<LifecycleRunReport>> RunDueAsync(CancellationToken ct = default);

    Task<LifecycleRunReport> RunAsync(string task, DateTime? asOfUtc = null, bool claim = false,
        CancellationToken ct = default);
}

public enum LifecycleActionKind
{
    /// <summary>sp_Workspace Active → Expired (+ PurgeAfter) และแจ้ง LINE ทีมขาย — เกิดครั้งเดียวต่อการหมดอายุ</summary>
    MarkExpired,

    /// <summary>Expired → Active เมื่อทีมงานต่ออายุแล้ว (วันหมดอายุใหม่ยังไม่ถึง)</summary>
    MarkActive,

    /// <summary>ส่งอีเมล — dedupe ด้วย sp_WorkspaceNotice(WorkspaceId, <see cref="LifecycleAction.NoticeKind"/>)</summary>
    Notify,

    /// <summary>ลบข้อมูลอู่ (IWorkspacePurger)</summary>
    Purge
}

public sealed record LifecycleAction(LifecycleActionKind Kind, string? NoticeKind = null, int DaysLeft = 0);

/// <summary>
/// กติกาล้วนของงานรายวัน (ไม่มี I/O ไม่มีนาฬิกา) — แยกออกมาให้เทสต์วันที่ 0/23/29/30/75/120 ได้ตรงๆ
/// · [BIZ] เฉพาะแพ็กเกจ trial ที่ได้อีเมลเตือน/หมดอายุ/ชวนกลับ · suspended มีแค่การลบเมื่อครบกำหนด
///   · paid/converted ไม่ถูกแตะเลย (ลูกค้าที่จ่ายเงินห้ามถูกลบอัตโนมัติ — converted ให้ทีมงานลบด้วย spadmin)
/// · วันหมดอายุ = Branch.EndPackageDate (ตัวจริงที่ PlanPolicy ใช้) → ไม่มีจึงใช้สำเนาใน sp_Workspace
/// · วันลบ = วันหมดอายุ + Trial:ReadOnlyDays แต่ไม่ก่อน sp_Workspace.PurgeAfter (ทีมงานเลื่อนการลบได้ ลดลงไม่ได้)
/// </summary>
public static class TrialLifecycleRules
{
    public const string ExpiredNotice = "expired";

    public static string ReminderNotice(int days) => string.Create(CultureInfo.InvariantCulture, $"reminder-{days}");
    public static string ComebackNotice(int day) => string.Create(CultureInfo.InvariantCulture, $"comeback-{day}");

    public static DateTime? ExpiresAt(LifecycleWorkspace w) =>
        w.BranchEndsAt
        ?? (w.Workspace.Plan is WorkspacePlanKind.Trial or WorkspacePlanKind.Suspended ? w.Workspace.TrialEndsAt : null);

    public static DateTime? PurgeAt(LifecycleWorkspace w, SelfServeOptions.TrialOptions trial)
    {
        if (ExpiresAt(w) is not { } expiresAt) return null;
        var byReadOnlyWindow = expiresAt.AddDays(trial.ReadOnlyDays);
        return w.Workspace.PurgeAfter is { } after && after > byReadOnlyWindow ? after : byReadOnlyWindow;
    }

    public static IReadOnlyList<LifecycleAction> Decide(
        LifecycleWorkspace w, DateTime nowUtc, SelfServeOptions.TrialOptions trial)
    {
        ArgumentNullException.ThrowIfNull(w);
        ArgumentNullException.ThrowIfNull(trial);

        var ws = w.Workspace;
        if (ws.BranchId is null || ws.Status is WorkspaceStatus.Purged or WorkspaceStatus.Provisioning) return [];
        if (ws.Plan is WorkspacePlanKind.Paid or WorkspacePlanKind.Converted) return [];
        if (ExpiresAt(w) is not { } expiresAt) return [];

        // ครบกำหนดลบแล้ว — ไม่ต้องส่งอะไรอีก (ลบทั้งอู่รวมแถว notice ไม่ได้ เพราะ notice อยู่คนละฐาน — ไม่เป็นไร)
        if (nowUtc >= PurgeAt(w, trial)!.Value) return [new LifecycleAction(LifecycleActionKind.Purge)];
        if (ws.Plan == WorkspacePlanKind.Suspended) return [];

        var actions = new List<LifecycleAction>();
        if (nowUtc >= expiresAt)
        {
            if (ws.Status == WorkspaceStatus.Active) actions.Add(new LifecycleAction(LifecycleActionKind.MarkExpired));

            var comebackAt = expiresAt.AddDays(trial.ComebackDay);
            var comebackKind = ComebackNotice(trial.ComebackDay);
            if (trial.ComebackDay > 0 && nowUtc >= comebackAt)
            {
                // งานหยุดไปหลายสัปดาห์จนเลยวันชวนกลับ: ส่งแค่ฉบับชวนกลับ ไม่ส่งฉบับ "หมดอายุ" ย้อนหลังซ้อนกันสองฉบับ
                if (!w.NoticesSent.Contains(comebackKind))
                    actions.Add(new LifecycleAction(LifecycleActionKind.Notify, comebackKind));
            }
            else if (!w.NoticesSent.Contains(ExpiredNotice))
            {
                actions.Add(new LifecycleAction(LifecycleActionKind.Notify, ExpiredNotice));
            }

            return actions;
        }

        if (ws.Status == WorkspaceStatus.Expired) actions.Add(new LifecycleAction(LifecycleActionKind.MarkActive));

        // เตือนเฉพาะรอบที่ใกล้ที่สุด — งานพลาดไปวันหนึ่งแล้วกลับมาวันที่เหลือ 1 วัน ต้องได้ฉบับ "พรุ่งนี้" ฉบับเดียว
        var daysLeft = SelfServeText.CalendarDaysLeft(expiresAt, nowUtc);
        var due = trial.EffectiveReminderDays.Where(d => d > 0 && daysLeft <= d).DefaultIfEmpty(0).Min();
        if (due > 0 && !w.NoticesSent.Contains(ReminderNotice(due)))
            actions.Add(new LifecycleAction(LifecycleActionKind.Notify, ReminderNotice(due), Math.Max(daysLeft, 0)));

        return actions;
    }
}

/// <summary>
/// ตัวรันงานรายวัน — ไม่มี logging ในชั้น Application จึงคืนรายงาน (จำนวน + ข้อผิดพลาด) ให้ผู้เรียก (hosted job/spadmin) log เอง
/// · [BIZ] ทุกอู่ทำแยกกันใน try/catch — อู่หนึ่งพังต้องไม่ทำให้อู่อื่นไม่ได้อีเมล/ไม่ถูกลบ
/// · อีเมล: claim notice ก่อนส่ง (ส่งได้ไม่เกินครั้งเดียว) ส่งไม่สำเร็จถอน claim ให้รอบหน้าลองใหม่
/// · เวลาทั้งหมดมาจาก <see cref="TimeProvider"/> (หรือ asOf ของ spadmin) — ไม่มี DateTime.Now ที่ไหน
/// </summary>
public sealed class TrialLifecycleService(
    ISelfServeLifecycleRepository repository,
    IWorkspacePurger purger,
    IEmailSender email,
    ISelfServeSalesNotifier sales,
    IBlocklistSource blocklist,
    SelfServeOptions options,
    TimeProvider clock,
    IWorkspaceProvisioningMaintenance? provisioning = null) : ITrialLifecycleService
{
    /// <summary>[ASSUME] เปิดอู่ค้างเกินเท่านี้ถือว่าถูกทิ้ง (S2 ข้ามอู่ที่ยังมีคำขอถือล็อกอยู่เอง)</summary>
    public static readonly TimeSpan StuckProvisioningAge = TimeSpan.FromHours(1);

    public static readonly TimeOnly DefaultDailyAt = new(3, 30);
    public static readonly TimeOnly DefaultSummaryAt = new(8, 30);
    public const string Actor = "system:lifecycle";

    public async Task<IReadOnlyList<LifecycleRunReport>> RunDueAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var timeOfDay = TimeOnly.FromDateTime(SelfServeText.ToThai(now));
        var reports = new List<LifecycleRunReport>();

        // เลยเวลาที่ตั้งไว้ของวันนี้แล้ว = ถึงกำหนด (API ที่เพิ่งสตาร์ทตอนสายยังรันงานของวันนี้ได้ · claim กันรันซ้ำ)
        foreach (var (task, at) in new[]
                 {
                     (TrialLifecycleTasks.Maintenance, ParseTime(options.Jobs.DailyAtLocalTime, DefaultDailyAt)),
                     (TrialLifecycleTasks.Summary, ParseTime(options.Jobs.SummaryAtLocalTime, DefaultSummaryAt))
                 })
        {
            if (timeOfDay < at) continue;
            reports.Add(await RunAsync(task, now, claim: true, ct));
        }

        return reports;
    }

    public async Task<LifecycleRunReport> RunAsync(string task, DateTime? asOfUtc = null, bool claim = false,
        CancellationToken ct = default)
    {
        if (!TrialLifecycleTasks.IsKnown(task))
            throw new ArgumentException($"ไม่รู้จักงาน '{task}' (มี {TrialLifecycleTasks.Maintenance}, {TrialLifecycleTasks.Summary})",
                nameof(task));

        var now = asOfUtc is { } asOf ? DateTime.SpecifyKind(asOf, DateTimeKind.Utc) : clock.GetUtcNow().UtcDateTime;
        var runDate = SelfServeText.ThaiDate(now);

        if (claim && !await repository.TryClaimJobRunAsync(task, runDate, clock.GetUtcNow().UtcDateTime,
                Environment.MachineName, ct))
            return new LifecycleRunReport(task, runDate, false, "skipped: already claimed",
                new Dictionary<string, int>(), []);

        try
        {
            var report = task == TrialLifecycleTasks.Maintenance
                ? await RunMaintenanceAsync(now, runDate, ct)
                : await RunSummaryAsync(now, runDate, ct);

            if (claim)
                await repository.CompleteJobRunAsync(task, runDate, clock.GetUtcNow().UtcDateTime, report.Outcome,
                    report.Errors.Count == 0 ? null : string.Join(" | ", report.Errors), CancellationToken.None);
            return report;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (claim)
                await repository.CompleteJobRunAsync(task, runDate, clock.GetUtcNow().UtcDateTime, "failed",
                    $"{ex.GetType().Name}: {ex.Message}", CancellationToken.None);
            return new LifecycleRunReport(task, runDate, true, "failed", new Dictionary<string, int>(),
                [$"{ex.GetType().Name}: {ex.Message}"]);
        }
    }

    private async Task<LifecycleRunReport> RunMaintenanceAsync(DateTime now, DateOnly runDate, CancellationToken ct)
    {
        var counts = new Dictionary<string, int>
        {
            ["expired"] = 0, ["reactivated"] = 0, ["emails"] = 0, ["emailsSkipped"] = 0, ["purged"] = 0,
            ["blockedDomainsAdded"] = 0, ["provisioningAbandoned"] = 0, ["abandonedServicePurged"] = 0
        };
        var errors = new List<string>();

        await CleanupAbandonedProvisioningAsync(counts, errors, ct);

        foreach (var w in await repository.ListForLifecycleAsync(ct))
        {
            var code = SelfServeText.WorkspaceCode(w.Workspace.WorkspaceNo);
            foreach (var action in TrialLifecycleRules.Decide(w, now, options.Trial))
            {
                try
                {
                    await ApplyAsync(w, action, now, counts, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"{code} {action.Kind}{(action.NoticeKind is null ? "" : ":" + action.NoticeKind)}: " +
                               $"{ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        try
        {
            counts["blockedDomainsAdded"] = await RefreshBlocklistAsync(now, runDate, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"blocklist: {ex.GetType().Name}: {ex.Message}");
        }

        var outcome = string.Create(CultureInfo.InvariantCulture,
            $"{(errors.Count == 0 ? "ok" : "partial")}: expired {counts["expired"]} · emails {counts["emails"]} · " +
            $"purged {counts["purged"]} · errors {errors.Count}");
        return new LifecycleRunReport(TrialLifecycleTasks.Maintenance, runDate, true, outcome, counts, errors);
    }

    /// <summary>
    /// อู่ที่เปิดค้าง (S2 รื้อแถว shadow + ตั้ง Purged) แล้วตามลบ svc_* ที่ขั้นตั้งค่าสร้างไว้ (หมวดหมู่/คลัง/กะ/override)
    /// · ทั้งสองขั้นไม่บังคับ: S2 ไม่ได้ลงทะเบียน (host อื่น) = ข้าม · ล้มแล้วรอบหน้าลองใหม่ (ทั้งคู่ idempotent)
    /// </summary>
    private async Task CleanupAbandonedProvisioningAsync(Dictionary<string, int> counts, List<string> errors,
        CancellationToken ct)
    {
        if (provisioning is not null)
        {
            try
            {
                var result = await provisioning.CleanupStuckAsync(StuckProvisioningAge, ct);
                counts["provisioningAbandoned"] = result.Abandoned;
                errors.AddRange(result.Errors.Select(e => $"provisioning: {e}"));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"provisioning: {ex.GetType().Name}: {ex.Message}");
            }
        }

        try
        {
            foreach (var workspaceId in await repository.ListAbandonedNeedingServicePurgeAsync(ct))
            {
                var result = await purger.PurgeAbandonedServiceRowsAsync(workspaceId, Actor, ct);
                if (result.Success) counts["abandonedServicePurged"]++;
                else errors.Add($"abandoned {workspaceId}: {result.Error!.Code}: {result.Error.MessageTh}");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            errors.Add($"abandoned: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private async Task ApplyAsync(LifecycleWorkspace w, LifecycleAction action, DateTime now,
        Dictionary<string, int> counts, CancellationToken ct)
    {
        var ws = w.Workspace;
        switch (action.Kind)
        {
            case LifecycleActionKind.MarkExpired:
            {
                var expiresAt = TrialLifecycleRules.ExpiresAt(w)!.Value;
                await repository.SetStatusAsync(ws.Id, WorkspaceStatus.Expired,
                    TrialLifecycleRules.PurgeAt(w, options.Trial), now, Actor, "expired", ct);
                counts["expired"]++;

                // สถานะบันทึกไปแล้ว — LINE ล้มรายงานเป็นข้อผิดพลาดของรอบนี้ (ทีมขายยังเห็นใน "หมดอายุวันนี้" ของสรุปรายวัน)
                if (sales.IsConfigured
                    && !await sales.SendAsync(SelfServeText.DeterministicGuid($"expired:{ws.Id}:{expiresAt:O}"),
                        ExpiredSalesMessage(ws, expiresAt), ct))
                    throw new InvalidOperationException("ส่ง LINE แจ้งทีมขายว่าหมดทดลองใช้ไม่สำเร็จ");
                break;
            }
            case LifecycleActionKind.MarkActive:
                await repository.SetStatusAsync(ws.Id, WorkspaceStatus.Active, null, now, Actor, "reactivated", ct);
                counts["reactivated"]++;
                break;
            case LifecycleActionKind.Notify:
                await NotifyAsync(w, action, now, counts, ct);
                break;
            case LifecycleActionKind.Purge:
            {
                var result = await purger.PurgeAsync(new WorkspacePurgeRequest(ws.Id, Actor), ct);
                if (!result.Success)
                    throw new InvalidOperationException($"{result.Error!.Code}: {result.Error.MessageTh}");
                if (!result.Data!.AlreadyPurged) counts["purged"]++;
                break;
            }
        }
    }

    private async Task NotifyAsync(LifecycleWorkspace w, LifecycleAction action, DateTime now,
        Dictionary<string, int> counts, CancellationToken ct)
    {
        var ws = w.Workspace;
        var kind = action.NoticeKind!;
        if (!await repository.TryAddNoticeAsync(ws.Id, kind, now, ct)) return;   // instance อื่นส่งไปแล้ว

        var message = BuildEmail(w, action, kind);
        if (message is null || string.IsNullOrWhiteSpace(ws.OwnerEmail))
        {
            counts["emailsSkipped"]++;   // ไม่มีอีเมลเจ้าของ (ข้อมูลผิด) — บันทึก notice ไว้ ไม่ลองซ้ำทุกวัน
            return;
        }

        try
        {
            await email.SendAsync(message.To(ws.OwnerEmail), ct);
            counts["emails"]++;
        }
        catch
        {
            await repository.RemoveNoticeAsync(ws.Id, kind, CancellationToken.None);
            throw;
        }
    }

    private EmailContent? BuildEmail(LifecycleWorkspace w, LifecycleAction action, string kind)
    {
        var ws = w.Workspace;
        var expiresAt = TrialLifecycleRules.ExpiresAt(w)!.Value;
        var purgeAt = TrialLifecycleRules.PurgeAt(w, options.Trial)!.Value;

        if (kind == TrialLifecycleRules.ExpiredNotice)
            return SelfServeEmails.Expired(ws.Name, ws.WorkspaceNo, expiresAt, options.Trial.ReadOnlyDays, purgeAt,
                options.PublicBaseUrl);
        if (kind == TrialLifecycleRules.ComebackNotice(options.Trial.ComebackDay))
            return SelfServeEmails.Comeback(ws.Name, ws.WorkspaceNo, expiresAt, purgeAt, options.PublicBaseUrl);
        // หัวเรื่องบอกวันที่เหลือจริง แต่ tag = รอบเตือนตาม config (reminder-7/reminder-1) ให้จัดกลุ่มที่ผู้ให้บริการอีเมลได้คงที่
        if (kind.StartsWith("reminder-", StringComparison.Ordinal))
            return SelfServeEmails.Reminder(action.DaysLeft, ws.Name, ws.WorkspaceNo, expiresAt,
                options.Trial.ReadOnlyDays, options.PublicBaseUrl) with { Tag = kind };
        return null;
    }

    private async Task<int> RefreshBlocklistAsync(DateTime now, DateOnly runDate, CancellationToken ct)
    {
        var url = options.Blocklist.Url?.Trim();
        if (string.IsNullOrEmpty(url)) return 0;   // ไม่ตั้ง URL = ใช้เฉพาะรายการที่ฝังมากับ build + ที่ทีมเพิ่มเอง

        var last = await repository.LastSuccessfulRunAsync(TrialLifecycleTasks.Blocklist, ct);
        if (last is { } l && now - l < TimeSpan.FromDays(Math.Max(1, options.Blocklist.RefreshDays))) return 0;
        if (!await repository.TryClaimJobRunAsync(TrialLifecycleTasks.Blocklist, runDate,
                clock.GetUtcNow().UtcDateTime, Environment.MachineName, ct)) return 0;

        try
        {
            var domains = await blocklist.FetchAsync(url, ct);
            var added = await repository.UpsertBlockedDomainsAsync(domains, "remote", now, ct);
            await repository.CompleteJobRunAsync(TrialLifecycleTasks.Blocklist, runDate, clock.GetUtcNow().UtcDateTime,
                string.Create(CultureInfo.InvariantCulture, $"ok: {domains.Count} domains · added {added}"), null,
                CancellationToken.None);
            return added;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await repository.CompleteJobRunAsync(TrialLifecycleTasks.Blocklist, runDate, clock.GetUtcNow().UtcDateTime,
                "failed", $"{ex.GetType().Name}: {ex.Message}", CancellationToken.None);
            throw;
        }
    }

    private async Task<LifecycleRunReport> RunSummaryAsync(DateTime now, DateOnly runDate, CancellationToken ct)
    {
        var all = (await repository.ListForLifecycleAsync(ct))
            .Where(w => w.Workspace.Status != WorkspaceStatus.Provisioning)
            .ToList();
        var since = now.AddHours(-24);

        var signups = all.Count(w => w.Workspace.CreatedAt > since && w.Workspace.CreatedAt <= now);
        var trials = all.Where(w => w.Workspace.Plan == WorkspacePlanKind.Trial)
            .Select(w => TrialLifecycleRules.ExpiresAt(w))
            .Where(e => e is not null)
            .Select(e => e!.Value)
            .ToList();
        var expiringToday = trials.Count(e => SelfServeText.ThaiDate(e) == runDate);
        var withinWeek = trials.Count(e => e > now && e <= now.AddDays(7));
        var extensions = all.Count(w => w.Workspace.ExtensionRequestedAt is { } at && at > since && at <= now);

        var counts = new Dictionary<string, int>
        {
            ["signups"] = signups, ["expiringToday"] = expiringToday, ["withinWeek"] = withinWeek,
            ["extensionRequests"] = extensions
        };
        var text = SummaryMessage(runDate, signups, expiringToday, withinWeek, extensions);

        if (!sales.IsConfigured)
            return new LifecycleRunReport(TrialLifecycleTasks.Summary, runDate, true, "skipped: LINE not configured",
                counts, []);

        var sent = await sales.SendAsync(
            SelfServeText.DeterministicGuid($"summary:{runDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}"),
            text, ct);
        return new LifecycleRunReport(TrialLifecycleTasks.Summary, runDate, true, sent ? "ok" : "failed", counts,
            sent ? [] : ["LINE push failed"]);
    }

    /// <summary>"สรุปทดลองใช้ dd/MM/yyyy: สมัครใหม่ N · หมดอายุวันนี้ M · เหลือ ≤7 วัน K · ขอต่ออายุ J"</summary>
    public static string SummaryMessage(DateOnly date, int signups, int expiringToday, int withinWeek, int extensions) =>
        string.Create(CultureInfo.InvariantCulture,
            $"สรุปทดลองใช้ {SelfServeText.FormatThaiDate(date)}: สมัครใหม่ {signups} · หมดอายุวันนี้ {expiringToday} · " +
            $"เหลือ ≤7 วัน {withinWeek} · ขอต่ออายุ {extensions}");

    /// <summary>"⏰ หมดทดลองใช้: อู่ X (SP1042) · โทร … · หมดอายุ dd/MM/yyyy"</summary>
    public static string ExpiredSalesMessage(SelfServeWorkspaceInfo ws, DateTime expiresAt) =>
        $"⏰ หมดทดลองใช้: อู่ {SelfServeText.SingleLine(ws.Name, 100)} ({SelfServeText.WorkspaceCode(ws.WorkspaceNo)}) · " +
        $"โทร {SelfServeText.DisplayPhone(ws.Phone)} · หมดอายุ {SelfServeText.FormatThaiDate(expiresAt)}";

    /// <summary>"HH:mm" เวลาไทย — ค่าผิดรูปใช้ค่าเริ่มต้น (งานต้องไม่หยุดเพราะพิมพ์ config ผิด)</summary>
    public static TimeOnly ParseTime(string? value, TimeOnly fallback) =>
        TimeOnly.TryParseExact(value?.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var parsed)
            ? parsed
            : fallback;
}
