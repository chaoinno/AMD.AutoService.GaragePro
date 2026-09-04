using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Catalog;

public interface ICatalogService
{
    Task<Result<PagedResult<CatalogManagementItemDto>>> SearchAsync(CatalogManagementQuery query, CancellationToken ct = default);
    Task<Result<CatalogManagementItemDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<CatalogManagementItemDto>> CreateAsync(CatalogUpsertRequest request, CancellationToken ct = default);
    Task<Result<CatalogManagementItemDto>> UpdateAsync(Guid id, CatalogUpsertRequest request, CancellationToken ct = default);
    Task<Result<bool>> SetStatusAsync(Guid id, CatalogStatusRequest request, CancellationToken ct = default);
}

public sealed class CatalogService : ICatalogService
{
    private readonly ICatalogRepository repository;
    private readonly ICurrentUser currentUser;
    private readonly IMasterDataRepository? masterData;

    public CatalogService(ICatalogRepository repository, ICurrentUser currentUser)
        : this(repository, currentUser, null) { }

    public CatalogService(ICatalogRepository repository, ICurrentUser currentUser, IMasterDataRepository? masterData)
    {
        this.repository = repository;
        this.currentUser = currentUser;
        this.masterData = masterData;
    }

    public async Task<Result<PagedResult<CatalogManagementItemDto>>> SearchAsync(
        CatalogManagementQuery query, CancellationToken ct = default)
    {
        var normalized = query with
        {
            Keyword = Clean(query.Keyword),
            Page = Math.Max(1, query.Page),
            PageSize = Math.Clamp(query.PageSize, 1, 100)
        };
        var (items, total) = await repository.SearchManagementAsync(
            currentUser.ShardKey, currentUser.BranchId, normalized, ct);
        var mapped = items.Select(Map).ToList();
        return Result<PagedResult<CatalogManagementItemDto>>.Ok(new(
            mapped, normalized.Page, normalized.PageSize, total,
            (int)Math.Ceiling(total / (double)normalized.PageSize)));
    }

