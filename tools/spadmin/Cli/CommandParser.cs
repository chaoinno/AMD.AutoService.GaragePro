using System.Globalization;
using System.Text;

namespace AMD.AutoService.GaragePro.SpAdmin.Cli;

/// <summary>ตัวเลือกหนึ่งตัวของคำสั่ง — <see cref="IsFlag"/> = ไม่มีค่า (เช่น --yes) · <see cref="Allowed"/> = ค่าที่รับ (ไม่สนตัวพิมพ์)</summary>
public sealed record OptionSpec(
    string Name,
    string Description,
    bool IsFlag = false,
    bool Required = false,
    string? ValueHint = null,
    IReadOnlyList<string>? Allowed = null);

public sealed record CommandSpec(
    string Name,
    string Summary,
    IReadOnlyList<OptionSpec> Options,
    IReadOnlyList<string> Examples,
    bool Mutates = false);

/// <summary>คำสั่งที่ parse แล้ว — ค่าของตัวเลือกที่มี <see cref="OptionSpec.Allowed"/> ถูกแปลงเป็นตัวพิมพ์เล็กแล้ว</summary>
public sealed class ParsedCommand(string name, IReadOnlyDictionary<string, string> options, IReadOnlySet<string> flags)
{
    public string Name { get; } = name;
    public IReadOnlyDictionary<string, string> Options { get; } = options;
    public IReadOnlySet<string> Flags { get; } = flags;

    public string? Get(string option) => Options.TryGetValue(option, out var value) ? value : null;
    public bool Has(string flag) => Flags.Contains(flag);
    public bool Verbose => Has("verbose");
}

/// <summary>ผล parse — <see cref="Command"/> null + <see cref="Error"/> null = ขอดูวิธีใช้ (<see cref="HelpFor"/> = ชื่อคำสั่ง หรือ null = ทั้งหมด)</summary>
public sealed record ParseResult(ParsedCommand? Command, string? Error, string? HelpFor = null)
{
    public bool IsHelp => Command is null && Error is null;
}

/// <summary>
/// ตัว parse argument แบบเขียนเอง (ไม่เพิ่ม package — System.CommandLine ไม่อยู่ใน NuGet cache และยังเป็น beta บน .NET 8)
/// · รูปแบบ: <c>spadmin &lt;คำสั่ง&gt; --ชื่อ ค่า | --ชื่อ=ค่า | --flag</c> · ค่าที่ขึ้นต้นด้วย "-" ตัวเดียวได้ (เช่น <c>--days -31</c>)
/// · ตัวเลือกที่ไม่รู้จัก/ซ้ำ/ขาดค่า/ค่านอกรายการ/ขาดตัวที่บังคับ = error ทันที (ไม่เดา) — คำสั่งของทีมงานพิมพ์ผิดต้องไม่ไปทำอย่างอื่น
/// · ตรวจเฉพาะ "รูปร่าง" ของคำสั่ง — ชนิดค่า (ตัวเลข/วันที่) ตรวจที่ <see cref="ValueParsers"/> ตอนรัน
/// </summary>
public static class CommandParser
{
    private static readonly OptionSpec Workspace =
        new("workspace", "รหัสอู่ เช่น SP1042 หรือ 1042", Required: true, ValueHint: "SP1042");

    private static readonly OptionSpec By =
        new("by", "ชื่อผู้กระทำที่ลง sp_WorkspaceAudit (ค่าเริ่มต้น $USER)", ValueHint: "ชื่อ");

    private static readonly OptionSpec Yes = new("yes", "ยืนยันคำสั่งที่ลบ/ย้ายข้อมูล", IsFlag: true);

    // docs/17 §7 — อนุมัติ/ปฏิเสธเรื่องเงิน: --by บังคับ (ใน container $USER ไม่มีความหมาย)
    private static readonly OptionSpec ReviewerBy =
        new("by", "ชื่อทีมงานผู้อนุมัติ/ปฏิเสธ (บังคับ · ลง sp_WorkspaceAudit)", Required: true, ValueHint: "ชื่อ");

    private static readonly OptionSpec PaymentId =
        new("id", "id ของรายการแจ้งชำระ หรือ 8 ตัวอักษรแรก (shortId ในข้อความ LINE)", Required: true, ValueHint: "id");

