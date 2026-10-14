using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Conversion;

/// <summary>
/// ชนิดแถวใน sp_ConversionMap — ค่าที่ซ้ำกับ ledger ข้อมูลตัวอย่าง (sp_SampleEntity) ใช้ชื่อเดียวกัน
/// (<see cref="StarterDataService.EntityTypes"/>) เพื่อให้ตัดแถวตัวอย่างออกด้วย (ชนิด, id) คู่เดียวกันได้ตรงๆ
/// </summary>
public static class ConversionEntityTypes
{
    public const string CatalogCategory = "CatalogCategory";
    public const string Warehouse = "Warehouse";
    public const string CatalogItem = StarterDataService.EntityTypes.CatalogItem;
    public const string QuotationTemplate = StarterDataService.EntityTypes.QuotationTemplate;
    public const string Staff = StarterDataService.EntityTypes.Staff;
    public const string Customer = StarterDataService.EntityTypes.Customer;
    public const string Car = StarterDataService.EntityTypes.Car;

    /// <summary>ลำดับที่ย้ายจริง (และลำดับในรายงาน) — แม่ก่อนลูกเสมอ</summary>
    public static readonly IReadOnlyList<string> Order =
        [CatalogCategory, Warehouse, CatalogItem, QuotationTemplate, Staff, Customer, Car];
}

/// <summary>
/// คำขอย้ายข้อมูลหลักของอู่ทดลองใช้ไปสาขา GaragePro จริง (แผน S5)
/// · <paramref name="TargetBranchId"/> ต้องสร้างไว้ก่อนด้วย GaragePro Admin (กลุ่ม 7) · <paramref name="TargetUserId"/> = [User].Id
///   ของพนักงานที่ active ในสาขานั้น (ปกติคือบัญชีเจ้าของอู่ที่ทีมงานสร้างให้) — ทุกแถวถูกสร้างในนามบัญชีนี้
/// · <paramref name="AllowFailures"/> = ปิดการย้าย (Plan=converted) แม้บางแถวย้ายไม่สำเร็จ — ค่าเริ่มต้นไม่ปิด
///   เพราะ converted = อ่านอย่างเดียว เจ้าของอู่จะแก้ข้อมูลต้นทางที่ผิดเองไม่ได้อีก
/// </summary>
public sealed record ConversionRequest(
    int WorkspaceNo,
    string TargetShardKey,
    int TargetBranchId,
    long TargetUserId,
    bool DryRun,
    string Actor,
    bool AllowFailures = false);

/// <summary>แถว sp_Workspace (+ [User].Id ของเจ้าของจาก sp_Account) ที่การย้ายต้องรู้</summary>
public sealed record ConversionWorkspace(
    Guid Id,
    int WorkspaceNo,
    int? BranchId,
    string Name,
    WorkspacePlanKind Plan,
    WorkspaceStatus Status,
    long? OwnerUserId,
    DateTime? ConvertedAt,
    string? ConvertedToShardKey,
    int? ConvertedToBranchId,
    DateTime? PurgeAfter);

public sealed record SourceCategory(Guid Id, string Code, string Name, Guid? ParentCategoryId, int? SortOrder);

public sealed record SourceWarehouse(Guid Id, string Code, string Name, string? Address);

/// <summary>
/// สินค้า/บริการต้นทาง — Reserved/OnOrder ไม่ย้าย (ผูกกับจ๊อบ/ใบสั่งซื้อที่ไม่ได้ย้าย) · <see cref="StockManaged"/> = ยอดมาจากล็อต FIFO
/// ซึ่งไม่ได้ย้าย (คัดลอกเป็นตัวเลข OnHand ธรรมดา)
/// </summary>
public sealed record SourceCatalogItem(
    Guid Id,
    string Code,
    LineType Type,
    string Name,
    string? Compatibility,
    string Unit,
    decimal Cost,
    decimal Price,
    decimal? StandardHours,
    int OnHand,
    int Damaged,
    string? EtaNote,
    Guid? CategoryId,
    Guid? WarehouseId,
    bool StockManaged);

public sealed record SourceTemplate(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<QuotationTemplateLineInput> Lines);

/// <summary>
/// พนักงานต้นทาง — <see cref="Role"/> จาก svc_UserRoleOverride ของ sp1 (null = ไม่มี override) ·
/// <see cref="IsOwner"/> = บัญชีเจ้าของอู่ (sp_Account.UserId) ไม่สร้างซ้ำ — ใช้บัญชีปลายทางที่ทีมงานสร้างไว้แทน
/// </summary>
public sealed record SourceStaff(StaffDetailDto Detail, UserRole? Role, bool IsOwner);

/// <summary>
/// ข้อมูลหลักที่ยังใช้งานอยู่ของอู่ใน shard self-serve — <b>รวมแถวตัวอย่าง</b> ไว้ด้วย
/// · <see cref="SampleKeys"/> = (ชนิด, id) จาก sp_SampleEntity ของอู่นี้ — ConversionService เป็นผู้ตัดออก (กติกาอยู่ที่เดียว เทสต์ได้)
/// </summary>
public sealed record ConversionSource(
    IReadOnlyList<SourceCategory> Categories,
    IReadOnlyList<SourceWarehouse> Warehouses,
    IReadOnlyList<SourceCatalogItem> CatalogItems,
    IReadOnlyList<SourceTemplate> Templates,
    IReadOnlyList<SourceStaff> Staff,
    IReadOnlyList<CustomerDetailDto> Customers,
    IReadOnlyList<VehicleDetailDto> Vehicles,
    IReadOnlySet<(string EntityType, string EntityId)> SampleKeys);

