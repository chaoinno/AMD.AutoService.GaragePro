using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public sealed class MasterDataConflictException(string constraintName, Exception innerException)
    : Exception($"Master-data constraint failed: {constraintName}", innerException)
{
    public string ConstraintName { get; } = constraintName;
}

public interface IMasterDataRepository
{
    Task<IReadOnlyList<Supplier>> SearchSuppliersAsync(string? keyword, bool includeInactive, CancellationToken ct = default);
    Task<Supplier?> GetSupplierAsync(Guid id, CancellationToken ct = default);
    Task<bool> SupplierCodeExistsAsync(string code, Guid? excludingId, CancellationToken ct = default);
    Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default);

    Task<IReadOnlyList<Warehouse>> SearchWarehousesAsync(
        string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default);
    Task<Warehouse?> GetWarehouseAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default);
    Task<bool> WarehouseCodeExistsAsync(string code, Guid? excludingId, CancellationToken ct = default);
    Task AddWarehouseAsync(Warehouse warehouse, CancellationToken ct = default);

    Task<IReadOnlyList<CatalogCategory>> SearchCategoriesAsync(string? keyword, bool includeInactive, CancellationToken ct = default);
    Task<CatalogCategory?> GetCategoryAsync(Guid id, CancellationToken ct = default);
    Task<bool> CategoryCodeExistsAsync(string code, Guid? excludingId, CancellationToken ct = default);
    Task<bool> CategoryHasChildrenAsync(Guid id, bool activeOnly, CancellationToken ct = default);
    Task<bool> CategoryWouldCycleAsync(Guid categoryId, Guid parentId, CancellationToken ct = default);
    Task<bool> CategoryInUseAsync(Guid id, CancellationToken ct = default);
    Task AddCategoryAsync(CatalogCategory category, CancellationToken ct = default);
    void RemoveCategory(CatalogCategory category);

    Task<IReadOnlyList<CatalogItemSupplier>> GetItemSuppliersAsync(Guid catalogItemId, CancellationToken ct = default);
    Task<CatalogItemSupplier?> GetItemSupplierAsync(Guid catalogItemId, Guid supplierId, CancellationToken ct = default);
    Task<bool> PreferredSupplierExistsAsync(Guid catalogItemId, Guid? excludingId, CancellationToken ct = default);
    Task AddItemSupplierAsync(CatalogItemSupplier itemSupplier, CancellationToken ct = default);
    void RemoveItemSupplier(CatalogItemSupplier itemSupplier);

    Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
