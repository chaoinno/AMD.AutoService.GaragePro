using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Conversion;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// อ่านข้อมูลหลักของอู่จาก shard self-serve (S5) — อ่านอย่างเดียวทั้งหมด
/// · svc_* (หมวดหมู่/คลัง/สินค้า/เทมเพลต/override บทบาท) ผ่าน ServiceDbContext ของ scope sp1 (ScopedCurrentUser → GarageSelfServe)
/// · ลูกค้า/รถ/พนักงาน ผ่าน repository เดิม (ICustomerVehicleRepository · IStaffRepository) ด้วย LegacyRequestScope ของสาขานั้น —
///   ได้กติกาการมองเห็นเดียวกับหน้ารายการ (BranchDataScope) โดยไม่ต้องเขียน SQL ใหม่ · ช้าแบบ N+1 แต่รันครั้งเดียวต่ออู่
/// · sp_SampleEntity / sp_Workspace / sp_Account ผ่าน <see cref="SelfServeDbContext"/>
/// </summary>
public sealed class ConversionSourceReader(
    SelfServeDbContext db,
    IShardScopeFactory scopes,
    IOptions<SelfServeOptions> options) : IConversionSourceReader
{
    /// <summary>เพดานแถวที่ export ได้ของ repository เดิม (ExportCustomersAsync/ExportVehiclesAsync) — เกินนี้ต้องแบ่งรอบ</summary>
    public const int MaxRows = 10_000;

    private readonly SelfServeOptions _options = options.Value;

    public async Task<ConversionWorkspace?> FindWorkspaceAsync(int workspaceNo, CancellationToken ct = default) =>
        await (
                from w in db.Workspaces.AsNoTracking()
                where w.WorkspaceNo == workspaceNo
                join a in db.Accounts.AsNoTracking() on w.OwnerAccountId equals a.Id into owners
                from a in owners.DefaultIfEmpty()
                select new ConversionWorkspace(w.Id, w.WorkspaceNo, w.BranchId, w.Name, w.Plan, w.Status,
                    a == null ? null : a.UserId, w.ConvertedAt, w.ConvertedToShardKey, w.ConvertedToBranchId, w.PurgeAfter))
            .FirstOrDefaultAsync(ct);

    public async Task<ConversionSource> ReadAsync(ConversionWorkspace workspace, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.BranchId is not { } branchId)
            throw new InvalidOperationException($"workspace {workspace.WorkspaceNo} ยังไม่มีสาขา");

        var samples = (await db.SampleEntities.AsNoTracking()
                .Where(s => s.WorkspaceId == workspace.Id)
                .Select(s => new { s.EntityType, s.EntityId })
                .ToListAsync(ct))
            .Select(s => (s.EntityType, s.EntityId))
            .ToHashSet();

        var shard = _options.ShardKey;
        var ownerUserId = workspace.OwnerUserId ?? 0;

        // อ่านในนามเจ้าของอู่ของ shard sp1 — ServiceDbContext ของ scope นี้ชี้ GarageSelfServe ตั้งแต่ถูกสร้าง
        return await scopes.RunAsAsync(
            new ScopedCurrentUser(shard, branchId, ownerUserId, "spadmin", UserRole.Manager, EventSource.Web),
            async (sp, token) =>
            {
                var service = sp.GetRequiredService<ServiceDbContext>();

                var categories = await service.CatalogCategories.AsNoTracking()
                    .Where(c => c.LegacyShardKey == shard && c.LegacyBranchId == branchId && c.IsActive)
                    .Select(c => new SourceCategory(c.Id, c.Code, c.Name, c.ParentCategoryId, c.SortOrder))
                    .ToListAsync(token);

                var warehouses = await service.Warehouses.AsNoTracking()
                    .Where(w => w.LegacyShardKey == shard && w.LegacyBranchId == branchId && w.IsActive)
                    .Select(w => new SourceWarehouse(w.Id, w.Code, w.Name, w.Address))
                    .ToListAsync(token);

                var items = await service.CatalogItems.AsNoTracking()
                    .Where(c => c.LegacyShardKey == shard && c.LegacyBranchId == branchId && c.IsActive)
                    .Select(c => new SourceCatalogItem(c.Id, c.Code, c.Type, c.Name, c.Compatibility, c.Unit, c.Cost,
                        c.Price, c.StandardHours, c.OnHand, c.Damaged, c.EtaNote, c.CategoryId, c.WarehouseId,
                        c.StockManaged || c.PurchasingLocked))
                    .ToListAsync(token);

                var templates = (await service.QuotationTemplates.AsNoTracking()
                        .Include(t => t.Lines)
                        .Where(t => t.LegacyShardKey == shard && t.LegacyBranchId == branchId && t.IsActive)
                        .ToListAsync(token))
                    .Select(t => new SourceTemplate(t.Id, t.Code, t.Name, t.Description, t.Lines
                        .OrderBy(l => l.Sequence)
                        .Select(l => new QuotationTemplateLineInput(l.CatalogCode, l.Name, l.Type, l.Unit, l.Quantity,
                            l.UnitPrice, l.UnitCost, l.StandardHours, l.DiscountPercent, l.Promotion, l.Source, l.Note))
                        .ToList()))
                    .ToList();

                var roles = (await service.UserRoleOverrides.AsNoTracking()
                        .Where(o => o.LegacyShardKey == shard && o.LegacyBranchId == branchId)
                        .Select(o => new { o.LegacyUserId, o.Role })
                        .ToListAsync(token))
                    .GroupBy(o => o.LegacyUserId)
                    .ToDictionary(g => g.Key, g => g.First().Role);

                var scope = new LegacyRequestScope(shard, branchId, ownerUserId, "spadmin");
                var staff = await ReadStaffAsync(sp.GetRequiredService<IStaffRepository>(), scope, branchId,
                    workspace.OwnerUserId, roles, token);

                var people = sp.GetRequiredService<ICustomerVehicleRepository>();
                var customers = new List<CustomerDetailDto>();
                foreach (var summary in await people.ExportCustomersAsync(scope, new CustomerSearchQuery(), MaxRows, token))
                {
                    if (await people.GetCustomerAsync(scope, summary.Id, token) is { IsDeleted: false } detail)
                        customers.Add(detail);
                }

                var vehicles = new List<VehicleDetailDto>();
                foreach (var summary in await people.ExportVehiclesAsync(scope, new VehicleSearchQuery(), MaxRows, token))
                {
                    if (await people.GetVehicleAsync(scope, summary.Id, token) is { IsDeleted: false } detail)
                        vehicles.Add(detail);
                }

                return new ConversionSource(categories, warehouses, items, templates, staff, customers, vehicles, samples);
            }, ct);
    }

    private static async Task<List<SourceStaff>> ReadStaffAsync(IStaffRepository repository, LegacyRequestScope scope,
        int branchId, long? ownerUserId, IReadOnlyDictionary<long, UserRole> roles, CancellationToken ct)
    {
        var result = new List<SourceStaff>();
        for (var page = 1; ; page++)
        {
            var summaries = await repository.SearchAsync(scope,
                new StaffSearchQuery(IncludeInactive: false, Page: page, PageSize: 100), ct);
            foreach (var summary in summaries.Items)
            {
                var detail = await repository.GetAsync(scope, false, summary.Id, ct);
                if (detail is null || detail.BranchId != branchId || !detail.IsActive) continue;

                var userId = detail.Account.Id;
                result.Add(new SourceStaff(detail,
                    roles.TryGetValue(userId, out var role) ? role : null,
                    IsOwner: ownerUserId is { } owner && userId == owner));
            }

            if (page >= summaries.TotalPages || summaries.Items.Count == 0) break;
        }

        return result;
    }
}