    public static readonly IReadOnlyList<string> PaymentStatusFilter = ["pending", "approved", "rejected", "cancelled", "all"];
    public static readonly IReadOnlyList<string> RejectReasons =
        ["slip_unreadable", "amount_mismatch", "not_received", "duplicate", "other"];

    public static readonly IReadOnlyList<OptionSpec> GlobalOptions =
    [
        new("verbose", "แสดง log ละเอียดและ stack trace", IsFlag: true),
        new("help", "แสดงวิธีใช้", IsFlag: true)
    ];

    public static readonly IReadOnlyList<CommandSpec> Commands =
    [
        new("list", "รายการอู่ทดลองใช้ทั้งหมด (กรองตามสถานะ/แพ็กเกจได้)",
        [
            new("status", "สถานะของอู่", ValueHint: "status", Allowed: ["provisioning", "active", "expired", "purged"]),
            new("plan", "แพ็กเกจ", ValueHint: "plan", Allowed: ["trial", "paid", "suspended", "converted"])
        ],
        ["spadmin list", "spadmin list --status active --plan trial"]),

        new("show", "รายละเอียดอู่ · บัญชีเจ้าของ · วันแพ็กเกจ · การใช้ลิมิต · audit ล่าสุด",
            [Workspace], ["spadmin show --workspace SP1042"]),

        new("extend", "เลื่อนวันหมดอายุ (Branch.EndPackageDate + สำเนา sp_Workspace) ติดลบได้ · Expired → Active ถ้าวันใหม่ยังไม่ถึง",
        [
            Workspace,
            new("days", "จำนวนวัน (+ ต่ออายุ / - ย่น)", Required: true, ValueHint: "D"),
            new("note", "หมายเหตุลง audit", ValueHint: "ข้อความ"),
            By
        ],
        ["spadmin extend --workspace SP1042 --days 14 --note \"ลูกค้าขอทดลองต่อ\""], Mutates: true),

        new("set-plan", "เปลี่ยนแพ็กเกจ trial|paid|suspended (paid = ทุกฟีเจอร์ ไม่จำกัด · ไม่ใส่ --until = ไม่มีกำหนด)",
        [
            Workspace,
            new("plan", "แพ็กเกจใหม่", Required: true, ValueHint: "plan", Allowed: ["trial", "paid", "suspended"]),
            new("until", "ใช้งานได้ถึงสิ้นวันนี้ (เวลาไทย)", ValueHint: "yyyy-MM-dd"),
            new("note", "หมายเหตุลง audit", ValueHint: "ข้อความ"),
            By
        ],
        ["spadmin set-plan --workspace SP1042 --plan paid --until 2027-12-31"], Mutates: true),

        new("suspend", "ระงับอู่ทันที (เข้าสู่ระบบไม่ได้ · ข้อมูลยังอยู่)",
        [
            Workspace,
            new("reason", "เหตุผล (ลง audit)", Required: true, ValueHint: "ข้อความ"),
            By
        ],
        ["spadmin suspend --workspace SP1042 --reason \"สแปม\""], Mutates: true),

        new("purge", "ลบข้อมูลอู่ทั้งหมดทันที (svc_* · ไฟล์แนบ · ลูกค้า/รถ/พนักงาน) คงไว้แค่อีเมลกันสมัครซ้ำ ย้อนกลับไม่ได้",
        [
            Workspace,
            Yes,
            new("allow-paid", "ยืนยันลบอู่ที่จ่ายเงินแล้ว (paid) หรือย้ายไปสาขาจริงแล้ว (converted)", IsFlag: true),
            By
        ],
        ["spadmin purge --workspace SP1042 --yes"], Mutates: true),

        new("run-jobs", "รันงานรายวันเองทันที (ไม่ลง sp_JobRun) ส่งอีเมล/LINE จริงถ้าตั้งค่าไว้",
        [
            new("task", "งาน (ค่าเริ่มต้น maintenance)", ValueHint: "task", Allowed: ["maintenance", "summary"]),
            new("as-of", "จำลองเวลาปัจจุบัน: ISO (2026-12-31T03:30:00+07:00) · วันที่ (2026-12-31) · ห่างจากตอนนี้ (+91d / -1d)",
                ValueHint: "เวลา"),
            new("date", "ชื่อเดิมของ --as-of", ValueHint: "เวลา")
        ],
        ["spadmin run-jobs", "spadmin run-jobs --task maintenance --as-of +91d"]),

        new("payments", "รายการแจ้งชำระค่าแพ็กเกจ (ค่าเริ่มต้นเฉพาะที่รอตรวจ)",
        [
            new("status", "สถานะ (ค่าเริ่มต้น pending)", ValueHint: "status", Allowed: PaymentStatusFilter),
            new("workspace", "เฉพาะอู่นี้ เช่น SP1042", ValueHint: "SP1042")
        ],
        ["spadmin payments", "spadmin payments --status all --workspace SP1042"]),

        new("payment", "รายละเอียดรายการแจ้งชำระ · สลิปซ้ำ · วันหมดใหม่ถ้าอนุมัติตอนนี้ · ลิงก์ดูสลิป",
            [PaymentId], ["spadmin payment --id 3f2a9c1b"]),

        new("approve-payment", "อนุมัติการชำระ: ต่อวันหมด + เปลี่ยนเป็น paid + ปิดรายการใน transaction เดียว · แจ้งเจ้าของอู่",
        [
            PaymentId,
            ReviewerBy,
            new("until", "กำหนดวันสุดท้ายที่ใช้ได้เอง (เวลาไทย) แทนการต่อตามจำนวนเดือน เช่น แถมวัน", ValueHint: "yyyy-MM-dd"),
            new("note", "หมายเหตุของทีมงาน (บังคับเมื่อยอดไม่ตรงราคามาตรฐาน)", ValueHint: "ข้อความ")
        ],
        [
            "spadmin approve-payment --id 3f2a9c1b --by noi",
            "spadmin approve-payment --id 3f2a9c1b --by noi --note \"ส่วนลดเปิดตัว\""
        ], Mutates: true),

        new("reject-payment", "ปฏิเสธการแจ้งชำระพร้อมเหตุผล · แจ้งเจ้าของอู่ (อีเมล + แจ้งเตือนในระบบ)",
        [
            PaymentId,
            ReviewerBy,
            new("reason", "เหตุผล", Required: true, ValueHint: "reason", Allowed: RejectReasons),
            new("note", "ข้อความถึงลูกค้า (บังคับเมื่อ --reason other)", ValueHint: "ข้อความ")
        ],
        ["spadmin reject-payment --id 3f2a9c1b --by noi --reason not_received"], Mutates: true),

        new("convert", "ย้ายข้อมูลหลัก (หมวดหมู่/คลัง/สินค้า/เทมเพลต/พนักงาน/ลูกค้า/รถ) ไปสาขา GaragePro จริง รัน --dry-run ก่อนเสมอ",
        [
            Workspace,
            new("target-branch", "Branch.Id ปลายทาง (สร้างใน GaragePro Admin กลุ่ม 7 แล้ว)", Required: true, ValueHint: "M"),
            new("target-user", "[User].Id ของพนักงานที่ active ในสาขาปลายทาง (ปกติคือบัญชีเจ้าของอู่)", Required: true,
                ValueHint: "U"),
            new("target-shard", "shard ปลายทาง (ค่าเริ่มต้น db2)", ValueHint: "db2"),
            new("dry-run", "อ่าน+ตรวจอย่างเดียว ไม่เขียนอะไรเลย", IsFlag: true),
            new("out", "เขียนรายงาน JSON ลงไฟล์ (มีชื่อผู้ใช้พนักงานใหม่ เก็บเป็นความลับ)", ValueHint: "report.json"),
            new("allow-failures", "ปิดการย้าย (converted) แม้บางรายการล้ม", IsFlag: true),
            Yes,
            By
        ],
        [
            "spadmin convert --workspace SP1042 --target-branch 431 --target-user 98765 --dry-run --out sp1042-dry.json",
            "spadmin convert --workspace SP1042 --target-branch 431 --target-user 98765 --yes --out sp1042.json"
        ], Mutates: true)
    ];

