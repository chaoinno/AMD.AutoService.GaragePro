using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public sealed class MasterDataConflictException(string constraintName, Exception innerException)
    : Exception($"Master-data constraint failed: {constraintName}", innerException)
{
    public string ConstraintName { get; } = constraintName;
}

/// <summary>
/// ข้อมูลหลักของสาขา (ซัพพลายเออร์ · คลัง · หมวดหมู่) — [SECURITY] ทุกเมธอดที่ค้นหา/อ่านด้วย id ที่มาจาก client รับ
/// (shardKey, branchId) จาก JWT เสมอ: id ของสาขาอื่นต้องได้ผลเหมือน "ไม่พบ" ไม่ใช่ 403 (ไม่บอกว่ามีอยู่จริง)
/// · เมธอดที่ตรวจ "ของที่ผูกอยู่" ของ entity ที่ผ่านการค้นหาแบบมี scope มาแล้ว (CategoryHasChildren/WouldCycle/InUse)
///   รับแค่ id และนับทั้งตารางโดยตั้งใจ — แถวผูกข้ามสาขาไม่ควรมี แต่ถ้ามีก็ต้องกันลบ/ปิดไว้ก่อนเพื่อไม่ให้ FK ขาด
/// </summary>
public interface IMasterDataRepository
{
    Task<IReadOnlyList<Supplier>> SearchSuppliersAsync(
        string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default);
    Task<Supplier?> GetSupplierAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default);
    Task<bool> SupplierCodeExistsAsync(
        string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default);
    Task AddSupplierAsync(Supplier supplier, CancellationToken ct = default);

    Task<IReadOnlyList<Warehouse>> SearchWarehousesAsync(
        string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default);
    Task<Warehouse?> GetWarehouseAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default);
    Task<bool> WarehouseCodeExistsAsync(
        string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default);
    /// <summary>สาขามีคลังอยู่แล้วหรือยัง (นับทั้งที่ปิดใช้งาน) — ใช้ตัดสินว่าจะ seed คลังเริ่มต้นหรือไม่</summary>
    Task<bool> AnyWarehouseAsync(string shardKey, int branchId, CancellationToken ct = default);
    Task AddWarehouseAsync(Warehouse warehouse, CancellationToken ct = default);

    Task<IReadOnlyList<CatalogCategory>> SearchCategoriesAsync(
        string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default);
    Task<CatalogCategory?> GetCategoryAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default);
    Task<bool> CategoryCodeExistsAsync(
        string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default);
    /// <summary>สาขามีหมวดหมู่อยู่แล้วหรือยัง (นับทั้งที่ปิดใช้งาน) — ใช้ตัดสินว่าจะ seed หมวดหมู่เริ่มต้นหรือไม่</summary>
    Task<bool> AnyCategoryAsync(string shardKey, int branchId, CancellationToken ct = default);
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
