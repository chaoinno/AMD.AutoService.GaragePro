using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record CatalogManagementQuery(
    string? Keyword = null,
    LineType? Type = null,
    bool IncludeInactive = false,
    bool LowStockOnly = false,
    int Page = 1,
    int PageSize = 25,
    // ถึงจุดต่ำสุด (พร้อมใช้ ≤ min) — คนละความหมายกับ LowStockOnly (พร้อมใช้ ≤ 0) ที่คงไว้ตามเดิม
    bool BelowMinOnly = false);

public sealed record CatalogManagementItemDto(
    Guid Id,
    string Code,
    string Type,
    string TypeLabelTh,
    string Name,
    string? Compatibility,
    string Unit,
    decimal Price,
    decimal? Cost,
    decimal? StandardHours,
    int OnHand,
    int Reserved,
    int OnOrder,
    int Damaged,
    int Available,
    string? EtaNote,
    bool IsActive,
    Guid? CategoryId = null,
    Guid? WarehouseId = null,
    bool StockLocked = false,
    int? MinStock = null,
    int? MaxStock = null,
    bool BelowMin = false,
    int? SuggestedOrderQty = null);

public sealed record CatalogUpsertRequest(
    string Code,
    LineType Type,
    string Name,
    string? Compatibility,
    string Unit,
    decimal Cost,
    decimal Price,
    decimal? StandardHours,
    int OnHand,
    int Reserved,
    int OnOrder,
    int Damaged,
    string? EtaNote,
    Guid? CategoryId = null,
    Guid? WarehouseId = null,
    int? MinStock = null,
    int? MaxStock = null);

public sealed record CatalogStatusRequest(bool IsActive);

public sealed record PurchasePartCreateRequest(
    string Name,
    string Unit,
    string? Code = null,
    string? Compatibility = null);