/// <summary>หนึ่งแถวของ sp_ConversionMap — (ชนิด, id ต้นทาง) → id ปลายทาง ของ (shard, สาขา) ปลายทาง</summary>
public sealed record ConversionMapEntry(
    string EntityType,
    string SourceId,
    string TargetId,
    string TargetShardKey,
    int TargetBranchId);

/// <summary>
/// อ่านข้อมูลหลักของอู่จาก shard self-serve (ตาราง svc_* ผ่าน EF · ตารางรูปร่าง Garage ผ่าน repository เดิม · sp_* ผ่าน SelfServeDbContext)
/// · อ่านอย่างเดียว — ไม่เขียนอะไรทั้งสิ้น (dry-run ใช้ตัวนี้ตัวเดียวกัน)
/// </summary>
public interface IConversionSourceReader
{
    Task<ConversionWorkspace?> FindWorkspaceAsync(int workspaceNo, CancellationToken ct = default);

    Task<ConversionSource> ReadAsync(ConversionWorkspace workspace, CancellationToken ct = default);
}

/// <summary>
/// ledger ของการย้าย (sp_ConversionMap) + ปิดการย้าย (sp_Workspace) + audit — เขียนเฉพาะฐาน GarageSelfServe
/// · <see cref="AddMapAsync"/> ถูกเรียกทันทีหลังสร้างแต่ละแถวสำเร็จ: ล้มกลางทางแล้วรันใหม่จะข้ามแถวที่ย้ายแล้ว ไม่สร้างซ้ำ
/// </summary>
public interface IConversionStore
{
    Task<IReadOnlyList<ConversionMapEntry>> GetMapAsync(Guid workspaceId, CancellationToken ct = default);

    Task AddMapAsync(Guid workspaceId, ConversionMapEntry entry, DateTime now, CancellationToken ct = default);

    /// <summary>
    /// Plan=Converted · ConvertedAt (ครั้งแรกเท่านั้น) · ConvertedToShardKey/BranchId · PurgeAfter = ค่าที่มากกว่าระหว่างค่าเดิมกับ
    /// <paramref name="purgeAfter"/> (ทีมงานเลื่อนการลบได้ การรันซ้ำไม่ทำให้ลบเร็วขึ้น) · audit "convert"
    /// </summary>
    Task MarkConvertedAsync(Guid workspaceId, string targetShardKey, int targetBranchId, DateTime convertedAt,
        DateTime purgeAfter, string actor, string payloadJson, CancellationToken ct = default);

    Task AddAuditAsync(Guid workspaceId, string action, string actor, string? note, string? payloadJson,
        DateTime now, CancellationToken ct = default);
}

/// <summary>ผลต่อชนิดข้อมูล — คลาส (ไม่ใช่ record) เพราะนับสะสมระหว่างย้าย · serialize เป็น JSON ของ --out ได้ตรงๆ</summary>
public sealed class ConversionEntityReport(string entityType)
{
    public string EntityType { get; } = entityType;

    /// <summary>แถวต้นทางที่ยังใช้งานอยู่ (หลังตัดแถวตัวอย่าง)</summary>
    public int Source { get; set; }

    /// <summary>แถวตัวอย่าง (sp_SampleEntity) ที่ไม่ย้าย</summary>
    public int SampleExcluded { get; set; }

    /// <summary>สร้างใหม่ที่ปลายทาง (dry-run = คาดว่าจะสร้าง)</summary>
    public int Created { get; set; }

    /// <summary>ปลายทางมีอยู่แล้ว (รหัสซ้ำ/ลูกค้าซ้ำ/เจ้าของอู่) — จับคู่กับของเดิมแทนการสร้างซ้ำ</summary>
    public int MappedExisting { get; set; }

    /// <summary>ย้ายไปแล้วในรอบก่อน (มีใน sp_ConversionMap) — ข้าม</summary>
    public int AlreadyConverted { get; set; }

    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<string> Messages { get; } = [];
}

/// <summary>
/// บัญชีพนักงานที่สร้างในสาขาปลายทาง — [SECURITY] สาขา legacy ตั้งชื่อผู้ใช้ = รหัสผ่าน = รหัสพนักงาน (plaintext ตามระบบเดิม)
/// รายงานจึงมีค่าที่ใช้เข้าระบบได้: เก็บไฟล์รายงานเป็นความลับ ส่งให้เจ้าของอู่แล้วลบทิ้ง และให้พนักงานเปลี่ยนรหัสผ่านทันที
/// </summary>
public sealed record ConversionStaffAccount(long SourceStaffId, long TargetStaffId, string FullName, string UserName);

public sealed record ConversionReport(
    string WorkspaceCode,
    int WorkspaceNo,
    bool DryRun,
    string TargetShardKey,
    int TargetBranchId,
    long TargetUserId,
    DateTime StartedAt,
    DateTime FinishedAt,
    bool Finished,
    IReadOnlyList<ConversionEntityReport> Entities,
    IReadOnlyList<ConversionStaffAccount> StaffAccounts,
    IReadOnlyList<string> ImagesNotCopied,
    IReadOnlyList<string> Notes)
{
    public int TotalFailed => Entities.Sum(e => e.Failed);
}