/// <summary>sp_ConversionMap + การปิดการย้ายใน sp_Workspace + sp_WorkspaceAudit (S5) — ทุกเมธอดเป็น SaveChanges ครั้งเดียว</summary>
public sealed class ConversionStore(SelfServeDbContext db) : IConversionStore
{
    public async Task<IReadOnlyList<ConversionMapEntry>> GetMapAsync(Guid workspaceId, CancellationToken ct = default) =>
        await db.ConversionMaps.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId)
            .Select(m => new ConversionMapEntry(m.EntityType, m.SourceId, m.TargetId, m.TargetShardKey, m.TargetBranchId))
            .ToListAsync(ct);

    public async Task AddMapAsync(Guid workspaceId, ConversionMapEntry entry, DateTime now, CancellationToken ct = default)
    {
        db.ConversionMaps.Add(new ConversionMap
        {
            WorkspaceId = workspaceId,
            EntityType = entry.EntityType,
            SourceId = entry.SourceId,
            TargetId = entry.TargetId,
            TargetShardKey = entry.TargetShardKey,
            TargetBranchId = entry.TargetBranchId,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task MarkConvertedAsync(Guid workspaceId, string targetShardKey, int targetBranchId,
        DateTime convertedAt, DateTime purgeAfter, string actor, string payloadJson, CancellationToken ct = default)
    {
        var workspace = await db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
                        ?? throw new InvalidOperationException($"ไม่พบ workspace {workspaceId}");
        var planFrom = workspace.Plan;

        workspace.Plan = WorkspacePlanKind.Converted;
        workspace.ConvertedAt ??= convertedAt;
        workspace.ConvertedToShardKey = targetShardKey;
        workspace.ConvertedToBranchId = targetBranchId;
        // รันซ้ำต้องไม่ทำให้ลบเร็วขึ้น — ทีมงานอาจเลื่อนวันลบไว้แล้ว
        workspace.PurgeAfter = workspace.PurgeAfter is { } existing && existing > purgeAfter ? existing : purgeAfter;
        workspace.UpdatedAt = convertedAt;

        db.WorkspaceAudits.Add(new WorkspaceAudit
        {
            WorkspaceId = workspaceId,
            Action = ConversionService.AuditAction,
            Actor = Limit(actor, 200),
            Note = $"plan {planFrom} → Converted · ลบข้อมูลใน sp1 ได้หลัง {workspace.PurgeAfter:O}",
            PayloadJson = payloadJson,
            CreatedAt = convertedAt
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task AddAuditAsync(Guid workspaceId, string action, string actor, string? note, string? payloadJson,
        DateTime now, CancellationToken ct = default)
    {
        db.WorkspaceAudits.Add(new WorkspaceAudit
        {
            WorkspaceId = workspaceId,
            Action = action,
            Actor = Limit(actor, 200),
            Note = note is { Length: > 1000 } ? note[..1000] : note,
            PayloadJson = payloadJson,
            CreatedAt = now
        });
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static string Limit(string value, int max) => value.Length <= max ? value : value[..max];
}
