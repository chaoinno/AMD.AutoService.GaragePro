using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// ชนิดแพ็กเกจของ workspace (= หนึ่งสาขา) — ใน JWT claim <c>plan</c> และ JSON เป็น string ตัวพิมพ์เล็ก
/// (<see cref="PlanKindNames"/>) · [BIZ] Legacy = สาขาใน Garage เดิม (db1/db2) พฤติกรรมเหมือนก่อนมี self-serve ทุกประการ
/// · Trial/Paid/Suspended = อู่ที่สมัครเองใน shard sp1 (docs/12 §6)
/// </summary>
public enum PlanKind
{
    Legacy,
    Trial,
    Paid,
    Suspended
}

/// <summary>
/// ฟีเจอร์ขั้นสูงที่ล็อกได้ต่อแพ็กเกจ (docs/12 §5) — bitmask ใน backend · ใน JSON ส่งเป็น string camelCase ทีละตัว
/// (<see cref="PlanFeatureNames.ToJsonNames"/>) เพื่อให้เว็บ/แอปไม่ต้องรู้ค่าตัวเลข · trial = None · paid/legacy = All
/// [BIZ] การล็อกต้องบังคับที่ API ([RequireFeature] → PLAN_FEATURE_LOCKED) ไม่ใช่แค่ซ่อนเมนู
/// </summary>
[Flags]
public enum PlanFeature
{
    None = 0,
    Purchasing = 1,
    Inventory = 2,
    Pos = 4,
    TaxInvoice = 8,
    AdvancedReports = 16,
    Export = 32,
    MultiBranch = 64,
    All = 127
}

/// <summary>ชนิดของลิมิตจำนวน (docs/12 §5 — มีไว้กันต้นทุน/สแปม ไม่ใช่กันการใช้งานปกติใน 30 วัน)</summary>
public enum PlanLimitKind
{
    Staff,
    CatalogItems,
    Customers,
    Jobs
}

/// <summary>จำนวนสูงสุดต่อชนิด — null = ไม่จำกัด (ถูกตัดออกจาก JSON เพราะ WhenWritingNull)</summary>
public sealed record PlanLimits(int? Staff, int? CatalogItems, int? Customers, int? Jobs)
{
    public static readonly PlanLimits Unlimited = new(null, null, null, null);

    public int? For(PlanLimitKind kind) => kind switch
    {
        PlanLimitKind.Staff => Staff,
        PlanLimitKind.CatalogItems => CatalogItems,
        PlanLimitKind.Customers => Customers,
        PlanLimitKind.Jobs => Jobs,
        _ => null
    };
}

/// <summary>
/// สถานะแพ็กเกจของ workspace ณ เวลาที่ถาม (คำนวณโดย <see cref="IWorkspacePolicy"/> — กฎล้วนอยู่ที่ PlanPolicy ของ B4)
/// · <see cref="IsReadOnly"/> = เลย EndPackageDate แล้ว: ดู/พิมพ์ได้ เขียนไม่ได้ (TRIAL_EXPIRED 403 ที่คำขอเขียน)
/// · <see cref="IsLocked"/> = เลยช่วงอ่านอย่างเดียว (+Trial:ReadOnlyDays) หรือ Suspended: เข้าสู่ระบบไม่ได้
/// </summary>
public sealed record WorkspacePlan(
    PlanKind Kind,
    DateTime? ExpiresAt,
    PlanFeature Features,
    PlanLimits Limits,
    bool IsReadOnly,
    bool IsLocked,
    int? WorkspaceNo)
{
    /// <summary>
    /// สาขา legacy — เปิดทุกฟีเจอร์ ไม่จำกัดจำนวน ไม่มีวันหมดอายุ
    /// [BIZ] ServicePro ยังไม่บังคับ EndPackageDate กับสาขา legacy (แผน §8 เลื่อนไว้) จึงเป็น Paid-like เสมอ
    /// </summary>
    public static readonly WorkspacePlan Legacy =
        new(PlanKind.Legacy, null, PlanFeature.All, PlanLimits.Unlimited, false, false, null);

    public bool HasFeature(PlanFeature feature) => (Features & feature) == feature;
}

/// <summary>
/// แหล่งความจริงเรื่องแพ็กเกจ/ลิมิตต่อ (shard, สาขา) — ทุกจุดที่ล็อกฟีเจอร์หรือนับลิมิตต้องถามที่นี่ ไม่ตัดสินเองจาก claim
/// · shard ≠ sp1 ต้องคืน <see cref="WorkspacePlan.Legacy"/> โดยไม่แตะฐานข้อมูล (พฤติกรรมของลูกค้าเดิมห้ามเปลี่ยน)
/// </summary>
public interface IWorkspacePolicy
{
    Task<WorkspacePlan> GetAsync(string shardKey, int branchId, CancellationToken ct = default);

    /// <summary>null = เพิ่มได้ · ApiError = ครบลิมิตแล้ว (PLAN_LIMIT_REACHED 422) — service เรียกก่อน insert แถวใหม่</summary>
    Task<ApiError?> CheckLimitAsync(string shardKey, int branchId, PlanLimitKind kind, CancellationToken ct = default);
}

