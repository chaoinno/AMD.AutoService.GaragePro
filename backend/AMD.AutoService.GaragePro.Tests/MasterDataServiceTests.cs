using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.CatalogCategories;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Suppliers;
using AMD.AutoService.GaragePro.Application.Warehouses;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class MasterDataServiceTests
{
    [Fact]
    public async Task Can_create_multilevel_categories()
    {
        var repository = new FakeMasterDataRepository();
        var service = new CatalogCategoryService(repository, new ManagerUser(), TimeProvider.System);
        var root = await service.CreateAsync(new("PART", "อะไหล่", null, 1));
        var child = await service.CreateAsync(new("ENGINE", "เครื่องยนต์", root.Data!.Id, 1));

        Assert.True(root.Success);
        Assert.True(child.Success);
        Assert.Equal(root.Data.Id, child.Data!.ParentCategoryId);
    }

    [Fact]
    public async Task Cannot_deactivate_category_with_active_children()
    {
        var repository = new FakeMasterDataRepository();
        var parent = new CatalogCategory { Code = "PART", Name = "อะไหล่", LegacyShardKey = "db2", LegacyBranchId = 105 };
        var child = new CatalogCategory
        { Code = "ENGINE", Name = "เครื่องยนต์", ParentCategoryId = parent.Id, LegacyShardKey = "db2", LegacyBranchId = 105 };
        repository.Categories.AddRange([parent, child]);
        var service = new CatalogCategoryService(repository, new ManagerUser(), TimeProvider.System);

        var result = await service.SetStatusAsync(parent.Id, new(false));

        Assert.False(result.Success);
        Assert.Equal("CATEGORY_HAS_CHILDREN", result.Error?.Code);
    }

    [Fact]
    public async Task Catalog_rejects_non_leaf_category()
    {
        var master = new FakeMasterDataRepository();
        var parent = new CatalogCategory { Code = "PART", Name = "อะไหล่", LegacyShardKey = "db2", LegacyBranchId = 105 };
        master.Categories.AddRange([parent, new CatalogCategory
        { Code = "ENGINE", Name = "เครื่องยนต์", ParentCategoryId = parent.Id, LegacyShardKey = "db2", LegacyBranchId = 105 }]);
        var catalog = new FakeCatalogRepository();
        var service = new CatalogService(catalog, new ManagerUser(), master);

        var result = await service.CreateAsync(ValidCatalog(parent.Id));

        Assert.False(result.Success);
        Assert.Equal("CATEGORY_NOT_LEAF", result.Error?.Code);
    }

    [Fact]
    public async Task Item_can_have_multiple_supplier_costs_but_only_one_preferred()
    {
        var master = new FakeMasterDataRepository();
        var supplier1 = new Supplier { Code = "S1", Name = "หนึ่ง", LegacyShardKey = "db2", LegacyBranchId = 105 };
        var supplier2 = new Supplier { Code = "S2", Name = "สอง", LegacyShardKey = "db2", LegacyBranchId = 105 };
        master.Suppliers.AddRange([supplier1, supplier2]);
        var catalog = new FakeCatalogRepository { Existing = new CatalogItem
        { Code = "P1", Name = "สินค้า", Unit = "ชิ้น", LegacyBranchId = 105, LegacyShardKey = "db2" } };
        var service = new SupplierService(master, catalog, new ManagerUser(), TimeProvider.System);

        var first = await service.UpsertForCatalogItemAsync(catalog.Existing.Id, supplier1.Id,
            new("A-1", 90m, 2, 1, true));
        var second = await service.UpsertForCatalogItemAsync(catalog.Existing.Id, supplier2.Id,
            new("B-1", 80m, 5, 3, false));
        var duplicatePreferred = await service.UpsertForCatalogItemAsync(catalog.Existing.Id, supplier2.Id,
            new("B-1", 80m, 5, 3, true));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(90m, first.Data!.SupplierCost);
        Assert.Equal(80m, second.Data!.SupplierCost);
        Assert.False(duplicatePreferred.Success);
        Assert.Equal("SUPPLIER_PREFERRED_DUPLICATE", duplicatePreferred.Error?.Code);
    }

    // ---------- แยกข้อมูลหลักต่อสาขา (ScopeMasterDataByBranch) ----------

    [Fact]
    public async Task Supplier_of_another_branch_behaves_as_not_found()
    {
        var master = new FakeMasterDataRepository();
        var other = new Supplier { Code = "S9", Name = "ของสาขาอื่น", LegacyShardKey = "db2", LegacyBranchId = 106 };
        master.Suppliers.Add(other);
        var catalog = new FakeCatalogRepository { Existing = new CatalogItem
        { Code = "P1", Name = "สินค้า", Unit = "ชิ้น", LegacyBranchId = 105, LegacyShardKey = "db2" } };
        var service = new SupplierService(master, catalog, new ManagerUser(), TimeProvider.System);

        var search = await service.SearchAsync(null, includeInactive: true);
        var get = await service.GetAsync(other.Id);
        var update = await service.UpdateAsync(other.Id, ValidSupplier("S9"));
        var status = await service.SetStatusAsync(other.Id, new(false));
        var link = await service.UpsertForCatalogItemAsync(catalog.Existing.Id, other.Id, new("X-1", 10m, 1, 1, true));

        Assert.True(search.Success);
        Assert.Empty(search.Data!.Items);
        Assert.Equal("SUPPLIER_NOT_FOUND", get.Error?.Code);
        Assert.Equal("SUPPLIER_NOT_FOUND", update.Error?.Code);
        Assert.Equal("SUPPLIER_NOT_FOUND", status.Error?.Code);
        Assert.Equal("SUPPLIER_NOT_FOUND", link.Error?.Code);
        Assert.Equal("ของสาขาอื่น", other.Name);
        Assert.True(other.IsActive);
        Assert.Empty(master.Links);
    }

    [Fact]
    public async Task Same_supplier_code_is_allowed_in_two_branches_but_not_twice_in_one()
    {
        var master = new FakeMasterDataRepository();
        var catalog = new FakeCatalogRepository();
        var branch105 = new SupplierService(master, catalog, new ManagerUser(branchId: 105), TimeProvider.System);
        var branch106 = new SupplierService(master, catalog, new ManagerUser(branchId: 106), TimeProvider.System);

        var first = await branch105.CreateAsync(ValidSupplier("SUP-001"));
        var second = await branch106.CreateAsync(ValidSupplier("sup-001"));
        var duplicate = await branch105.CreateAsync(ValidSupplier("SUP-001"));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal("SUPPLIER_CODE_DUPLICATE", duplicate.Error?.Code);
        Assert.Equal([105, 106], master.Suppliers.Select(x => x.LegacyBranchId).Order());
        Assert.All(master.Suppliers, x => Assert.Equal("db2", x.LegacyShardKey));
    }

    [Fact]
    public async Task Category_of_another_branch_behaves_as_not_found()
    {
        var master = new FakeMasterDataRepository();
        var other = new CatalogCategory { Code = "PART", Name = "อะไหล่สาขาอื่น", LegacyShardKey = "db2", LegacyBranchId = 106 };
        master.Categories.Add(other);
        var service = new CatalogCategoryService(master, new ManagerUser(), TimeProvider.System);

        var search = await service.SearchAsync(null, includeInactive: true);
        var get = await service.GetAsync(other.Id);
        var update = await service.UpdateAsync(other.Id, new("PART", "เปลี่ยนชื่อ", null, 1));
        var status = await service.SetStatusAsync(other.Id, new(false));
        var delete = await service.DeleteAsync(other.Id);

        Assert.Empty(search.Data!);
        Assert.Equal("CATEGORY_NOT_FOUND", get.Error?.Code);
        Assert.Equal("CATEGORY_NOT_FOUND", update.Error?.Code);
        Assert.Equal("CATEGORY_NOT_FOUND", status.Error?.Code);
        Assert.Equal("CATEGORY_NOT_FOUND", delete.Error?.Code);
        Assert.Contains(other, master.Categories);
        Assert.Equal("อะไหล่สาขาอื่น", other.Name);
        Assert.True(other.IsActive);
    }

    [Fact]
    public async Task Category_parent_from_another_branch_is_rejected()
    {
        var master = new FakeMasterDataRepository();
        var foreignParent = new CatalogCategory { Code = "ROOT", Name = "หมวดสาขาอื่น", LegacyShardKey = "db2", LegacyBranchId = 106 };
        var own = new CatalogCategory { Code = "OWN", Name = "หมวดของเรา", LegacyShardKey = "db2", LegacyBranchId = 105 };
        master.Categories.AddRange([foreignParent, own]);
        var service = new CatalogCategoryService(master, new ManagerUser(), TimeProvider.System);

        var create = await service.CreateAsync(new("CHILD", "หมวดย่อย", foreignParent.Id, 1));
        var update = await service.UpdateAsync(own.Id, new("OWN", "หมวดของเรา", foreignParent.Id, 1));

        Assert.Equal("CATEGORY_PARENT_NOT_FOUND", create.Error?.Code);
        Assert.Equal("CATEGORY_PARENT_NOT_FOUND", update.Error?.Code);
        Assert.Null(own.ParentCategoryId);
        Assert.Equal(2, master.Categories.Count);
    }

    [Fact]
    public async Task Same_category_code_is_allowed_in_two_branches_and_carries_the_creator_scope()
    {
        var master = new FakeMasterDataRepository();
        var branch105 = new CatalogCategoryService(master, new ManagerUser(branchId: 105), TimeProvider.System);
        var branch106 = new CatalogCategoryService(master, new ManagerUser(branchId: 106), TimeProvider.System);

        var first = await branch105.CreateAsync(new("PART", "อะไหล่", null, 1));
        var second = await branch106.CreateAsync(new("PART", "อะไหล่", null, 1));
        var duplicate = await branch106.CreateAsync(new("part", "ซ้ำ", null, 2));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal("CATEGORY_CODE_DUPLICATE", duplicate.Error?.Code);
        Assert.Equal(105, master.Categories.Single(x => x.Id == first.Data!.Id).LegacyBranchId);
        Assert.Equal(106, master.Categories.Single(x => x.Id == second.Data!.Id).LegacyBranchId);
    }

    [Fact]
    public async Task Catalog_rejects_category_of_another_branch()
    {
        var master = new FakeMasterDataRepository();
        var foreign = new CatalogCategory { Code = "PART", Name = "อะไหล่สาขาอื่น", LegacyShardKey = "db2", LegacyBranchId = 106 };
        master.Categories.Add(foreign);
        var service = new CatalogService(new FakeCatalogRepository(), new ManagerUser(), master);

        var result = await service.CreateAsync(ValidCatalog(foreign.Id));

        Assert.False(result.Success);
        Assert.Equal("CATEGORY_NOT_FOUND", result.Error?.Code);
    }

    [Fact]
    public async Task Same_warehouse_code_is_allowed_in_two_branches_but_not_twice_in_one()
    {
        var master = new FakeMasterDataRepository();
        master.Warehouses.Add(new Warehouse { Code = "MAIN", Name = "คลังหลักสาขาอื่น", LegacyShardKey = "db2", LegacyBranchId = 106 });
        var service = new WarehouseService(master, new FakeLegacyReader(), new ManagerUser(), TimeProvider.System);

        var created = await service.CreateAsync(new("MAIN", "คลังหลัก", null));
        var duplicate = await service.CreateAsync(new("main", "คลังซ้ำ", null));

        Assert.True(created.Success);
        Assert.Equal("db2", created.Data!.LegacyShardKey);
        Assert.Equal(105, created.Data.LegacyBranchId);
        Assert.Equal("WAREHOUSE_CODE_DUPLICATE", duplicate.Error?.Code);
        Assert.Equal(2, master.Warehouses.Count);
    }

    private static SupplierUpsertRequest ValidSupplier(string code) =>
        new(code, "ซัพพลายเออร์", null, null, null, null, null, null, null);

    private static CatalogUpsertRequest ValidCatalog(Guid categoryId) => new(
        "P1", LineType.Part, "สินค้า", null, "ชิ้น", 10m, 20m, null,
        0, 0, 0, 0, null, categoryId);

    private sealed class ManagerUser(string shardKey = "db2", int branchId = 105) : ICurrentUser
    {
        public long UserId => 1;
        public string UserName => "ผู้จัดการ";
        public UserRole Role => UserRole.Manager;
        public string ShardKey => shardKey;
        public int BranchId => branchId;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => true;
        public Guid? SessionId => null;
    }

    private sealed class FakeLegacyReader : ILegacyReader
    {
        public Task<LegacyBranchDto?> GetBranchAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult<LegacyBranchDto?>(new LegacyBranchDto(branchId, "อู่ทดสอบ", null, null, null));
        public Task<IReadOnlyList<LegacyTechnicianDto>> GetTechniciansAsync(
            string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LegacyTechnicianDto>>([]);
    }

    private sealed class FakeCatalogRepository : ICatalogRepository
    {
        public CatalogItem? Existing { get; set; }
        public Task<CatalogItem?> GetAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Existing?.Id == id ? Existing : null);
        public Task AddAsync(CatalogItem item, CancellationToken ct = default) { Existing = item; return Task.CompletedTask; }
        public Task<bool> CodeExistsAsync(string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string shardKey, int branchId, string? keyword, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CatalogItem>>([]);
        public Task<IReadOnlyList<CatalogItem>> GetByCodesAsync(string shardKey, int branchId, IEnumerable<string> codes, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CatalogItem>>([]);
        public Task<(IReadOnlyList<CatalogItem> Items, int Total)> SearchManagementAsync(string shardKey, int branchId, CatalogManagementQuery query, CancellationToken ct = default) => Task.FromResult(((IReadOnlyList<CatalogItem>)[], 0));
    }

    /// <summary>
    /// กรองด้วย (ชาร์ด, สาขา) แบบเดียวกับ MasterDataRepository — เทสต์การมองไม่เห็นข้ามสาขาจึงวัดพฤติกรรมของ service จริง
    /// · ใช้ร่วมกับ BranchDefaultsSeederTests
    /// </summary>
    internal sealed class FakeMasterDataRepository : IMasterDataRepository
    {
        public List<Supplier> Suppliers { get; } = [];
        public List<Warehouse> Warehouses { get; } = [];
        public List<CatalogCategory> Categories { get; } = [];
        public List<CatalogItemSupplier> Links { get; } = [];
        public List<ActivityEvent> Events { get; } = [];
        public int SaveCount { get; private set; }

        private static bool In(string entityShard, int entityBranch, string shardKey, int branchId) =>
            entityShard == shardKey && entityBranch == branchId;

        public Task<IReadOnlyList<Supplier>> SearchSuppliersAsync(string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Supplier>>(Suppliers.Where(x => In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId) && (includeInactive || x.IsActive)).ToList());
        public Task<Supplier?> GetSupplierAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Suppliers.FirstOrDefault(x => x.Id == id && In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId)));
        public Task<bool> SupplierCodeExistsAsync(string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default) =>
            Task.FromResult(Suppliers.Any(x => In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId) && x.Code == code && x.Id != excludingId));
        public Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default) { Suppliers.Add(supplier); return Task.CompletedTask; }
        public Task<IReadOnlyList<Warehouse>> SearchWarehousesAsync(string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Warehouse>>(Warehouses.Where(x => In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId) && (includeInactive || x.IsActive)).ToList());
        public Task<Warehouse?> GetWarehouseAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Warehouses.FirstOrDefault(x => x.Id == id && In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId)));
        public Task<bool> WarehouseCodeExistsAsync(string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default) =>
            Task.FromResult(Warehouses.Any(x => In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId) && x.Code == code && x.Id != excludingId));
        public Task<bool> AnyWarehouseAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult(Warehouses.Any(x => In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId)));
        public Task AddWarehouseAsync(Warehouse warehouse, CancellationToken ct = default) { Warehouses.Add(warehouse); return Task.CompletedTask; }
        public Task<IReadOnlyList<CatalogCategory>> SearchCategoriesAsync(string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CatalogCategory>>(Categories.Where(x => In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId) && (includeInactive || x.IsActive)).ToList());
        public Task<CatalogCategory?> GetCategoryAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Categories.FirstOrDefault(x => x.Id == id && In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId)));
        public Task<bool> CategoryCodeExistsAsync(string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default) =>
            Task.FromResult(Categories.Any(x => In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId) && x.Code == code && x.Id != excludingId));
        public Task<bool> AnyCategoryAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult(Categories.Any(x => In(x.LegacyShardKey, x.LegacyBranchId, shardKey, branchId)));
        public Task<bool> CategoryHasChildrenAsync(Guid id, bool activeOnly, CancellationToken ct = default) => Task.FromResult(Categories.Any(x => x.ParentCategoryId == id && (!activeOnly || x.IsActive)));
        public Task<bool> CategoryWouldCycleAsync(Guid categoryId, Guid parentId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CategoryInUseAsync(Guid id, CancellationToken ct = default) => Task.FromResult(false);
        public Task AddCategoryAsync(CatalogCategory category, CancellationToken ct = default) { Categories.Add(category); return Task.CompletedTask; }
        public void RemoveCategory(CatalogCategory category) => Categories.Remove(category);
        public Task<IReadOnlyList<CatalogItemSupplier>> GetItemSuppliersAsync(Guid catalogItemId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CatalogItemSupplier>>(Links.Where(x => x.CatalogItemId == catalogItemId).ToList());
        public Task<CatalogItemSupplier?> GetItemSupplierAsync(Guid catalogItemId, Guid supplierId, CancellationToken ct = default) => Task.FromResult(Links.FirstOrDefault(x => x.CatalogItemId == catalogItemId && x.SupplierId == supplierId));
        public Task<bool> PreferredSupplierExistsAsync(Guid catalogItemId, Guid? excludingId, CancellationToken ct = default) => Task.FromResult(Links.Any(x => x.CatalogItemId == catalogItemId && x.IsPreferred && x.Id != excludingId));
        public Task AddItemSupplierAsync(CatalogItemSupplier itemSupplier, CancellationToken ct = default) { Links.Add(itemSupplier); return Task.CompletedTask; }
        public void RemoveItemSupplier(CatalogItemSupplier itemSupplier) => Links.Remove(itemSupplier);
        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default) { Events.Add(activityEvent); return Task.CompletedTask; }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) { SaveCount++; return Task.FromResult(1); }
    }
}