    public async Task<Result<CatalogManagementItemDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var item = await repository.GetAsync(currentUser.ShardKey, currentUser.BranchId, id, ct);
        return item is null
            ? Result<CatalogManagementItemDto>.Fail("CATALOG_NOT_FOUND", "ไม่พบสินค้าหรือรายการนี้ไม่ได้อยู่ในสาขาปัจจุบัน")
            : Result<CatalogManagementItemDto>.Ok(Map(item));
    }

    public async Task<Result<CatalogManagementItemDto>> CreateAsync(
        CatalogUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = EnsureCanManage();
        if (forbidden is not null) return Result<CatalogManagementItemDto>.Fail(forbidden);
        var normalized = Normalize(request);
        var error = CatalogValidator.Validate(normalized);
        if (error is not null) return Result<CatalogManagementItemDto>.Fail(error);
        var referenceError = await ValidateReferencesAsync(normalized.CategoryId, normalized.WarehouseId, ct);
        if (referenceError is not null) return Result<CatalogManagementItemDto>.Fail(referenceError);
        if (await repository.CodeExistsAsync(currentUser.ShardKey, currentUser.BranchId, normalized.Code, null, ct))
            return Result<CatalogManagementItemDto>.Fail("CATALOG_CODE_DUPLICATE", "รหัสสินค้านี้มีอยู่แล้วในสาขาปัจจุบัน", "code");

        var item = new CatalogItem
        {
            Code = normalized.Code,
            Type = normalized.Type,
            Name = normalized.Name,
            Compatibility = normalized.Compatibility,
            Unit = normalized.Unit,
            Cost = normalized.Cost,
            Price = normalized.Price,
            StandardHours = normalized.StandardHours,
            OnHand = normalized.OnHand,
            Reserved = normalized.Reserved,
            OnOrder = normalized.OnOrder,
            Damaged = normalized.Damaged,
            EtaNote = normalized.EtaNote,
            LegacyShardKey = currentUser.ShardKey,
            LegacyBranchId = currentUser.BranchId,
            CategoryId = normalized.CategoryId,
            WarehouseId = normalized.WarehouseId
        };
        await repository.AddAsync(item, ct);
        await repository.AddEventAsync(Event(item, "catalog.created", $"เพิ่มสินค้า {item.Code} {item.Name}"), ct);
        await repository.SaveChangesAsync(ct);
        return Result<CatalogManagementItemDto>.Ok(Map(item));
    }

    public async Task<Result<CatalogManagementItemDto>> UpdateAsync(
        Guid id, CatalogUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = EnsureCanManage();
        if (forbidden is not null) return Result<CatalogManagementItemDto>.Fail(forbidden);
        var normalized = Normalize(request);
        var error = CatalogValidator.Validate(normalized);
        if (error is not null) return Result<CatalogManagementItemDto>.Fail(error);
        var referenceError = await ValidateReferencesAsync(normalized.CategoryId, normalized.WarehouseId, ct);
        if (referenceError is not null) return Result<CatalogManagementItemDto>.Fail(referenceError);
        var item = await repository.GetAsync(currentUser.ShardKey, currentUser.BranchId, id, ct);
        if (item is null)
            return Result<CatalogManagementItemDto>.Fail("CATALOG_NOT_FOUND", "ไม่พบสินค้าหรือรายการนี้ไม่ได้อยู่ในสาขาปัจจุบัน");
        if (await repository.CodeExistsAsync(currentUser.ShardKey, currentUser.BranchId, normalized.Code, id, ct))
            return Result<CatalogManagementItemDto>.Fail("CATALOG_CODE_DUPLICATE", "รหัสสินค้านี้มีอยู่แล้วในสาขาปัจจุบัน", "code");

        item.Code = normalized.Code;
        item.Type = normalized.Type;
        item.Name = normalized.Name;
        item.Compatibility = normalized.Compatibility;
        item.Unit = normalized.Unit;
        item.Cost = normalized.Cost;
        item.Price = normalized.Price;
        item.StandardHours = normalized.StandardHours;
        item.OnHand = normalized.OnHand;
        item.Reserved = normalized.Reserved;
        item.OnOrder = normalized.OnOrder;
        item.Damaged = normalized.Damaged;
        item.EtaNote = normalized.EtaNote;
        item.CategoryId = normalized.CategoryId;
        item.WarehouseId = normalized.WarehouseId;
        await repository.AddEventAsync(Event(item, "catalog.updated", $"แก้ไขสินค้า {item.Code} {item.Name}"), ct);
        await repository.SaveChangesAsync(ct);
        return Result<CatalogManagementItemDto>.Ok(Map(item));
    }

    public async Task<Result<bool>> SetStatusAsync(
        Guid id, CatalogStatusRequest request, CancellationToken ct = default)
    {
        var forbidden = EnsureCanManage();
        if (forbidden is not null) return Result<bool>.Fail(forbidden);
        var item = await repository.GetAsync(currentUser.ShardKey, currentUser.BranchId, id, ct);
        if (item is null)
            return Result<bool>.Fail("CATALOG_NOT_FOUND", "ไม่พบสินค้าหรือรายการนี้ไม่ได้อยู่ในสาขาปัจจุบัน");
        item.IsActive = request.IsActive;
        await repository.AddEventAsync(Event(item, request.IsActive ? "catalog.activated" : "catalog.deactivated",
            $"{(request.IsActive ? "เปิด" : "ปิด")}ใช้งานสินค้า {item.Code} {item.Name}"), ct);
        await repository.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    private ApiError? EnsureCanManage() => currentUser.CanSeeCost
        ? null
        : new("CATALOG_MANAGE_FORBIDDEN", "เฉพาะผู้จัดการสาขาเท่านั้นที่เพิ่ม แก้ไข หรือเปลี่ยนสถานะสินค้าได้");

    private CatalogManagementItemDto Map(CatalogItem item) => new(
        item.Id, item.Code, item.Type.ToString().ToLowerInvariant(),
        item.Type == LineType.Part ? "อะไหล่" : "ค่าแรง", item.Name, item.Compatibility,
        item.Unit, item.Price, currentUser.CanSeeCost ? item.Cost : null, item.StandardHours,
        item.OnHand, item.Reserved, item.OnOrder, item.Damaged, item.Available, item.EtaNote, item.IsActive,
        item.CategoryId, item.WarehouseId);

    private ActivityEvent Event(CatalogItem item, string eventType, string description) => new()
    {
        EntityId = item.Id,
        EntityType = nameof(CatalogItem),
        EventType = eventType,
        DescriptionTh = description,
        PerformedByUserId = currentUser.UserId,
        PerformedByName = currentUser.UserName,
        Source = currentUser.Source,
        PayloadJson = JsonSerializer.Serialize(new { item.Code, item.Type, item.IsActive })
    };

    private static CatalogUpsertRequest Normalize(CatalogUpsertRequest request)
    {
        var type = request.Type;
        return request with
        {
            Code = request.Code.Trim().ToUpperInvariant(),
            Name = request.Name.Trim(),
            Compatibility = Clean(request.Compatibility),
            Unit = request.Unit.Trim(),
            StandardHours = type == LineType.Labor ? request.StandardHours : null,
            OnHand = type == LineType.Part ? request.OnHand : 0,
            Reserved = type == LineType.Part ? request.Reserved : 0,
            OnOrder = type == LineType.Part ? request.OnOrder : 0,
            Damaged = type == LineType.Part ? request.Damaged : 0,
            EtaNote = type == LineType.Part ? Clean(request.EtaNote) : null
        };
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private async Task<ApiError?> ValidateReferencesAsync(Guid? categoryId, Guid? warehouseId, CancellationToken ct)
    {
        if (masterData is null && (categoryId.HasValue || warehouseId.HasValue))
            return new("CATALOG_REFERENCE_UNAVAILABLE", "ยังไม่สามารถตรวจสอบหมวดหมู่หรือคลังได้");

        if (categoryId.HasValue)
        {
            var category = await masterData!.GetCategoryAsync(categoryId.Value, ct);
            if (category is null || !category.IsActive)
                return new("CATEGORY_NOT_FOUND", "ไม่พบหมวดหมู่สินค้าที่เปิดใช้งาน", "categoryId");
            // [BIZ] สินค้าผูกได้เฉพาะหมวดปลายทาง (leaf) เท่านั้น
            if (await masterData.CategoryHasChildrenAsync(categoryId.Value, activeOnly: false, ct))
                return new("CATEGORY_NOT_LEAF", "เลือกได้เฉพาะหมวดหมู่ที่ไม่มีหมวดย่อย", "categoryId");
        }

        if (warehouseId.HasValue &&
            await masterData!.GetWarehouseAsync(currentUser.ShardKey, currentUser.BranchId, warehouseId.Value, ct) is not { IsActive: true })
            return new("WAREHOUSE_NOT_FOUND", "ไม่พบคลังที่เปิดใช้งานในสาขาปัจจุบัน", "warehouseId");

        return null;
    }
}
