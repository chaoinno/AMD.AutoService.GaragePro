using System.Globalization;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.CatalogCategories;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Application.QuotationTemplates;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Application.Warehouses;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Conversion;

public interface IConversionService
{
    Task<Result<ConversionReport>> ConvertAsync(ConversionRequest request, CancellationToken ct = default);
}

/// <summary>
/// ย้าย<b>ข้อมูลหลัก</b>ของอู่ทดลองใช้ (shard sp1) ไปเป็นสาขา GaragePro จริง (db2) เมื่อลูกค้าจ่ายเงิน — แผน S5
/// · [BIZ] ย้ายเฉพาะ หมวดหมู่ · คลัง (เพื่อผูกสินค้า) · สินค้า/บริการ · เทมเพลตใบเสนอราคา · พนักงาน · ลูกค้า · รถ (+เจ้าของ)
///   ไม่ย้ายจ๊อบ/ใบเสนอราคา/ใบเสร็จ/เวลาทำงาน/ซัพพลายเออร์/รูปภาพ (การตัดสินใจของผู้ใช้ 2026-10-13)
/// · [BIZ] เขียนปลายทางผ่าน service เดิมเท่านั้น (ICatalogCategoryService · IWarehouseService · ICatalogService ·
///   IQuotationTemplateService · IStaffService · ICustomerVehicleService) ในนาม <c>ScopedCurrentUser(db2, M, U, Manager, Web)</c>
///   — การเขียน Garage legacy (ลูกค้า/รถ/พนักงาน) จึงอยู่ในทางเขียนเดิมที่มีข้อยกเว้นอยู่แล้ว ไม่มีคำสั่ง SQL ใหม่ (CLAUDE.md [RISK] 2026-09-04)
/// · แถวตัวอย่าง (sp_SampleEntity) และแถวที่ปิดใช้งาน/ถูกลบไม่ย้าย · ทุกแถวที่สำเร็จ (สร้างใหม่หรือจับคู่ของเดิม) ลง sp_ConversionMap
///   ทันที — รันซ้ำข้ามแถวที่ย้ายแล้ว (ล้มกลางทางแล้วรันใหม่ได้) · แถวหนึ่งล้มไม่หยุดแถวอื่น
/// · dry-run: อ่านต้นทาง+ปลายทางและตรวจความถูกต้องด้วยกติกาเดียวกับ service (validator) — <b>ไม่เขียนอะไรเลย</b> ทั้งปลายทาง/ledger/audit
/// · ปิดการย้าย (Plan=converted · ConvertedAt · PurgeAfter = +30 วัน) เฉพาะเมื่อไม่มีแถวที่ล้ม หรือทีมงานยืนยัน AllowFailures —
///   converted = อ่านอย่างเดียว (PlanPolicy) เจ้าของอู่จะแก้ข้อมูลต้นทางที่ทำให้ล้มไม่ได้อีก
/// </summary>
public sealed class ConversionService(
    IShardScopeFactory scopes,
    IConversionSourceReader source,
    IConversionStore store,
    ILegacyUserReader users,
    SelfServeOptions options,
    TimeProvider clock) : IConversionService
{
    /// <summary>[ASSUME] เก็บข้อมูลอู่ใน sp1 ไว้อีกเท่านี้หลังย้าย (ให้ตรวจเทียบ/ย้ายซ้ำได้) แล้ว job รายวันลบ</summary>
    public static readonly TimeSpan PurgeDelay = TimeSpan.FromDays(30);

    public const string AuditAction = "convert";
    public const string PartialAuditAction = "convert-partial";
    public const string DryRunTargetId = "(dry-run)";

    /// <summary>
    /// ตำแหน่ง/หน่วยงาน (ชื่อ) ตามบทบาท — ชื่อของ id ใน <see cref="SelfServeStaffRoles"/> ตรวจจาก db2 จริง 2026-10-14
    /// · [BIZ] สาขา legacy ไม่มี svc_UserRoleOverride: RoleMapper ตัดสินบทบาทจากชื่อตำแหน่ง/หน่วยงาน จึงจับคู่ด้วยชื่อตามบทบาทใน sp1
    ///   ก่อน (บทบาทจริงที่เจ้าของอู่เลือก) แล้วค่อยใช้ชื่อตำแหน่ง/หน่วยงานที่บันทึกไว้
    /// </summary>
    public static readonly IReadOnlyDictionary<UserRole, (string Position, string Sector, string Department)> RoleNames =
        new Dictionary<UserRole, (string, string, string)>
        {
            [UserRole.Manager] = ("ผู้จัดการ", "ผู้จัดการ", "ฝ่ายบริหาร"),
            [UserRole.Lead] = ("หัวหน้าแผนก", "ควบคุมงานซ่อม", "ฝ่ายโรงงาน"),
            [UserRole.Technician] = ("พนักงาน", "งานซ่อมทั่วไป", "ฝ่ายโรงงาน"),
            [UserRole.FrontDesk] = ("พนักงาน", "รับรถ", "ฝ่ายฟรอนท์"),
            [UserRole.Office] = ("พนักงาน", "ธุรการ", "ฝ่ายแบคออฟฟิต"),
            [UserRole.Cashier] = ("พนักงาน", "บัญชี", "ฝ่ายแบคออฟฟิต")
        };

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<ConversionReport>> ConvertAsync(ConversionRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var shard = request.TargetShardKey?.Trim() ?? string.Empty;
        if (shard.Length == 0)
            return Fail(SelfServeAdminErrors.Validation("ต้องระบุ shard ปลายทาง (--target-shard)", "targetShard"));
        if (request.WorkspaceNo <= 0)
            return Fail(SelfServeAdminErrors.Validation("เลขอู่ไม่ถูกต้อง", "workspace"));
        if (request.TargetBranchId <= 0)
            return Fail(SelfServeAdminErrors.Validation("ต้องระบุสาขาปลายทาง (--target-branch)", "targetBranch"));
        if (request.TargetUserId <= 0)
            return Fail(SelfServeAdminErrors.Validation("ต้องระบุผู้ใช้ปลายทาง (--target-user)", "targetUser"));

        // [SECURITY] ปลายทางต้องเป็นสาขาจริง ไม่ใช่ shard ทดลองใช้เอง — ไม่งั้น "ย้าย" = คัดลอกซ้อนในฐานเดียวกัน
        if (SelfServeText.IsSelfServeShard(shard, options))
            return Fail(SelfServeAdminErrors.ConversionRefused(
                $"shard ปลายทาง '{shard}' เป็น shard ของอู่ทดลองใช้เอง — ปลายทางต้องเป็นสาขา GaragePro จริง (เช่น db2)"));

        var actor = string.IsNullOrWhiteSpace(request.Actor) ? "spadmin" : request.Actor.Trim();

        var workspace = await source.FindWorkspaceAsync(request.WorkspaceNo, ct);
        if (workspace is null) return Fail(SelfServeLifecycleErrors.WorkspaceNotFound());
        var code = SelfServeText.WorkspaceCode(workspace.WorkspaceNo);

        if (workspace.Status is not (WorkspaceStatus.Active or WorkspaceStatus.Expired))
            return Fail(SelfServeAdminErrors.ConversionRefused(
                $"อู่ {code} อยู่สถานะ {workspace.Status} — ย้ายได้เฉพาะอู่ที่ใช้งานอยู่ (Active) หรือหมดอายุ (Expired)"));
        if (workspace.BranchId is not { } sourceBranch)
            return Fail(SelfServeAdminErrors.ConversionRefused($"อู่ {code} ยังไม่มีสาขาใน shard ทดลองใช้"));
        if (workspace.ConvertedToBranchId is { } convertedBranch
            && (convertedBranch != request.TargetBranchId
                || !string.Equals(workspace.ConvertedToShardKey, shard, StringComparison.OrdinalIgnoreCase)))
            return Fail(SelfServeAdminErrors.ConversionRefused(
                $"อู่ {code} ถูกย้ายไป {workspace.ConvertedToShardKey}/{convertedBranch} แล้ว — รันซ้ำได้เฉพาะปลายทางเดิม"));

        var (targetError, targetUser) = await ValidateTargetAsync(shard, request.TargetBranchId, request.TargetUserId, ct);
        if (targetError is not null) return Fail(targetError);

        var existing = await store.GetMapAsync(workspace.Id, ct);
        var foreign = existing.FirstOrDefault(e =>
            e.TargetBranchId != request.TargetBranchId
            || !string.Equals(e.TargetShardKey, shard, StringComparison.OrdinalIgnoreCase));
        if (foreign is not null)
            return Fail(SelfServeAdminErrors.ConversionRefused(
                $"อู่ {code} มีรายการที่ย้ายไป {foreign.TargetShardKey}/{foreign.TargetBranchId} แล้ว — รันซ้ำได้เฉพาะปลายทางเดิม"));

        var data = await source.ReadAsync(workspace, ct);
        var started = Now;

        var run = new ConversionRun(request with { TargetShardKey = shard, Actor = actor }, workspace, data,
            existing, store, targetUser!.StaffId!.Value, () => Now);

        // ทุกอย่างของปลายทาง (อ่าน+เขียน) อยู่ใน scope ของผู้ใช้ปลายทาง: ServiceDbContext เลือก GarageService ตาม shard
        // และ Dapper ของลูกค้า/รถ/พนักงานเปิด LegacyShards:ConnectionStrings:{shard} · Source=Web แบบเดียวกับผู้จัดการกดเองบนเว็บ
        var asTarget = new ScopedCurrentUser(shard, request.TargetBranchId, request.TargetUserId,
            Limit($"spadmin ({actor})", 100), UserRole.Manager, EventSource.Web, targetUser.StaffId);
        await scopes.RunAsAsync(asTarget, (sp, token) => run.ExecuteAsync(sp, token), ct);

        var finished = false;
        if (!request.DryRun)
        {
            var payload = JsonSerializer.Serialize(new
            {
                targetShardKey = shard,
                targetBranchId = request.TargetBranchId,
                targetUserId = request.TargetUserId,
                sourceBranchId = sourceBranch,
                failed = run.TotalFailed,
                entities = run.Reports.ToDictionary(r => r.EntityType, r => new
                {
                    r.Created, r.MappedExisting, r.AlreadyConverted, r.Skipped, r.Failed
                })
            });

            if (run.TotalFailed == 0 || request.AllowFailures)
            {
                var now = Now;
                await store.MarkConvertedAsync(workspace.Id, shard, request.TargetBranchId, now, now + PurgeDelay,
                    actor, payload, ct);
                finished = true;
            }
            else
            {
                await store.AddAuditAsync(workspace.Id, PartialAuditAction, actor,
                    $"ย้ายไม่สำเร็จ {run.TotalFailed} รายการ — ยังไม่ปิดการย้าย (แก้แล้วรันซ้ำ หรือยืนยันด้วย --allow-failures)",
                    payload, Now, ct);
            }
        }

        var notes = new List<string>
        {
            "ไม่ย้าย: จ๊อบ · ใบเสนอราคา · ใบเสร็จ/การชำระเงิน · เวลาทำงานช่าง · ซัพพลายเออร์/ใบสั่งซื้อ/ล็อตสต็อก (ย้ายเฉพาะข้อมูลหลัก)",
            "รูปภาพ (พนักงาน/รถ) ไม่ถูกคัดลอก — ดูรายการใน imagesNotCopied แล้วอัปโหลดใหม่ที่สาขาปลายทางถ้าต้องการ"
        };
        if (run.StaffAccounts.Count > 0)
            notes.Add("[SECURITY] พนักงานที่สร้างใหม่ใช้กติกาสาขาเดิม: ชื่อผู้ใช้ = รหัสผ่าน = รหัสพนักงาน — " +
                      "ส่งให้เจ้าของอู่แล้วให้เปลี่ยนรหัสผ่านทันที และเก็บไฟล์รายงานนี้เป็นความลับ");
        if (request.DryRun)
            notes.Add("dry-run: ไม่ได้เขียนอะไรเลย · ตัวเลข Created/MappedExisting เป็นการคาดการณ์จากข้อมูลปลายทาง ณ ตอนนี้");
        else if (!finished)
            notes.Add($"ยังไม่ปิดการย้าย: ล้ม {run.TotalFailed} รายการ — แก้สาเหตุแล้วรันคำสั่งเดิมซ้ำ (แถวที่ย้ายแล้วจะถูกข้าม)");

        return Result<ConversionReport>.Ok(new ConversionReport(
            code, workspace.WorkspaceNo, request.DryRun, shard, request.TargetBranchId, request.TargetUserId,
            started, Now, finished, run.Reports, run.StaffAccounts, run.Images, notes));
    }

    /// <summary>
    /// ปลายทางต้องเป็น: ผู้ใช้ที่ active (User/Staff Status=1) · เป็นพนักงานของสาขานั้น · สาขา active กลุ่ม Service (7)
    /// · ใช้ ILegacyUserReader เดิม (อ่านอย่างเดียว) — GetAccessibleBranchesAsync ของพนักงานคืนสาขาตัวเองเฉพาะเมื่อ active+กลุ่ม 7
    ///   (ผู้ดูแลระบบได้ทุกสาขากลุ่ม 7) จึงตอบได้ทั้งสองเงื่อนไขโดยไม่ต้องเขียน SQL ใหม่
    /// </summary>
    private async Task<(ApiError? Error, LegacyUserDto? User)> ValidateTargetAsync(string shard, int branchId,
        long userId, CancellationToken ct)
    {
        LegacyUserDto? user;
        try
        {
            user = await users.FindByIdAsync(shard, userId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return (SelfServeAdminErrors.ConversionTargetInvalid($"ไม่รู้จัก shard '{shard}' ({ex.Message})"), null);
        }

        if (user is null)
            return (SelfServeAdminErrors.ConversionTargetInvalid(
                $"ไม่พบผู้ใช้ {userId} ที่ใช้งานอยู่ (User/Staff Status = 1) ใน {shard}"), null);
        if (user.StaffId is null)
            return (SelfServeAdminErrors.ConversionTargetInvalid($"ผู้ใช้ {userId} ไม่ได้ผูกกับพนักงาน"), null);
        if (user.BranchId != branchId)
            return (SelfServeAdminErrors.ConversionTargetInvalid(
                $"ผู้ใช้ {userId} เป็นพนักงานของสาขา {user.BranchId?.ToString(CultureInfo.InvariantCulture) ?? "-"} ไม่ใช่สาขา {branchId}"),
                null);

        var branches = await users.GetAccessibleBranchesAsync(shard, user, ct);
        if (!branches.Any(b => b.BranchId == branchId))
            return (SelfServeAdminErrors.ConversionTargetInvalid(
                $"สาขา {branchId} ไม่ได้เปิดใช้งาน หรือไม่ใช่กลุ่ม Service (BranchGroupId 7) — สร้าง/เปิดใน GaragePro Admin ก่อน"),
                null);

        return (null, user);
    }

    private static Result<ConversionReport> Fail(ApiError error) => Result<ConversionReport>.Fail(error);

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];

    /// <summary>
    /// สถานะของการย้ายหนึ่งรอบ (ใน scope ของผู้ใช้ปลายทาง) — แยกจาก service เพื่อให้แต่ละขั้นอ่านง่าย
    /// · <see cref="_map"/> = (ชนิด, id ต้นทาง) → id ปลายทาง ทั้งจากรอบก่อน (ledger) และรอบนี้ · dry-run ใส่ <see cref="DryRunTargetId"/>
    /// </summary>
    private sealed class ConversionRun
    {
        private readonly ConversionRequest _request;
        private readonly ConversionWorkspace _workspace;
        private readonly ConversionSource _data;
        private readonly IConversionStore _store;
        private readonly long _targetStaffId;
        private readonly Func<DateTime> _now;
        private readonly HashSet<string> _previous = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _map = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _samples;
        private readonly Dictionary<string, ConversionEntityReport> _reports;

        // สถานะปลายทางที่อ่านก่อนเริ่ม (รหัส → id) — ใช้จับคู่ของที่มีอยู่แล้วแทนการสร้างซ้ำ
        private Dictionary<string, (Guid Id, bool IsActive)> _targetCategories = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, (Guid Id, bool IsActive)> _targetWarehouses = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Guid> _targetCatalog = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, Guid> _targetTemplates = new(StringComparer.OrdinalIgnoreCase);
        private StaffReferenceDataDto _refs = new([], [], [], [], []);
        private IReadOnlyList<LookupItemDto> _sectors = [];

        public ConversionRun(ConversionRequest request, ConversionWorkspace workspace, ConversionSource data,
            IReadOnlyList<ConversionMapEntry> existing, IConversionStore store, long targetStaffId, Func<DateTime> now)
        {
            _request = request;
            _workspace = workspace;
            _data = data;
            _store = store;
            _targetStaffId = targetStaffId;
            _now = now;
            foreach (var entry in existing)
            {
                var key = Key(entry.EntityType, entry.SourceId);
                _previous.Add(key);
                _map[key] = entry.TargetId;
            }

            _samples = data.SampleKeys.Select(k => Key(k.EntityType, k.EntityId)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            _reports = ConversionEntityTypes.Order.ToDictionary(t => t, t => new ConversionEntityReport(t));
        }

        public IReadOnlyList<ConversionEntityReport> Reports => ConversionEntityTypes.Order.Select(t => _reports[t]).ToList();
        public List<ConversionStaffAccount> StaffAccounts { get; } = [];
        public List<string> Images { get; } = [];
        public int TotalFailed => _reports.Values.Sum(r => r.Failed);

        private bool DryRun => _request.DryRun;

        public async Task ExecuteAsync(IServiceProvider sp, CancellationToken ct)
        {
            var categories = sp.GetRequiredService<ICatalogCategoryService>();
            var warehouses = sp.GetRequiredService<IWarehouseService>();
            var catalog = sp.GetRequiredService<ICatalogService>();
            var templates = sp.GetRequiredService<IQuotationTemplateService>();
            var staff = sp.GetRequiredService<IStaffService>();
            var customers = sp.GetRequiredService<ICustomerVehicleService>();

            await ReadTargetAsync(categories, warehouses, catalog, templates, staff, ct);

            // ลำดับนี้คือสัญญา: แม่ก่อนลูกเสมอ (หมวดแม่ → หมวดลูก → สินค้า → เทมเพลตที่อ้างรหัสสินค้า · ลูกค้า → รถ)
            await CategoriesAsync(categories, ct);
            await WarehousesAsync(warehouses, ct);
            await CatalogItemsAsync(catalog, ct);
            await TemplatesAsync(templates, ct);
            await StaffAsync(staff, ct);
            await CustomersAsync(customers, ct);
            await VehiclesAsync(customers, ct);
        }

        // ───────────────────────────────────────────── อ่านปลายทาง

        private async Task ReadTargetAsync(ICatalogCategoryService categories, IWarehouseService warehouses,
            ICatalogService catalog, IQuotationTemplateService templates, IStaffService staff, CancellationToken ct)
        {
            var categoryTree = Required(await categories.SearchAsync(null, includeInactive: true, ct), "หมวดหมู่ปลายทาง");
            _targetCategories = Flatten(categoryTree)
                .GroupBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (g.First().Id, g.First().IsActive), StringComparer.OrdinalIgnoreCase);

            _targetWarehouses = Required(await warehouses.SearchAsync(null, includeInactive: true, ct), "คลังปลายทาง")
                .GroupBy(w => w.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (g.First().Id, g.First().IsActive), StringComparer.OrdinalIgnoreCase);

            _targetCatalog = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            for (var page = 1; ; page++)
            {
                var result = Required(await catalog.SearchAsync(
                    new CatalogManagementQuery(IncludeInactive: true, Page: page, PageSize: 100), ct), "สินค้าปลายทาง");
                foreach (var item in result.Items) _targetCatalog.TryAdd(item.Code, item.Id);
                if (page >= result.TotalPages || result.Items.Count == 0) break;
            }

            _targetTemplates = Required(await templates.SearchAsync(null, includeInactive: true, ct), "เทมเพลตปลายทาง")
                .GroupBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

            _refs = Required(await staff.GetReferenceDataAsync(ct), "ข้อมูลอ้างอิงพนักงานปลายทาง");
            _sectors = Required(await staff.GetSectorsAsync(null, ct), "หน่วยงานปลายทาง");
        }

        private static T Required<T>(Result<T> result, string what) => result.Success
            ? result.Data!
            : throw new InvalidOperationException($"อ่าน{what}ไม่สำเร็จ: {result.Error?.Code} {result.Error?.MessageTh}");

        private static IEnumerable<CatalogCategoryDto> Flatten(IEnumerable<CatalogCategoryDto> nodes) =>
            nodes.SelectMany(n => new[] { n }.Concat(Flatten(n.Children ?? [])));

        // ───────────────────────────────────────────── หมวดหมู่

        private async Task CategoriesAsync(ICatalogCategoryService service, CancellationToken ct)
        {
            const string type = ConversionEntityTypes.CatalogCategory;
            var report = _reports[type];
            var rows = WithoutSamples(_data.Categories, type, c => Id(c.Id), report);
            var byId = rows.ToDictionary(c => c.Id);

            foreach (var category in ParentsFirst(rows, byId))
            {
                await RowAsync(report, category.Code, async () =>
                {
                    var sourceId = Id(category.Id);
                    if (AlreadyConverted(type, sourceId, report)) return;

                    if (_targetCategories.TryGetValue(category.Code, out var found))
                    {
                        if (!found.IsActive)
                            report.Messages.Add($"{category.Code}: ปลายทางมีรหัสนี้แต่ปิดใช้งาน — จับคู่ไว้ (สินค้าในหมวดนี้จะสร้างโดยไม่มีหมวด)");
                        await RecordAsync(type, sourceId, Id(found.Id), report, created: false, ct);
                        return;
                    }

                    Guid? parent = null;
                    if (category.ParentCategoryId is { } parentId)
                    {
                        if (!byId.ContainsKey(parentId))
                        {
                            report.Messages.Add($"{category.Code}: หมวดแม่ไม่ได้ย้าย (ปิดใช้งาน/ไม่พบ) — สร้างเป็นหมวดระดับบน");
                        }
                        else if (!_map.TryGetValue(Key(type, Id(parentId)), out var parentTarget))
                        {
                            Failed(report, $"{category.Code}: หมวดแม่ย้ายไม่สำเร็จ");
                            return;
                        }
                        else
                        {
                            parent = Guid.TryParse(parentTarget, out var g) ? g : null;
                        }
                    }

                    var request = new CatalogCategoryUpsertRequest(category.Code, category.Name, parent, category.SortOrder);
                    if (DryRun)
                    {
                        var error = MasterDataSupport.Required(MasterDataSupport.Code(request.Code), 30, "CATEGORY", "รหัสหมวดหมู่", "code")
                                    ?? MasterDataSupport.Required(request.Name.Trim(), 200, "CATEGORY", "ชื่อหมวดหมู่", "name");
                        if (error is not null) Failed(report, Describe(category.Code, error));
                        else await RecordAsync(type, sourceId, DryRunTargetId, report, created: true, ct);
                        return;
                    }

                    var result = await service.CreateAsync(request, ct);
                    if (result.Success)
                    {
                        _targetCategories[category.Code] = (result.Data!.Id, true);
                        await RecordAsync(type, sourceId, Id(result.Data.Id), report, created: true, ct);
                    }
                    else if (result.Error!.Code == "CATEGORY_CODE_DUPLICATE"
                             && Flatten(Required(await service.SearchAsync(category.Code, true, ct), "หมวดหมู่ปลายทาง"))
                                 .FirstOrDefault(c => SameCode(c.Code, category.Code)) is { } dup)
                    {
                        await RecordAsync(type, sourceId, Id(dup.Id), report, created: false, ct);
                    }
                    else
                    {
                        Failed(report, Describe(category.Code, result.Error));
                    }
                }, ct);
            }
        }

        /// <summary>แม่ก่อนลูก — แม่ที่ไม่อยู่ในชุด (ปิดใช้งาน) ถือเป็นราก · กันวนซ้ำด้วย visited</summary>
        private static IEnumerable<SourceCategory> ParentsFirst(IReadOnlyList<SourceCategory> rows,
            IReadOnlyDictionary<Guid, SourceCategory> byId)
        {
            var ordered = new List<SourceCategory>();
            var visited = new HashSet<Guid>();

            void Visit(SourceCategory c)
            {
                if (!visited.Add(c.Id)) return;
                if (c.ParentCategoryId is { } p && byId.TryGetValue(p, out var parent)) Visit(parent);
                ordered.Add(c);
            }

            foreach (var row in rows.OrderBy(r => r.SortOrder ?? int.MaxValue).ThenBy(r => r.Code, StringComparer.Ordinal))
                Visit(row);
            return ordered;
        }

        // ───────────────────────────────────────────── คลัง

        private async Task WarehousesAsync(IWarehouseService service, CancellationToken ct)
        {
            const string type = ConversionEntityTypes.Warehouse;
            var report = _reports[type];
            foreach (var warehouse in WithoutSamples(_data.Warehouses, type, w => Id(w.Id), report)
                         .OrderBy(w => w.Code, StringComparer.Ordinal))
            {
                await RowAsync(report, warehouse.Code, async () =>
                {
                    var sourceId = Id(warehouse.Id);
                    if (AlreadyConverted(type, sourceId, report)) return;

                    if (_targetWarehouses.TryGetValue(warehouse.Code, out var found))
                    {
                        if (!found.IsActive)
                            report.Messages.Add($"{warehouse.Code}: ปลายทางมีคลังรหัสนี้แต่ปิดใช้งาน — สินค้าจะสร้างโดยไม่ผูกคลัง");
                        await RecordAsync(type, sourceId, Id(found.Id), report, created: false, ct);
                        return;
                    }

                    var request = new WarehouseUpsertRequest(warehouse.Code, warehouse.Name, warehouse.Address);
                    if (DryRun)
                    {
                        var error = MasterDataSupport.Required(MasterDataSupport.Code(request.Code), 30, "WAREHOUSE", "รหัสคลัง", "code")
                                    ?? MasterDataSupport.Required(request.Name.Trim(), 200, "WAREHOUSE", "ชื่อคลัง", "name");
                        if (error is not null) Failed(report, Describe(warehouse.Code, error));
                        else await RecordAsync(type, sourceId, DryRunTargetId, report, created: true, ct);
                        return;
                    }

                    var result = await service.CreateAsync(request, ct);
                    if (result.Success)
                    {
                        _targetWarehouses[warehouse.Code] = (result.Data!.Id, true);
                        await RecordAsync(type, sourceId, Id(result.Data.Id), report, created: true, ct);
                    }
                    else if (result.Error!.Code == "WAREHOUSE_CODE_DUPLICATE"
                             && Required(await service.SearchAsync(warehouse.Code, true, ct), "คลังปลายทาง")
                                 .FirstOrDefault(w => SameCode(w.Code, warehouse.Code)) is { } dup)
                    {
                        await RecordAsync(type, sourceId, Id(dup.Id), report, created: false, ct);
                    }
                    else
                    {
                        Failed(report, Describe(warehouse.Code, result.Error));
                    }
                }, ct);
            }
        }

        // ───────────────────────────────────────────── สินค้า/บริการ

        private async Task CatalogItemsAsync(ICatalogService service, CancellationToken ct)
        {
            const string type = ConversionEntityTypes.CatalogItem;
            var report = _reports[type];
            foreach (var item in WithoutSamples(_data.CatalogItems, type, i => Id(i.Id), report)
                         .OrderBy(i => i.Code, StringComparer.Ordinal))
            {
                await RowAsync(report, item.Code, async () =>
                {
                    var sourceId = Id(item.Id);
                    if (AlreadyConverted(type, sourceId, report)) return;

                    // [BIZ] รหัสซ้ำ = สินค้าเดียวกัน → ใช้ของเดิมที่ปลายทาง (ไม่แก้ราคา/ชื่อของสาขาจริง)
                    if (_targetCatalog.TryGetValue(item.Code, out var existingId))
                    {
                        await RecordAsync(type, sourceId, Id(existingId), report, created: false, ct);
                        return;
                    }

                    var category = MapReference(ConversionEntityTypes.CatalogCategory, item.CategoryId, item.Code, "หมวดหมู่", report);
                    var warehouse = MapReference(ConversionEntityTypes.Warehouse, item.WarehouseId, item.Code, "คลัง", report);
                    if (item.StockManaged)
                        report.Messages.Add($"{item.Code}: ใช้ระบบสต็อก FIFO — คัดลอกยอดคงเหลือ {item.OnHand} เป็นตัวเลข (ไม่ได้ย้ายล็อต) " +
                                            "ตั้งยอดยกมาที่สาขาปลายทางถ้าใช้ระบบสต็อก");

                    // Reserved/OnOrder ผูกกับจ๊อบ/ใบสั่งซื้อที่ไม่ได้ย้าย จึงเริ่มที่ 0
                    var request = new CatalogUpsertRequest(item.Code, item.Type, item.Name, item.Compatibility, item.Unit,
                        item.Cost, item.Price, item.StandardHours, item.OnHand, 0, 0, item.Damaged, item.EtaNote,
                        category, warehouse);

                    if (DryRun)
                    {
                        if (CatalogValidator.Validate(request) is { } error) Failed(report, Describe(item.Code, error));
                        else
                        {
                            await RecordAsync(type, sourceId, DryRunTargetId, report, created: true, ct);
                            _targetCatalog.TryAdd(item.Code, Guid.Empty);
                        }

                        return;
                    }

                    var result = await service.CreateAsync(request, ct);

                    // หมวด/คลังปลายทางใช้ไม่ได้ (ปิดใช้งาน · หมวดมีหมวดย่อย) → สร้างโดยไม่ผูก ดีกว่าไม่ได้สินค้าเลย
                    if (!result.Success && request.CategoryId is not null
                        && result.Error!.Code is "CATEGORY_NOT_FOUND" or "CATEGORY_NOT_LEAF")
                    {
                        report.Messages.Add($"{item.Code}: หมวดหมู่ปลายทางใช้ไม่ได้ ({result.Error.Code}) — สร้างโดยไม่มีหมวด");
                        request = request with { CategoryId = null };
                        result = await service.CreateAsync(request, ct);
                    }

                    if (!result.Success && request.WarehouseId is not null && result.Error!.Code == "WAREHOUSE_NOT_FOUND")
                    {
                        report.Messages.Add($"{item.Code}: คลังปลายทางใช้ไม่ได้ — สร้างโดยไม่ผูกคลัง");
                        request = request with { WarehouseId = null };
                        result = await service.CreateAsync(request, ct);
                    }

                    if (result.Success)
                    {
                        _targetCatalog[item.Code] = result.Data!.Id;
                        await RecordAsync(type, sourceId, Id(result.Data.Id), report, created: true, ct);
                    }
                    else if (result.Error!.Code == "CATALOG_CODE_DUPLICATE"
                             && Required(await service.SearchAsync(new CatalogManagementQuery(item.Code, IncludeInactive: true,
                                     PageSize: 100), ct), "สินค้าปลายทาง").Items
                                 .FirstOrDefault(i => SameCode(i.Code, item.Code)) is { } dup)
                    {
                        _targetCatalog[item.Code] = dup.Id;
                        await RecordAsync(type, sourceId, Id(dup.Id), report, created: false, ct);
                    }
                    else
                    {
                        Failed(report, Describe(item.Code, result.Error));
                    }
                }, ct);
            }
        }

        private Guid? MapReference(string type, Guid? sourceId, string rowKey, string label, ConversionEntityReport report)
        {
            if (sourceId is not { } id) return null;
            if (_map.TryGetValue(Key(type, Id(id)), out var target))
                return Guid.TryParse(target, out var g) ? g : null;   // dry-run: ยังไม่มี id จริง

            report.Messages.Add($"{rowKey}: {label}ต้นทางไม่ได้ย้าย (ปิดใช้งาน/ล้ม) — สร้างโดยไม่ผูก{label}");
            return null;
        }

        // ───────────────────────────────────────────── เทมเพลตใบเสนอราคา

        private async Task TemplatesAsync(IQuotationTemplateService service, CancellationToken ct)
        {
            const string type = ConversionEntityTypes.QuotationTemplate;
            var report = _reports[type];
            foreach (var template in WithoutSamples(_data.Templates, type, t => Id(t.Id), report)
                         .OrderBy(t => t.Code, StringComparer.Ordinal))
            {
                await RowAsync(report, template.Code, async () =>
                {
                    var sourceId = Id(template.Id);
                    if (AlreadyConverted(type, sourceId, report)) return;

                    if (_targetTemplates.TryGetValue(template.Code, out var existingId))
                    {
                        await RecordAsync(type, sourceId, Id(existingId), report, created: false, ct);
                        return;
                    }

                    // บรรทัดแคตตาล็อกอ้างด้วย "รหัส" (สดจากแคตตาล็อกตอนนำไปใช้ — docs/08) — รหัสที่ปลายทางไม่มี (สินค้าตัวอย่าง/ปิดใช้งาน)
                    // จะทำให้ทั้งเทมเพลตถูกปฏิเสธ (QUOTE_TEMPLATE_ITEM_MISSING) จึงตัดบรรทัดนั้นออกพร้อมรายงาน
                    var lines = template.Lines
                        .Where(l => string.IsNullOrWhiteSpace(l.CatalogCode) || _targetCatalog.ContainsKey(l.CatalogCode.Trim()))
                        .ToList();
                    var dropped = template.Lines
                        .Where(l => !string.IsNullOrWhiteSpace(l.CatalogCode) && !_targetCatalog.ContainsKey(l.CatalogCode.Trim()))
                        .Select(l => l.CatalogCode.Trim())
                        .ToList();
                    if (dropped.Count > 0)
                        report.Messages.Add($"{template.Code}: ตัดบรรทัดที่อ้างรหัสซึ่งไม่ได้ย้าย ({string.Join(", ", dropped)})");
                    if (lines.Count == 0)
                    {
                        report.Skipped++;
                        report.Messages.Add($"{template.Code}: ไม่มีบรรทัดเหลือหลังตัดรหัสที่ไม่ได้ย้าย — ไม่สร้าง");
                        return;
                    }

                    var request = new QuotationTemplateUpsertRequest(template.Code, template.Name, template.Description, lines);
                    if (DryRun)
                    {
                        await RecordAsync(type, sourceId, DryRunTargetId, report, created: true, ct);
                        return;
                    }

                    var result = await service.CreateAsync(request, ct);
                    if (result.Success)
                    {
                        _targetTemplates[template.Code] = result.Data!.Id;
                        await RecordAsync(type, sourceId, Id(result.Data.Id), report, created: true, ct);
                    }
                    else if (result.Error!.Code == "QUOTE_TEMPLATE_CODE_DUPLICATE"
                             && Required(await service.SearchAsync(template.Code, true, ct), "เทมเพลตปลายทาง")
                                 .FirstOrDefault(t => SameCode(t.Code, template.Code)) is { } dup)
                    {
                        await RecordAsync(type, sourceId, Id(dup.Id), report, created: false, ct);
                    }
                    else
                    {
                        Failed(report, Describe(template.Code, result.Error));
                    }
                }, ct);
            }
        }

        // ───────────────────────────────────────────── พนักงาน

        private async Task StaffAsync(IStaffService service, CancellationToken ct)
        {
            const string type = ConversionEntityTypes.Staff;
            var report = _reports[type];
            foreach (var staff in WithoutSamples(_data.Staff, type, s => Id(s.Detail.Id), report)
                         .OrderBy(s => s.Detail.Id))
            {
                var detail = staff.Detail;
                var fullName = FullName(detail.FirstName, detail.LastName);
                var rowKey = $"{fullName} (Staff {Id(detail.Id)})";
                await RowAsync(report, rowKey, async () =>
                {
                    var sourceId = Id(detail.Id);
                    if (AlreadyConverted(type, sourceId, report)) return;

                    // เจ้าของอู่มีบัญชีที่สาขาปลายทางแล้ว (ทีมงานสร้างใน GaragePro Admin = ผู้ใช้ปลายทาง) — ไม่สร้างคนซ้ำ
                    if (staff.IsOwner)
                    {
                        report.Messages.Add($"{rowKey}: เจ้าของอู่ — ใช้บัญชีปลายทาง (Staff {_targetStaffId}) แทนการสร้างใหม่");
                        await RecordAsync(type, sourceId, Id(_targetStaffId), report, created: false, ct);
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(detail.PictureUrl)) Images.Add($"รูปพนักงาน {rowKey}");

                    var existing = Required(await service.SearchAsync(
                            new StaffSearchQuery(detail.FirstName.Trim(), IncludeInactive: true, PageSize: 100), ct), "พนักงานปลายทาง")
                        .Items.FirstOrDefault(s => SameName(s.FullName, fullName));
                    if (existing is not null)
                    {
                        report.Messages.Add($"{rowKey}: มีพนักงานชื่อนี้ที่สาขาปลายทางแล้ว (Staff {existing.Id}) — จับคู่แทนการสร้างซ้ำ");
                        await RecordAsync(type, sourceId, Id(existing.Id), report, created: false, ct);
                        return;
                    }

                    var (request, problem) = BuildStaffRequest(staff, rowKey, report);
                    if (request is null)
                    {
                        Failed(report, $"{rowKey}: {problem}");
                        return;
                    }

                    if (DryRun)
                    {
                        if (StaffValidator.Validate(request, isCreate: true) is { } error) Failed(report, Describe(rowKey, error));
                        else await RecordAsync(type, sourceId, DryRunTargetId, report, created: true, ct);
                        return;
                    }

                    var result = await service.CreateAsync(request, null, ct);
                    if (!result.Success)
                    {
                        Failed(report, Describe(rowKey, result.Error!));
                        return;
                    }

                    await RecordAsync(type, sourceId, Id(result.Data!.Id), report, created: true, ct);
                    StaffAccounts.Add(new ConversionStaffAccount(detail.Id, result.Data.Id, fullName, result.Data.Account.UserName));
                }, ct);
            }
        }

        private (StaffUpsertRequest? Request, string? Problem) BuildStaffRequest(SourceStaff staff, string rowKey,
            ConversionEntityReport report)
        {
            var detail = staff.Detail;
            var main = detail.SectorPositions.FirstOrDefault(p => p.IsMain) ?? detail.SectorPositions.FirstOrDefault();

            var candidates = new List<(string Position, string Sector, string? Department)>();
            if (staff.Role is { } role && RoleNames.TryGetValue(role, out var byRole)) candidates.Add(byRole);
            if (main is not null) candidates.Add((main.PositionName, main.SectorName, main.DepartmentName));

            int? positionId = null, sectorId = null;
            foreach (var (position, sector, department) in candidates)
            {
                var p = FindByName(_refs.Positions, position);
                var s = FindSector(sector, department);
                if (p is null || s is null) continue;
                positionId = p.Id;
                sectorId = s.Id;
                break;
            }

            if (positionId is null || sectorId is null)
                return (null, "ไม่พบตำแหน่ง/หน่วยงานที่ตรงกันในสาขาปลายทาง (" +
                              string.Join(" · ", candidates.Select(c => $"{c.Position}/{c.Sector}")) + ")");

            if (detail.SectorPositions.Count(p => !p.IsMain) > 0)
                report.Messages.Add($"{rowKey}: ไม่ได้ย้ายหน่วยงานเพิ่มเติม — เพิ่มเองใน GaragePro Admin ถ้าต้องการ");

            // เพศ: sp1 มี "ไม่ระบุ" (Id 3) ที่ Garage ไม่มี — จับคู่ด้วยชื่อก่อน แล้ว id เดิม (ตารางคัดลอก id มา) ไม่งั้นค่าแรกของปลายทาง
            var gender = FindByName(_refs.Genders, detail.GenderName)
                         ?? _refs.Genders.FirstOrDefault(g => g.Id == detail.GenderId);
            if (gender is null)
            {
                gender = _refs.Genders.OrderBy(g => g.Id).FirstOrDefault();
                if (gender is null) return (null, "สาขาปลายทางไม่มีตารางเพศให้เลือก");
                report.Messages.Add($"{rowKey}: เพศ '{detail.GenderName ?? "-"}' ไม่มีที่ปลายทาง — ตั้งเป็น '{gender.Name}' ชั่วคราว " +
                                    "แก้ใน GaragePro Admin");
            }

            var skill = FindByName(_refs.SkillLevels, detail.StaffSkillLevelName)
                        ?? _refs.SkillLevels.FirstOrDefault(l => l.Id == detail.StaffSkillLevelId);
            if (skill is null && detail.StaffSkillLevelId is not null)
                report.Messages.Add($"{rowKey}: ไม่พบระดับฝีมือ '{detail.StaffSkillLevelName}' ที่ปลายทาง — เว้นว่าง");

            // BranchId = สาขาปลายทาง · UserName/Password ว่าง = ทางสร้างเดิมของ legacy (ระบบออกรหัสพนักงานและตั้งชื่อผู้ใช้ให้)
            return (new StaffUpsertRequest(
                _request.TargetBranchId, detail.FirstName, detail.LastName, gender.Id, detail.IdCard,
                detail.Address1, detail.Address2, detail.ProvinceId, detail.AmphureId, detail.DistrictId, detail.ZipCode,
                detail.PhoneNumber1 ?? string.Empty, detail.PhoneNumber2, detail.Email, detail.LineId,
                detail.Salary ?? 0m, skill?.Id, detail.ExperienceYear ?? 0, detail.ExperienceMonth ?? 0,
                detail.StartJobDate, null, detail.Note, sectorId.Value, positionId.Value, null, null, null), null);
        }

        private LookupItemDto? FindSector(string? name, string? department)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var byName = _sectors.Where(s => SameText(s.Name, name)).ToList();
            return byName.FirstOrDefault(s => SameText(s.Secondary, department))
                   ?? (byName.Count == 1 ? byName[0] : null);
        }

        private static LookupItemDto? FindByName(IEnumerable<LookupItemDto> items, string? name) =>
            string.IsNullOrWhiteSpace(name) ? null : items.FirstOrDefault(i => SameText(i.Name, name));

        // ───────────────────────────────────────────── ลูกค้า

        private async Task CustomersAsync(ICustomerVehicleService service, CancellationToken ct)
        {
            const string type = ConversionEntityTypes.Customer;
            var report = _reports[type];
            var genderIds = _refs.Genders.Select(g => g.Id).ToHashSet();

            foreach (var customer in WithoutSamples(_data.Customers, type, c => Id(c.Id), report).OrderBy(c => c.Id))
            {
                var rowKey = $"{FullName(customer.FirstName, customer.LastName)} (Customer {Id(customer.Id)})";
                await RowAsync(report, rowKey, async () =>
                {
                    var sourceId = Id(customer.Id);
                    if (AlreadyConverted(type, sourceId, report)) return;

                    // เพศไม่บังคับสำหรับลูกค้า — id ที่ปลายทางไม่มี (เช่น 3 "ไม่ระบุ" ของ sp1) เว้นว่าง ไม่งั้นชน FK
                    var request = new CustomerUpsertRequest(
                        customer.FirstName, customer.LastName, customer.PhoneNumber1 ?? string.Empty, customer.PhoneNumber2,
                        customer.IdCard, customer.DriverLicense,
                        customer.GenderId is { } g && genderIds.Contains(g) ? g : null,
                        customer.DateOfBirth, customer.Address1, customer.Address2, customer.ProvinceId, customer.AmphureId,
                        customer.DistrictId, customer.ZipCode, customer.Email, customer.LineId, customer.IsBlacklist,
                        customer.BlacklistRemark);

                    if (DryRun)
                    {
                        if (CustomerVehicleValidator.ValidateCustomer(request) is { } error)
                        {
                            Failed(report, Describe(rowKey, error));
                            return;
                        }

                        // คาดการณ์กติกาซ้ำเดียวกับ CreateCustomerAsync (ชื่อ + นามสกุล + เบอร์หลัก) ด้วยการค้นหาที่มีอยู่
                        var phone = request.PhoneNumber1.Trim();
                        var duplicate = Required(await service.SearchCustomersAsync(
                                new CustomerSearchQuery(phone, IncludeDeleted: true, PageSize: 100), ct), "ลูกค้าปลายทาง")
                            .Items.FirstOrDefault(c => SameText(c.FirstName, request.FirstName)
                                                       && SameText(c.LastName, request.LastName)
                                                       && SameText(c.PhoneNumber1, phone));
                        await RecordAsync(type, sourceId, duplicate is null ? DryRunTargetId : Id(duplicate.Id), report,
                            created: duplicate is null, ct);
                        return;
                    }

                    var result = await service.CreateCustomerAsync(request, ct);
                    if (result.Success)
                    {
                        await RecordAsync(type, sourceId, Id(result.Data!.Id), report, created: true, ct);
                    }
                    else if (result.Error!.Code == "CUSTOMER_DUPLICATE" && result.Error.Details is CustomerDuplicateDto dup)
                    {
                        if (dup.IsDeleted)
                            report.Messages.Add($"{rowKey}: ปลายทางมีลูกค้าคนเดียวกันแต่ถูกลบอยู่ (Customer {dup.Id}) — จับคู่ไว้ กู้คืนเองถ้าต้องการ");
                        await RecordAsync(type, sourceId, Id(dup.Id), report, created: false, ct);
                    }
                    else
                    {
                        Failed(report, Describe(rowKey, result.Error));
                    }
                }, ct);
            }
        }

        // ───────────────────────────────────────────── รถ

        private async Task VehiclesAsync(ICustomerVehicleService service, CancellationToken ct)
        {
            const string type = ConversionEntityTypes.Car;
            var report = _reports[type];
            foreach (var vehicle in WithoutSamples(_data.Vehicles, type, v => Id(v.Id), report).OrderBy(v => v.Id))
            {
                var rowKey = $"{vehicle.Registration} (Car {Id(vehicle.Id)})";
                await RowAsync(report, rowKey, async () =>
                {
                    var sourceId = Id(vehicle.Id);
                    if (AlreadyConverted(type, sourceId, report)) return;

                    var owner = vehicle.Owners.FirstOrDefault(o => _map.ContainsKey(Key(ConversionEntityTypes.Customer, Id(o.Id))));
                    if (owner is null)
                    {
                        if (vehicle.Owners.Count > 0
                            && vehicle.Owners.All(o => _samples.Contains(Key(ConversionEntityTypes.Customer, Id(o.Id)))))
                        {
                            report.Skipped++;
                            report.Messages.Add($"{rowKey}: เจ้าของเป็นลูกค้าตัวอย่าง — ไม่ย้าย");
                        }
                        else
                        {
                            Failed(report, $"{rowKey}: เจ้าของรถยังไม่ถูกย้าย (ลูกค้าล้ม/ถูกลบ/ไม่มีเจ้าของ)");
                        }

                        return;
                    }

                    if (vehicle.Owners.Count > 1)
                        report.Messages.Add($"{rowKey}: มีเจ้าของ {vehicle.Owners.Count} คน — ผูกกับ {owner.FullName} คนเดียว");

                    if (vehicle.ProvinceId is not { } province || vehicle.BrandId is not { } brand
                        || vehicle.ModelId is not { } model || vehicle.NicknameId is not { } nickname
                        || vehicle.YearId is not { } year)
                    {
                        Failed(report, $"{rowKey}: ข้อมูลรถไม่ครบ (จังหวัด/ยี่ห้อ/รุ่น/โฉม/ปี) — แก้ที่ต้นทางแล้วรันซ้ำ");
                        return;
                    }

                    if (!string.IsNullOrWhiteSpace(vehicle.ImageUrl)) Images.Add($"รูปรถ {rowKey}");

                    var ownerTarget = _map[Key(ConversionEntityTypes.Customer, Id(owner.Id))];
                    var hasRealOwner = long.TryParse(ownerTarget, NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId);

                    // ลูกค้าที่จับคู่กับของเดิม อาจมีรถคันนี้อยู่แล้ว — ทะเบียนเดียวกัน (ตัดช่องว่าง/ขีด) = คันเดียวกัน
                    if (hasRealOwner)
                    {
                        var ownerDetail = await service.GetCustomerAsync(ownerId, ct);
                        var same = ownerDetail.Success
                            ? ownerDetail.Data!.Vehicles.FirstOrDefault(v => !v.IsDeleted
                                                                             && Plate(v.Registration) == Plate(vehicle.Registration))
                            : null;
                        if (same is not null)
                        {
                            report.Messages.Add($"{rowKey}: เจ้าของที่ปลายทางมีรถทะเบียนนี้แล้ว (Car {same.Id}) — จับคู่แทนการสร้างซ้ำ");
                            await RecordAsync(type, sourceId, Id(same.Id), report, created: false, ct);
                            return;
                        }
                    }

                    // ยี่ห้อ/รุ่น/โฉม/ปี/จังหวัด/สี ฯลฯ ของ sp1 เป็น synonym ไปตาราง Garage — id ใช้ร่วมกันได้ตรงๆ
                    var request = new VehicleUpsertRequest(hasRealOwner ? ownerId : 1, vehicle.Registration, province, brand,
                        model, nickname, year, vehicle.PrimaryColorId, vehicle.ColorMixId, vehicle.GearId, vehicle.MachineId,
                        vehicle.DriveSystemId, vehicle.Vin, vehicle.EngineNumber, vehicle.InsuranceId,
                        vehicle.InsuranceExpiredDate);

                    if (DryRun)
                    {
                        if (CustomerVehicleValidator.ValidateVehicle(request) is { } error) Failed(report, Describe(rowKey, error));
                        else await RecordAsync(type, sourceId, DryRunTargetId, report, created: true, ct);
                        return;
                    }

                    var result = await service.CreateVehicleAsync(request, null, ct);
                    if (result.Success) await RecordAsync(type, sourceId, Id(result.Data!.Id), report, created: true, ct);
                    else Failed(report, Describe(rowKey, result.Error!));
                }, ct);
            }
        }

        // ───────────────────────────────────────────── ตัวช่วย

        private List<T> WithoutSamples<T>(IEnumerable<T> rows, string type, Func<T, string> id, ConversionEntityReport report)
        {
            var kept = new List<T>();
            foreach (var row in rows)
            {
                if (_samples.Contains(Key(type, id(row)))) report.SampleExcluded++;
                else kept.Add(row);
            }

            report.Source = kept.Count;
            return kept;
        }

        private bool AlreadyConverted(string type, string sourceId, ConversionEntityReport report)
        {
            if (!_previous.Contains(Key(type, sourceId))) return false;
            report.AlreadyConverted++;
            return true;
        }

        private async Task RecordAsync(string type, string sourceId, string targetId, ConversionEntityReport report,
            bool created, CancellationToken ct)
        {
            // บันทึก ledger ทันทีหลังสำเร็จ (ไม่รอจบทั้งรอบ) — process ตายกลางทาง รันใหม่ต้องไม่สร้างแถวนี้ซ้ำ
            if (!DryRun)
                await _store.AddMapAsync(_workspace.Id,
                    new ConversionMapEntry(type, sourceId, targetId, _request.TargetShardKey, _request.TargetBranchId), _now(), ct);

            _map[Key(type, sourceId)] = targetId;
            if (created) report.Created++;
            else report.MappedExisting++;
        }

        /// <summary>แถวหนึ่งล้ม (exception) ไม่หยุดแถวอื่น — นับเป็น Failed พร้อมสาเหตุ · ยกเลิก (Ctrl+C) หยุดทันที</summary>
        private static async Task RowAsync(ConversionEntityReport report, string rowKey, Func<Task> work, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await work();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Failed(report, $"{rowKey}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static void Failed(ConversionEntityReport report, string message)
        {
            report.Failed++;
            report.Messages.Add(message);
        }

        private static string Describe(string rowKey, ApiError error) =>
            $"{rowKey}: {error.Code} {error.MessageTh}";

        private static string Key(string type, string id) => $"{type}:{id.Trim()}";
        private static string Id(Guid id) => id.ToString("D");
        private static string Id(long id) => id.ToString(CultureInfo.InvariantCulture);

        private static bool SameCode(string? a, string? b) =>
            string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static bool SameText(string? a, string? b) =>
            a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.Ordinal);

        private static string FullName(string? first, string? last) =>
            string.Join(' ', new[] { first?.Trim(), last?.Trim() }.Where(s => !string.IsNullOrEmpty(s)));

        private static bool SameName(string? a, string b) =>
            a is not null && string.Equals(string.Join(' ', a.Split(' ', StringSplitOptions.RemoveEmptyEntries)), b,
                StringComparison.Ordinal);

        private static string Plate(string? registration) =>
            new string((registration ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray())
                .ToUpperInvariant();
    }
}
