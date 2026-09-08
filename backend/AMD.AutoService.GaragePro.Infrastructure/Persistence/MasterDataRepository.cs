using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class MasterDataRepository(ServiceDbContext db) : IMasterDataRepository
{
    public async Task<IReadOnlyList<Supplier>> SearchSuppliersAsync(
        string? keyword, bool includeInactive, CancellationToken ct = default)
    {
        var query = db.Suppliers.AsNoTracking();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(x => EF.Functions.Like(x.Code, $"%{term}%") ||
                                     EF.Functions.Like(x.Name, $"%{term}%"));
        }
        return await query.OrderBy(x => x.Code).Take(500).ToListAsync(ct);
    }

    public Task<Supplier?> GetSupplierAsync(Guid id, CancellationToken ct = default) =>
        db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> SupplierCodeExistsAsync(string code, Guid? excludingId, CancellationToken ct = default) =>
        db.Suppliers.AnyAsync(x => x.Code == code && (!excludingId.HasValue || x.Id != excludingId), ct);

    public async Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default) =>
        await db.Suppliers.AddAsync(supplier, ct);

    public async Task<IReadOnlyList<Warehouse>> SearchWarehousesAsync(
        string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default)
    {
        var query = db.Warehouses.AsNoTracking()
            .Where(x => x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId);
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(x => EF.Functions.Like(x.Code, $"%{term}%") ||
                                     EF.Functions.Like(x.Name, $"%{term}%"));
        }
        return await query.OrderBy(x => x.Code).Take(500).ToListAsync(ct);
    }

    public Task<Warehouse?> GetWarehouseAsync(
        string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
        db.Warehouses.FirstOrDefaultAsync(x => x.Id == id &&
            x.LegacyShardKey == shardKey && x.LegacyBranchId == branchId, ct);

    public Task<bool> WarehouseCodeExistsAsync(string code, Guid? excludingId, CancellationToken ct = default) =>
        db.Warehouses.AnyAsync(x => x.Code == code && (!excludingId.HasValue || x.Id != excludingId), ct);

    public async Task AddWarehouseAsync(Warehouse warehouse, CancellationToken ct = default) =>
        await db.Warehouses.AddAsync(warehouse, ct);

    public async Task<IReadOnlyList<CatalogCategory>> SearchCategoriesAsync(
        string? keyword, bool includeInactive, CancellationToken ct = default)
    {
        var query = db.CatalogCategories.AsNoTracking().AsQueryable();
        if (!includeInactive) query = query.Where(x => x.IsActive);
        if (!string.IsNullOrWhiteSpace(keyword))
        {
            var term = keyword.Trim();
            query = query.Where(x => EF.Functions.Like(x.Code, $"%{term}%") ||
                                     EF.Functions.Like(x.Name, $"%{term}%"));
        }
        return await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Code).Take(1000).ToListAsync(ct);
    }

    public Task<CatalogCategory?> GetCategoryAsync(Guid id, CancellationToken ct = default) =>
        db.CatalogCategories.Include(x => x.Children).FirstOrDefaultAsync(x => x.Id == id, ct);

    public Task<bool> CategoryCodeExistsAsync(string code, Guid? excludingId, CancellationToken ct = default) =>
        db.CatalogCategories.AnyAsync(x => x.Code == code && (!excludingId.HasValue || x.Id != excludingId), ct);

    public Task<bool> CategoryHasChildrenAsync(Guid id, bool activeOnly, CancellationToken ct = default) =>
        db.CatalogCategories.AnyAsync(x => x.ParentCategoryId == id && (!activeOnly || x.IsActive), ct);

    public async Task<bool> CategoryWouldCycleAsync(Guid categoryId, Guid parentId, CancellationToken ct = default)
    {
        var cursor = (Guid?)parentId;
        var visited = new HashSet<Guid>();
        while (cursor.HasValue && visited.Add(cursor.Value))
        {
            if (cursor.Value == categoryId) return true;
            cursor = await db.CatalogCategories.Where(x => x.Id == cursor.Value)
                .Select(x => x.ParentCategoryId).FirstOrDefaultAsync(ct);
        }
        return false;
    }

    public Task<bool> CategoryInUseAsync(Guid id, CancellationToken ct = default) =>
        db.CatalogItems.AnyAsync(x => x.CategoryId == id, ct);

    public async Task AddCategoryAsync(CatalogCategory category, CancellationToken ct = default) =>
        await db.CatalogCategories.AddAsync(category, ct);

    public void RemoveCategory(CatalogCategory category) => db.CatalogCategories.Remove(category);

    public async Task<IReadOnlyList<CatalogItemSupplier>> GetItemSuppliersAsync(
        Guid catalogItemId, CancellationToken ct = default) =>
        await db.CatalogItemSuppliers.AsNoTracking().Include(x => x.Supplier)
            .Where(x => x.CatalogItemId == catalogItemId)
            .OrderByDescending(x => x.IsPreferred).ThenBy(x => x.Supplier!.Code).ToListAsync(ct);

    public Task<CatalogItemSupplier?> GetItemSupplierAsync(
        Guid catalogItemId, Guid supplierId, CancellationToken ct = default) =>
        db.CatalogItemSuppliers.Include(x => x.Supplier)
            .FirstOrDefaultAsync(x => x.CatalogItemId == catalogItemId && x.SupplierId == supplierId, ct);

    public Task<bool> PreferredSupplierExistsAsync(
        Guid catalogItemId, Guid? excludingId, CancellationToken ct = default) =>
        db.CatalogItemSuppliers.AnyAsync(x => x.CatalogItemId == catalogItemId && x.IsPreferred &&
            (!excludingId.HasValue || x.Id != excludingId), ct);

    public async Task AddItemSupplierAsync(CatalogItemSupplier itemSupplier, CancellationToken ct = default) =>
        await db.CatalogItemSuppliers.AddAsync(itemSupplier, ct);

    public void RemoveItemSupplier(CatalogItemSupplier itemSupplier) => db.CatalogItemSuppliers.Remove(itemSupplier);

    public async Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(activityEvent, ct);

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            var constraint = KnownConstraints.FirstOrDefault(sql.Message.Contains) ?? "UNIQUE_CONSTRAINT";
            throw new MasterDataConflictException(constraint, ex);
        }
    }

    private static readonly string[] KnownConstraints =
    [
        "UX_svc_Supplier_Code",
        "UX_svc_Warehouse_Code",
        "UX_svc_CatalogCategory_Code",
        "UX_svc_CatalogItemSupplier_Item_Supplier",
        "UX_svc_CatalogItemSupplier_Preferred"
    ];
}