    public static CommandSpec? Find(string? name) =>
        Commands.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

    public static ParseResult Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0) return new ParseResult(null, null);

        string? commandName = null;
        var tokens = new List<string>();
        foreach (var arg in args)
        {
            if (commandName is null && !arg.StartsWith("--", StringComparison.Ordinal) && arg is not "-h")
            {
                commandName = arg;
                continue;
            }

            tokens.Add(arg);
        }

        if (string.Equals(commandName, "help", StringComparison.OrdinalIgnoreCase))
            return new ParseResult(null, null, tokens.FirstOrDefault(t => !t.StartsWith('-')));

        var wantsHelp = tokens.Any(t => t is "--help" or "-h");
        if (commandName is null)
            return wantsHelp ? new ParseResult(null, null) : new ParseResult(null, "ไม่ได้ระบุคำสั่ง");

        var spec = Find(commandName);
        if (spec is null)
            return new ParseResult(null,
                $"ไม่รู้จักคำสั่ง '{commandName}' (มี {string.Join(", ", Commands.Select(c => c.Name))})");
        if (wantsHelp) return new ParseResult(null, null, spec.Name);

        var all = spec.Options.Concat(GlobalOptions).ToDictionary(o => o.Name, StringComparer.Ordinal);
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (!token.StartsWith("--", StringComparison.Ordinal) || token.Length == 2)
                return Error(spec, $"ไม่เข้าใจ '{token}' ค่าต้องตามหลังชื่อตัวเลือก เช่น --workspace SP1042");

            var body = token[2..];
            string? inlineValue = null;
            var eq = body.IndexOf('=');
            if (eq >= 0)
            {
                inlineValue = body[(eq + 1)..];
                body = body[..eq];
            }

            var name = body.ToLowerInvariant();
            if (!all.TryGetValue(name, out var option))
                return Error(spec, $"คำสั่ง {spec.Name} ไม่มีตัวเลือก --{name}");
            if (options.ContainsKey(name) || flags.Contains(name))
                return Error(spec, $"ระบุ --{name} ซ้ำ");

            if (option.IsFlag)
            {
                if (inlineValue is not null) return Error(spec, $"--{name} ไม่รับค่า");
                flags.Add(name);
                continue;
            }

            var value = inlineValue;
            if (value is null)
            {
                if (i + 1 >= tokens.Count || tokens[i + 1].StartsWith("--", StringComparison.Ordinal))
                    return Error(spec, $"--{name} ต้องมีค่า ({option.ValueHint ?? "ค่า"})");
                value = tokens[++i];
            }

            if (string.IsNullOrWhiteSpace(value)) return Error(spec, $"--{name} ต้องไม่ว่าง");
            if (option.Allowed is { } allowed)
            {
                var normalized = value.Trim().ToLowerInvariant();
                if (!allowed.Contains(normalized))
                    return Error(spec, $"--{name} รับได้เฉพาะ {string.Join("|", allowed)} (ได้ '{value}')");
                value = normalized;
            }

            options[name] = value.Trim();
        }

        var missing = spec.Options.Where(o => o.Required && !options.ContainsKey(o.Name)).Select(o => "--" + o.Name).ToList();
        if (missing.Count > 0) return Error(spec, $"คำสั่ง {spec.Name} ต้องระบุ {string.Join(", ", missing)}");

        // --date เป็นชื่อเดิมของ --as-of (แผน §6 #10) — รับได้ทั้งสองแต่ห้ามใส่พร้อมกัน
        if (options.ContainsKey("date"))
        {
            if (options.ContainsKey("as-of")) return Error(spec, "ใช้ --as-of หรือ --date อย่างใดอย่างหนึ่ง");
            options["as-of"] = options["date"];
            options.Remove("date");
        }

        return new ParseResult(new ParsedCommand(spec.Name, options, flags), null);
    }

    private static ParseResult Error(CommandSpec spec, string message) =>
        new(null, $"{message}\nดูวิธีใช้: spadmin {spec.Name} --help");

    public static string Usage(string? command = null)
    {
        var sb = new StringBuilder();
        if (Find(command) is { } spec)
        {
            sb.AppendLine($"spadmin {spec.Name}: {spec.Summary}");
            sb.AppendLine();
            sb.AppendLine("ตัวเลือก:");
            foreach (var option in spec.Options.Concat(GlobalOptions))
            {
                var head = option.IsFlag ? $"--{option.Name}" : $"--{option.Name} <{option.ValueHint ?? "ค่า"}>";
                var tail = option.Allowed is { } a ? $" [{string.Join("|", a)}]" : "";
                var required = option.Required ? " (บังคับ)" : "";
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {head,-34} {option.Description}{tail}{required}");
            }

            sb.AppendLine();
            sb.AppendLine("ตัวอย่าง:");
            foreach (var example in spec.Examples) sb.AppendLine($"  {example}");
            return sb.ToString();
        }

        sb.AppendLine("spadmin: เครื่องมือทีมงานของอู่ทดลองใช้ ServicePro (shard sp1)");
        sb.AppendLine();
        sb.AppendLine("วิธีใช้: spadmin <คำสั่ง> [ตัวเลือก]   ·   spadmin <คำสั่ง> --help");
        sb.AppendLine();
        sb.AppendLine("คำสั่ง:");
        foreach (var c in Commands) sb.AppendLine(CultureInfo.InvariantCulture, $"  {c.Name,-16} {c.Summary}");
        sb.AppendLine();
        sb.AppendLine("ทุกคำสั่งที่เปลี่ยนข้อมูลบันทึก sp_WorkspaceAudit (ผู้กระทำ = --by หรือ $USER) · purge/convert ต้อง --yes");
        sb.AppendLine("approve-payment/reject-payment ต้องระบุ --by เสมอ (การอนุมัติเรื่องเงิน)");
        sb.AppendLine("ค่าตั้ง: appsettings.json ข้างไฟล์โปรแกรม → dotnet user-secrets ของ API (นอก container) → ตัวแปรแวดล้อม");
        sb.AppendLine("ต้องมี SelfServe:ConnectionString และ LegacyShards:ConnectionStrings:sp1 (convert ต้องมีของ shard ปลายทางด้วย)");
        return sb.ToString();
    }
}

