namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record SupplierDto(
    Guid Id, string Code, string Name, string? ContactName, string? Phone, string? Email,
    string? Address, string? TaxId, string? PaymentTerms, string? Note, bool IsActive,
    DateTime CreatedDate, DateTime LastUpdated);

public sealed record SupplierUpsertRequest(
    string Code, string Name, string? ContactName, string? Phone, string? Email,
    string? Address, string? TaxId, string? PaymentTerms, string? Note);

public sealed record WarehouseDto(
    Guid Id, string Code, string Name, string? LegacyShardKey, int? LegacyBranchId,
    string? Address, bool IsActive, DateTime CreatedDate, DateTime LastUpdated);

/// <summary>[SECURITY] ไม่มี shard/branch ใน body; service อ่านสองค่านี้จาก JWT เท่านั้น</summary>
public sealed record WarehouseUpsertRequest(string Code, string Name, string? Address);

public sealed record CatalogCategoryDto(
    Guid Id, string Code, string Name, Guid? ParentCategoryId, int? SortOrder,
    bool HasChildren, bool IsActive, DateTime CreatedDate, DateTime LastUpdated,
    IReadOnlyList<CatalogCategoryDto>? Children = null);

public sealed record CatalogCategoryUpsertRequest(
    string Code, string Name, Guid? ParentCategoryId, int? SortOrder);

public sealed record MasterDataStatusRequest(bool IsActive);

public sealed record CatalogItemSupplierDto(
    Guid Id, Guid CatalogItemId, Guid SupplierId, string SupplierCode, string SupplierName,
    string? SupplierItemCode, decimal? SupplierCost, int? LeadTimeDays, int? MinOrderQty,
    bool IsPreferred, bool IsActive, DateTime CreatedDate, DateTime LastUpdated);

public sealed record CatalogItemSupplierUpsertRequest(
    string? SupplierItemCode, decimal? SupplierCost, int? LeadTimeDays,
    int? MinOrderQty, bool IsPreferred, bool IsActive = true);
