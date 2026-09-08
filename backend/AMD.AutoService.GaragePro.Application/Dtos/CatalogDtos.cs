using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record CatalogManagementQuery(
    string? Keyword = null,
    LineType? Type = null,
    bool IncludeInactive = false,
    bool LowStockOnly = false,
    int Page = 1,
    int PageSize = 25);

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
    bool StockLocked = false);

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
    Guid? WarehouseId = null);

public sealed record CatalogStatusRequest(bool IsActive);