/// <summary>แปลงค่าตัวเลือกเป็นชนิดจริง — ฟังก์ชันล้วน ทดสอบได้โดยไม่มีฐาน</summary>
public static class ValueParsers
{
    public static bool TryParseInt(string? value, out int result) =>
        int.TryParse(value?.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);

    public static bool TryParseLong(string? value, out long result) =>
        long.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out result) && result > 0;

    public static bool TryParseDate(string? value, out DateOnly result) =>
        DateOnly.TryParseExact(value?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

    /// <summary>
    /// เวลาจำลองของ run-jobs → UTC
    /// · <c>+91d</c> / <c>-1d</c> = ห่างจาก <paramref name="nowUtc"/> · <c>2026-12-31</c> = วันนั้นเวลา <paramref name="dailyAtThai"/> (เวลาไทย)
    /// · ISO มี offset (<c>2026-12-31T03:30:00+07:00</c>) ใช้ตามนั้น · ไม่มี offset ถือเป็นเวลาไทย (ทีมงานอยู่ไทย — กันพลาด 7 ชม.)
    /// </summary>
    public static bool TryParseAsOf(string? value, DateTime nowUtc, TimeOnly dailyAtThai, out DateTime utc)
    {
        utc = default;
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0) return false;

        if ((text[0] is '+' or '-') && text.EndsWith('d') && int.TryParse(text[1..^1], NumberStyles.None,
                CultureInfo.InvariantCulture, out var days))
        {
            utc = nowUtc.AddDays(text[0] == '-' ? -days : days);
            return true;
        }

        if (TryParseDate(text, out var date))
        {
            utc = Application.SelfServe.BranchPackageDates.ToUtc(date.ToDateTime(dailyAtThai));
            return true;
        }

        var hasOffset = text.EndsWith('Z') || System.Text.RegularExpressions.Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$");
        if (hasOffset && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset))
        {
            utc = offset.UtcDateTime;
            return true;
        }

        if (!hasOffset && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            utc = Application.SelfServe.BranchPackageDates.ToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
            return true;
        }

        return false;
    }
}
