using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Conversion;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.SpAdmin.Cli;

/// <summary>รหัสออกของโปรแกรม — ใช้ใน script ได้</summary>
public static class ExitCodes
{
    public const int Ok = 0;
    public const int Failed = 1;          // ถูกปฏิเสธ / ล้ม
    public const int Usage = 2;           // คำสั่ง/ค่าไม่ถูกต้อง หรือขาด --yes
    public const int NotConfigured = 3;   // ไม่มีค่าตั้งฐาน self-serve
    public const int CompletedWithFailures = 4;   // convert/run-jobs ทำจบแต่มีบางรายการล้ม
    public const int Cancelled = 130;
}

/// <summary>
/// รันคำสั่งที่ parse แล้ว — แต่ละคำสั่งได้ DI scope ของตัวเอง · ตรรกะทั้งหมดอยู่ใน SelfServeAdminService/ConversionService
/// ที่นี่แค่แปลงค่า → เรียก → พิมพ์ผล (ไม่มีกติกาธุรกิจซ่อนอยู่ใน CLI)
/// </summary>
public sealed class CommandRunner(IServiceProvider services, TextWriter output, TextWriter error)
{
    private static readonly JsonSerializerOptions ReportJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // ภาษาไทยอ่านได้ในไฟล์ (ไม่ใช่ ก…)
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<int> RunAsync(ParsedCommand command, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        return command.Name switch
        {
            "list" => await ListAsync(sp, command, ct),
            "show" => await ShowAsync(sp, command, ct),
            "extend" => await ExtendAsync(sp, command, ct),
            "set-plan" => await SetPlanAsync(sp, command, ct),
            "suspend" => await SuspendAsync(sp, command, ct),
            "purge" => await PurgeAsync(sp, command, ct),
            "run-jobs" => await RunJobsAsync(sp, command, ct),
            "convert" => await ConvertAsync(sp, command, ct),
            _ => Usage($"ไม่รู้จักคำสั่ง {command.Name}")
        };
    }

    /// <summary>ผู้กระทำที่ลง audit: --by → $USER → ชื่อบัญชีของเครื่อง — นำหน้าด้วย "spadmin:" ให้แยกจากการกดในแอป</summary>
    public static string Actor(ParsedCommand command)
    {
        var name = command.Get("by");
        if (string.IsNullOrWhiteSpace(name)) name = Environment.GetEnvironmentVariable("USER");
        if (string.IsNullOrWhiteSpace(name)) name = Environment.UserName;
        return $"spadmin:{name.Trim()}";
    }

    // ───────────────────────────────────────────── list / show

    private async Task<int> ListAsync(IServiceProvider sp, ParsedCommand command, CancellationToken ct)
    {
        var admin = sp.GetRequiredService<SelfServeAdminService>();
        WorkspaceStatus? status = command.Get("status") is { } s ? Enum.Parse<WorkspaceStatus>(s, ignoreCase: true) : null;
        WorkspacePlanKind? plan = command.Get("plan") is { } p ? Enum.Parse<WorkspacePlanKind>(p, ignoreCase: true) : null;

        var rows = await admin.ListAsync(status, plan, ct);
        var now = DateTime.UtcNow;
        var table = new TextTable("รหัสอู่", "ชื่ออู่", "โทร", "แพ็กเกจ/สถานะ", "หมดอายุ", "เหลือ(วัน)", "สมัครเมื่อ", "อีเมลเจ้าของ");
        foreach (var r in rows)
            table.Add(SelfServeText.WorkspaceCode(r.WorkspaceNo), SelfServeText.SingleLine(r.Name, 40),
                SelfServeText.DisplayPhone(r.Phone), $"{r.Plan}/{r.Status}",
                r.EndsAt is { } e ? SelfServeText.FormatThaiDateTime(e) : "-",
                r.EndsAt is { } e2 && r.Status != WorkspaceStatus.Purged
                    ? SelfServeText.DaysLeft(e2, now).ToString(CultureInfo.InvariantCulture) : "-",
                SelfServeText.FormatThaiDate(r.CreatedAt), r.OwnerEmail ?? "-");

        table.Write(output);
        output.WriteLine($"ทั้งหมด {rows.Count} อู่");
        return ExitCodes.Ok;
    }