/// <summary>
/// นโยบายเริ่มต้น: ทุกอย่างเป็น legacy ไม่จำกัด — ใช้เป็นค่า default ของ ctor service (กัน ctor ในเทสต์เดิมพัง)
/// ในเทสต์ และเป็น implementation ที่ลงทะเบียนไว้จนกว่า WorkspacePolicy จริง (B4) จะมาแทนใน AddSelfServeInfrastructure
/// </summary>
public sealed class UnlimitedWorkspacePolicy : IWorkspacePolicy
{
    public static readonly UnlimitedWorkspacePolicy Instance = new();

    public Task<WorkspacePlan> GetAsync(string shardKey, int branchId, CancellationToken ct = default) =>
        Task.FromResult(WorkspacePlan.Legacy);

    public Task<ApiError?> CheckLimitAsync(string shardKey, int branchId, PlanLimitKind kind,
        CancellationToken ct = default) =>
        Task.FromResult<ApiError?>(null);
}

/// <summary>ชื่อ claim/JSON ของ <see cref="PlanKind"/> — ตัวพิมพ์เล็กเสมอ (<c>trial|paid|legacy|suspended</c>)</summary>
public static class PlanKindNames
{
    public static string ToJsonName(PlanKind kind) => kind.ToString().ToLowerInvariant();

    /// <summary>ไม่สนตัวพิมพ์ · ตัวเลขดิบหรือค่าที่ไม่รู้จัก = false (claim จากรุ่นอื่นต้องไม่กลายเป็นค่าที่ไม่มีจริง)</summary>
    public static bool TryParse(string? value, out PlanKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind);
}

/// <summary>
/// ชื่อ JSON (camelCase) และป้ายภาษาไทยของแต่ละ <see cref="PlanFeature"/> — สัญญากับเว็บ/แอป:
/// <c>purchasing, inventory, pos, taxInvoice, advancedReports, export, multiBranch</c> ห้ามเปลี่ยนชื่อโดยไม่แก้ทุกฝั่ง
/// </summary>
public static class PlanFeatureNames
{
    private static readonly (PlanFeature Flag, string Json, string LabelTh)[] Known =
    [
        (PlanFeature.Purchasing, "purchasing", "จัดซื้อ (PR/PO/รับสินค้า/ซัพพลายเออร์)"),
        (PlanFeature.Inventory, "inventory", "สต็อก/คลังสินค้า/ใบเบิกสินค้า"),
        (PlanFeature.Pos, "pos", "ขายสินค้าหน้าร้าน (POS)/โปรโมชัน"),
        (PlanFeature.TaxInvoice, "taxInvoice", "ใบกำกับภาษีเต็มรูป"),
        (PlanFeature.AdvancedReports, "advancedReports", "รายงานขั้นสูง"),
        (PlanFeature.Export, "export", "ส่งออกข้อมูล (Excel)"),
        (PlanFeature.MultiBranch, "multiBranch", "หลายสาขา/สลับสาขา")
    ];

    /// <summary>bitmask → รายชื่อ camelCase เรียงตามบิต · None → ว่าง · All → ครบ 7 ตัว</summary>
    public static string[] ToJsonNames(PlanFeature features) =>
        Known.Where(k => (features & k.Flag) == k.Flag).Select(k => k.Json).ToArray();

    /// <summary>
    /// รายชื่อ → bitmask · ไม่สนตัวพิมพ์ · ชื่อที่ไม่รู้จักถูกข้าม (ไม่โยน) เพื่อให้ JSON/claim ที่มีฟีเจอร์ใหม่กว่าโค้ดนี้ยังอ่านได้
    /// ถ้าต้องการตรวจเข้มใช้ <see cref="TryParseOne"/>
    /// </summary>
    public static PlanFeature Parse(IEnumerable<string>? names)
    {
        var result = PlanFeature.None;
        if (names is null) return result;

        foreach (var name in names)
            if (TryParseOne(name, out var flag)) result |= flag;

        return result;
    }

    public static bool TryParseOne(string? name, out PlanFeature feature)
    {
        var trimmed = name?.Trim();
        foreach (var k in Known)
        {
            if (!string.Equals(k.Json, trimmed, StringComparison.OrdinalIgnoreCase)) continue;
            feature = k.Flag;
            return true;
        }

        feature = PlanFeature.None;
        return false;
    }

    /// <summary>ป้ายไทยสำหรับข้อความ error/UI — หลายบิตคั่นด้วย " · "</summary>
    public static string LabelTh(PlanFeature features)
    {
        var labels = Known.Where(k => (features & k.Flag) == k.Flag).Select(k => k.LabelTh).ToArray();
        return labels.Length == 0 ? "ฟีเจอร์ขั้นสูง" : string.Join(" · ", labels);
    }
}

/// <summary>ชื่อ JSON และป้ายไทยของ <see cref="PlanLimitKind"/> — ใช้ใน PLAN_LIMIT_REACHED และ <c>GET /selfserve/status.limits</c></summary>
public static class PlanLimitNames
{
    public static string ToJsonName(PlanLimitKind kind) => kind switch
    {
        PlanLimitKind.Staff => "staff",
        PlanLimitKind.CatalogItems => "catalog",
        PlanLimitKind.Customers => "customers",
        PlanLimitKind.Jobs => "jobs",
        _ => kind.ToString().ToLowerInvariant()
    };

    public static string LabelTh(PlanLimitKind kind) => kind switch
    {
        PlanLimitKind.Staff => "พนักงาน",
        PlanLimitKind.CatalogItems => "สินค้า/บริการ",
        PlanLimitKind.Customers => "ลูกค้า",
        PlanLimitKind.Jobs => "จ๊อบ",
        _ => kind.ToString()
    };
}
