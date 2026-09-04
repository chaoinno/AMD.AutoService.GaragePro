using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.CatalogCategories;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Suppliers;
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
        var parent = new CatalogCategory { Code = "PART", Name = "อะไหล่" };
        var child = new CatalogCategory { Code = "ENGINE", Name = "เครื่องยนต์", ParentCategoryId = parent.Id };
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
        var parent = new CatalogCategory { Code = "PART", Name = "อะไหล่" };
        master.Categories.AddRange([parent, new CatalogCategory
        { Code = "ENGINE", Name = "เครื่องยนต์", ParentCategoryId = parent.Id }]);
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
        var supplier1 = new Supplier { Code = "S1", Name = "หนึ่ง" };
        var supplier2 = new Supplier { Code = "S2", Name = "สอง" };
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

    private static CatalogUpsertRequest ValidCatalog(Guid categoryId) => new(
        "P1", LineType.Part, "สินค้า", null, "ชิ้น", 10m, 20m, null,
        0, 0, 0, 0, null, categoryId);

    private sealed class ManagerUser : ICurrentUser
    {
        public long UserId => 1;
        public string UserName => "ผู้จัดการ";
        public UserRole Role => UserRole.Manager;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => true;
        public Guid? SessionId => null;
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

    private sealed class FakeMasterDataRepository : IMasterDataRepository
    {
        public List<Supplier> Suppliers { get; } = [];
        public List<Warehouse> Warehouses { get; } = [];
        public List<CatalogCategory> Categories { get; } = [];
        public List<CatalogItemSupplier> Links { get; } = [];
        public Task<IReadOnlyList<Supplier>> SearchSuppliersAsync(string? keyword, bool includeInactive, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Supplier>>(Suppliers);
        public Task<Supplier?> GetSupplierAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Suppliers.FirstOrDefault(x => x.Id == id));
        public Task<bool> SupplierCodeExistsAsync(string code, Guid? excludingId, CancellationToken ct = default) => Task.FromResult(Suppliers.Any(x => x.Code == code && x.Id != excludingId));
        public Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default) { Suppliers.Add(supplier); return Task.CompletedTask; }
        public Task<IReadOnlyList<Warehouse>> SearchWarehousesAsync(string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Warehouse>>(Warehouses);
        public Task<Warehouse?> GetWarehouseAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) => Task.FromResult(Warehouses.FirstOrDefault(x => x.Id == id));
        public Task<bool> WarehouseCodeExistsAsync(string code, Guid? excludingId, CancellationToken ct = default) => Task.FromResult(Warehouses.Any(x => x.Code == code && x.Id != excludingId));
        public Task AddWarehouseAsync(Warehouse warehouse, CancellationToken ct = default) { Warehouses.Add(warehouse); return Task.CompletedTask; }
        public Task<IReadOnlyList<CatalogCategory>> SearchCategoriesAsync(string? keyword, bool includeInactive, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CatalogCategory>>(Categories);
        public Task<CatalogCategory?> GetCategoryAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Categories.FirstOrDefault(x => x.Id == id));
        public Task<bool> CategoryCodeExistsAsync(string code, Guid? excludingId, CancellationToken ct = default) => Task.FromResult(Categories.Any(x => x.Code == code && x.Id != excludingId));
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
        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