    private async Task<int> ShowAsync(IServiceProvider sp, ParsedCommand command, CancellationToken ct)
    {
        if (!TryWorkspace(command, out var no, out var exit)) return exit;
        var result = await sp.GetRequiredService<SelfServeAdminService>().ShowAsync(no, ct);
        if (!result.Success) return Refused(result.Error!);

        var d = result.Data!;
        var ws = d.Snapshot.Workspace;
        output.WriteLine($"{SelfServeText.WorkspaceCode(ws.WorkspaceNo)} · {ws.Name}");
        Line("Workspace Id", ws.Id.ToString());
        Line("แพ็กเกจ / สถานะ", $"{ws.Plan} / {ws.Status}");
        Line("สาขา (sp1)", ws.BranchId?.ToString(CultureInfo.InvariantCulture) ?? "-");
        Line("โทร", $"{SelfServeText.DisplayPhone(ws.Phone)}{(ws.DuplicatePhoneFlag ? "  ⚠ เบอร์เคยสมัครอู่อื่น" : "")}");
        Line("สมัครเมื่อ", SelfServeText.FormatThaiDateTime(ws.CreatedAt));
        Line("หมดอายุ (Branch)", d.EndsAt is { } e
            ? $"{SelfServeText.FormatThaiDateTime(e)} (เหลือ {d.DaysLeft} วัน)"
            : "ไม่มีกำหนด");
        if (d.Snapshot.Branch is { } b)
            Line("Branch package", $"Status {b.Status} · Start {Fmt(b.StartsAtUtc)} · Days {b.PackageDays?.ToString(CultureInfo.InvariantCulture) ?? "-"} · End {Fmt(b.EndsAtUtc)}");
        Line("สำเนา TrialEndsAt", Fmt(ws.TrialEndsAt));
        Line("ลบได้หลัง (PurgeAfter)", Fmt(ws.PurgeAfter));
        if (ws.ConvertedAt is not null)
            Line("ย้ายไปสาขาจริง", $"{ws.ConvertedToShardKey}/{ws.ConvertedToBranchId} เมื่อ {Fmt(ws.ConvertedAt)}");
        Line("ข้อมูลตัวอย่าง", ws.SampleDataLoadedAt is { } l ? $"โหลดแล้ว {Fmt(l)}" : ws.SampleDataError ?? "ยังไม่โหลด");
        Line("ขอต่ออายุล่าสุด", Fmt(ws.ExtensionRequestedAt));
        Line("เข้าแอปมือถือครั้งแรก", Fmt(ws.MobileLoginSeenAt));
        Line("ที่มา", string.Join(" · ", new[] { ws.Source, ws.UtmSource, ws.UtmMedium, ws.UtmCampaign }
            .Where(x => !string.IsNullOrWhiteSpace(x)).DefaultIfEmpty("-")));

        if (d.Snapshot.Owner is { } o)
        {
            output.WriteLine();
            output.WriteLine("บัญชีเจ้าของ (sp_Account)");
            Line("อีเมล", $"{o.Email ?? "-"} (normalized {o.NormalizedEmail})");
            Line("ช่องทาง / สถานะ", $"{o.Provider} / {o.Status}");
            Line("ชื่อผู้ใช้", $"{o.LoginUserName ?? "-"} (User {o.UserId?.ToString(CultureInfo.InvariantCulture) ?? "-"})");
            Line("ยืนยันอีเมล", Fmt(o.EmailVerifiedAt));
        }

        if (d.Usage.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("การใช้ลิมิต");
            foreach (var u in d.Usage)
                Line(u.Kind, u.Max is { } max ? $"{u.Used} / {max}" : $"{u.Used} (ไม่จำกัด)");
        }

        foreach (var warning in d.Warnings) output.WriteLine($"⚠ {warning}");

        if (d.Snapshot.Audit.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"audit ล่าสุด {d.Snapshot.Audit.Count} รายการ");
            var table = new TextTable("เวลา", "การกระทำ", "ผู้กระทำ", "หมายเหตุ");
            foreach (var a in d.Snapshot.Audit)
                table.Add(SelfServeText.FormatThaiDateTime(a.CreatedAt), a.Action, a.Actor,
                    SelfServeText.SingleLine(a.Note, 80));
            table.Write(output);
        }

        return ExitCodes.Ok;
    }

    // ───────────────────────────────────────────── เปลี่ยนแพ็กเกจ

    private async Task<int> ExtendAsync(IServiceProvider sp, ParsedCommand command, CancellationToken ct)
    {
        if (!TryWorkspace(command, out var no, out var exit)) return exit;
        if (!ValueParsers.TryParseInt(command.Get("days"), out var days)) return Usage("--days ต้องเป็นจำนวนเต็ม เช่น 14 หรือ -31");

        var result = await sp.GetRequiredService<SelfServeAdminService>()
            .ExtendAsync(no, days, command.Get("note"), Actor(command), ct);
        return Changed(result);
    }

    private async Task<int> SetPlanAsync(IServiceProvider sp, ParsedCommand command, CancellationToken ct)
    {
        if (!TryWorkspace(command, out var no, out var exit)) return exit;
        DateOnly? until = null;
        if (command.Get("until") is { } raw)
        {
            if (!ValueParsers.TryParseDate(raw, out var parsed)) return Usage("--until ต้องเป็นวันที่ yyyy-MM-dd (ค.ศ.)");
            until = parsed;
        }

        var plan = Enum.Parse<WorkspacePlanKind>(command.Get("plan")!, ignoreCase: true);
        var result = await sp.GetRequiredService<SelfServeAdminService>()
            .SetPlanAsync(no, plan, until, command.Get("note"), Actor(command), ct);
        return Changed(result);
    }

    private async Task<int> SuspendAsync(IServiceProvider sp, ParsedCommand command, CancellationToken ct)
    {
        if (!TryWorkspace(command, out var no, out var exit)) return exit;
        var result = await sp.GetRequiredService<SelfServeAdminService>()
            .SuspendAsync(no, command.Get("reason")!, Actor(command), ct);
        return Changed(result);
    }

    private int Changed(Result<AdminChangeResult> result)
    {
        if (!result.Success) return Refused(result.Error!);
        var r = result.Data!;
        output.WriteLine($"{SelfServeText.WorkspaceCode(r.WorkspaceNo)}: บันทึกแล้ว");
        Line("แพ็กเกจ", r.PlanFrom == r.PlanTo ? r.PlanTo.ToString() : $"{r.PlanFrom} → {r.PlanTo}");
        Line("สถานะ", r.StatusFrom == r.StatusTo ? r.StatusTo.ToString() : $"{r.StatusFrom} → {r.StatusTo}");
        Line("หมดอายุ", $"{FmtEnd(r.EndsAtFrom)} → {FmtEnd(r.EndsAtTo)}");
        output.WriteLine("มีผลกับ API ภายใน ~60 วินาที (cache ของนโยบายแพ็กเกจ)");
        return ExitCodes.Ok;
    }

    // ───────────────────────────────────────────── purge

    private async Task<int> PurgeAsync(IServiceProvider sp, ParsedCommand command, CancellationToken ct)
    {
        if (!TryWorkspace(command, out var no, out var exit)) return exit;
        var admin = sp.GetRequiredService<SelfServeAdminService>();

        if (!command.Has("yes"))
        {
            var preview = await admin.ShowAsync(no, ct);
            if (!preview.Success) return Refused(preview.Error!);
            var ws = preview.Data!.Snapshot.Workspace;
            error.WriteLine($"จะลบข้อมูลทั้งหมดของ {SelfServeText.WorkspaceCode(ws.WorkspaceNo)} · {ws.Name} ({ws.Plan}/{ws.Status}) " +
                            "— ย้อนกลับไม่ได้");
            return Usage("ยืนยันด้วย --yes" + (ws.Plan is WorkspacePlanKind.Paid or WorkspacePlanKind.Converted
                ? " --allow-paid (อู่นี้จ่ายเงิน/ย้ายไปสาขาจริงแล้ว)" : ""));
        }

        var result = await admin.PurgeAsync(no, command.Has("allow-paid"), Actor(command), ct);
        if (!result.Success) return Refused(result.Error!);

        var report = result.Data!;
        if (report.AlreadyPurged)
        {
            output.WriteLine($"{SelfServeText.WorkspaceCode(report.WorkspaceNo)} ถูกลบไปแล้วก่อนหน้านี้ — ไม่มีอะไรต้องทำ");
            return ExitCodes.Ok;
        }

        output.WriteLine($"{SelfServeText.WorkspaceCode(report.WorkspaceNo)}: ลบข้อมูลแล้ว (สาขา {report.BranchId})");
        var table = new TextTable("ตาราง", "แถว");
        foreach (var (name, rows) in report.DeletedRows.OrderBy(k => k.Key, StringComparer.Ordinal))
            table.Add(name, rows.ToString(CultureInfo.InvariantCulture));
        table.Write(output);
        output.WriteLine($"ไฟล์แนบ: ลบ {report.FilesDeleted} · ไม่พบ {report.FilesMissing} · ลบไม่สำเร็จ {report.FilesFailed.Count}");
        foreach (var path in report.FilesFailed) output.WriteLine($"  ลบเองที่ FTP: {path}");
        return ExitCodes.Ok;
    }

    // ───────────────────────────────────────────── run-jobs

    private async Task<int> RunJobsAsync(IServiceProvider sp, ParsedCommand command, CancellationToken ct)
    {
        var task = command.Get("task") ?? TrialLifecycleTasks.Maintenance;
        DateTime? asOf = null;
        if (command.Get("as-of") is { } raw)
        {
            var options = sp.GetRequiredService<IOptions<SelfServeOptions>>().Value;
            var dailyAt = TrialLifecycleService.ParseTime(
                task == TrialLifecycleTasks.Summary ? options.Jobs.SummaryAtLocalTime : options.Jobs.DailyAtLocalTime,
                task == TrialLifecycleTasks.Summary ? TrialLifecycleService.DefaultSummaryAt : TrialLifecycleService.DefaultDailyAt);
            if (!ValueParsers.TryParseAsOf(raw, DateTime.UtcNow, dailyAt, out var parsed))
                return Usage("--as-of ต้องเป็น ISO (2026-12-31T03:30:00+07:00) · วันที่ (2026-12-31) · หรือ +91d / -1d");
            asOf = parsed;
            output.WriteLine($"จำลองเวลา: {SelfServeText.FormatThaiDateTime(parsed)} (UTC {parsed:yyyy-MM-dd HH:mm})");
        }

        var result = await sp.GetRequiredService<SelfServeAdminService>().RunJobsAsync(task, asOf, ct);
        if (!result.Success) return Refused(result.Error!);

        var r = result.Data!;
        output.WriteLine($"{r.Task} ({SelfServeText.FormatThaiDate(r.RunDate)}): {r.Outcome}");
        foreach (var (name, count) in r.Counts) Line(name, count.ToString(CultureInfo.InvariantCulture));
        foreach (var e in r.Errors) error.WriteLine($"✗ {e}");
        return r.Errors.Count == 0 ? ExitCodes.Ok : ExitCodes.CompletedWithFailures;
    }

    // ───────────────────────────────────────────── convert

    private async Task<int> ConvertAsync(IServiceProvider sp, ParsedCommand command, CancellationToken ct)
    {
        if (!TryWorkspace(command, out var no, out var exit)) return exit;
        if (!ValueParsers.TryParseInt(command.Get("target-branch"), out var branch) || branch <= 0)
            return Usage("--target-branch ต้องเป็น Branch.Id (จำนวนเต็มบวก)");
        if (!ValueParsers.TryParseLong(command.Get("target-user"), out var user))
            return Usage("--target-user ต้องเป็น [User].Id (จำนวนเต็มบวก)");

        var dryRun = command.Has("dry-run");
        if (!dryRun && !command.Has("yes"))
            return Usage("การย้ายจริงต้องยืนยันด้วย --yes — รัน --dry-run ก่อนเสมอ และย้ายเฉพาะเมื่อสร้างสาขาปลายทางใน " +
                         "GaragePro Admin แล้วและเจ้าของอู่อนุมัติแล้ว");

        var request = new ConversionRequest(no, command.Get("target-shard") ?? "db2", branch, user, dryRun, Actor(command),
            command.Has("allow-failures"));
        var result = await sp.GetRequiredService<IConversionService>().ConvertAsync(request, ct);
        if (!result.Success) return Refused(result.Error!);

        var report = result.Data!;
        output.WriteLine($"{report.WorkspaceCode} → {report.TargetShardKey}/{report.TargetBranchId} (User {report.TargetUserId})" +
                         (report.DryRun ? " · DRY-RUN ไม่ได้เขียนอะไร" : ""));
        var table = new TextTable("ชนิด", "ต้นทาง", "ตัวอย่าง", "สร้าง", "จับคู่ของเดิม", "ย้ายแล้ว", "ข้าม", "ล้ม");
        foreach (var e in report.Entities)
            table.Add(e.EntityType, N(e.Source), N(e.SampleExcluded), N(e.Created), N(e.MappedExisting),
                N(e.AlreadyConverted), N(e.Skipped), N(e.Failed));
        table.Write(output);

        foreach (var e in report.Entities.Where(e => e.Messages.Count > 0))
        {
            output.WriteLine();
            output.WriteLine($"{e.EntityType}:");
            foreach (var m in e.Messages) output.WriteLine($"  · {m}");
        }

        if (report.StaffAccounts.Count > 0)
        {
            output.WriteLine();
            output.WriteLine("บัญชีพนักงานใหม่ที่สาขาปลายทาง (รหัสผ่านเริ่มต้น = ชื่อผู้ใช้ ตามกติกาสาขาเดิม — ให้เปลี่ยนทันที):");
            foreach (var a in report.StaffAccounts) output.WriteLine($"  · {a.FullName}: {a.UserName} (Staff {a.TargetStaffId})");
        }

        if (report.ImagesNotCopied.Count > 0)
        {
            output.WriteLine();
            output.WriteLine($"รูปที่ไม่ได้คัดลอก ({report.ImagesNotCopied.Count}):");
            foreach (var image in report.ImagesNotCopied) output.WriteLine($"  · {image}");
        }

        output.WriteLine();
        foreach (var note in report.Notes) output.WriteLine($"หมายเหตุ: {note}");
        output.WriteLine(report.DryRun
            ? "ผล: dry-run"
            : report.Finished ? "ผล: ปิดการย้ายแล้ว (Plan=converted · ลบข้อมูลใน sp1 อัตโนมัติหลัง 30 วัน)" : "ผล: ยังไม่ปิดการย้าย");

        if (command.Get("out") is { } path)
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, ReportJson), ct);
            output.WriteLine($"เขียนรายงานแล้ว: {Path.GetFullPath(path)} (มีชื่อผู้ใช้พนักงาน — เก็บเป็นความลับ)");
        }

        return report.TotalFailed == 0 ? ExitCodes.Ok : ExitCodes.CompletedWithFailures;
    }

    // ───────────────────────────────────────────── ตัวช่วย

    private bool TryWorkspace(ParsedCommand command, out int workspaceNo, out int exitCode)
    {
        exitCode = ExitCodes.Ok;
        if (SelfServeAdminService.TryParseWorkspaceNo(command.Get("workspace"), out workspaceNo)) return true;
        exitCode = Usage("--workspace ต้องเป็นรหัสอู่ เช่น SP1042 หรือ 1042");
        return false;
    }

    private int Usage(string message)
    {
        error.WriteLine(message);
        return ExitCodes.Usage;
    }

    private int Refused(ApiError apiError)
    {
        error.WriteLine($"✗ {apiError.Code}: {apiError.MessageTh}");
        return ExitCodes.Failed;
    }

    private void Line(string label, string value) => output.WriteLine($"  {label,-24} {value}");

    private static string Fmt(DateTime? utc) => utc is { } v ? SelfServeText.FormatThaiDateTime(v) : "-";
    private static string FmtEnd(DateTime? utc) => utc is { } v ? SelfServeText.FormatThaiDateTime(v) : "ไม่มีกำหนด";
    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>ตารางข้อความแบบง่าย (ช่องว่างเติมตามความยาวตัวอักษร — อักษรไทยที่มีสระบน/ล่างอาจเยื้องเล็กน้อย)</summary>
public sealed class TextTable(params string[] headers)
{
    private readonly List<string[]> _rows = [];

    public void Add(params string[] cells) => _rows.Add(cells);

    public void Write(TextWriter writer)
    {
        var widths = headers.Select((h, i) => Math.Max(Width(h),
            _rows.Count == 0 ? 0 : _rows.Max(r => i < r.Length ? Width(r[i]) : 0))).ToArray();
        writer.WriteLine(Format(headers, widths));
        writer.WriteLine(string.Join("  ", widths.Select(w => new string('-', w))));
        foreach (var row in _rows) writer.WriteLine(Format(row, widths));
    }

    private static string Format(IReadOnlyList<string> cells, IReadOnlyList<int> widths) =>
        string.Join("  ", widths.Select((w, i) =>
        {
            var cell = i < cells.Count ? cells[i] ?? "" : "";
            return cell + new string(' ', Math.Max(0, w - Width(cell)));
        })).TrimEnd();

    /// <summary>ความกว้างที่เห็นบนจอ — ไม่นับสระบน/ล่างและวรรณยุกต์ไทย (combining mark) ให้คอลัมน์ตรงขึ้น</summary>
    private static int Width(string? text) =>
        (text ?? "").Count(c => CharUnicodeInfo.GetUnicodeCategory(c) is not (UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark));
}
