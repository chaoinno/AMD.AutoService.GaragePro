using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.CatalogCategories;

public interface ICatalogCategoryService
{
    Task<Result<IReadOnlyList<CatalogCategoryDto>>> SearchAsync(string? keyword, bool includeInactive, CancellationToken ct = default);
    Task<Result<CatalogCategoryDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<CatalogCategoryDto>> CreateAsync(CatalogCategoryUpsertRequest request, CancellationToken ct = default);
    Task<Result<CatalogCategoryDto>> UpdateAsync(Guid id, CatalogCategoryUpsertRequest request, CancellationToken ct = default);
    Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default);
}

public sealed class CatalogCategoryService(
    IMasterDataRepository repository, ICurrentUser user, TimeProvider clock) : ICatalogCategoryService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<IReadOnlyList<CatalogCategoryDto>>> SearchAsync(
        string? keyword, bool includeInactive, CancellationToken ct = default)
    {
        var entities = await repository.SearchCategoriesAsync(MasterDataSupport.Clean(keyword), includeInactive, ct);
        var childrenByParent = entities.Where(x => x.ParentCategoryId.HasValue)
            .GroupBy(x => x.ParentCategoryId!.Value)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.SortOrder).ThenBy(y => y.Code).ToList());
        var roots = entities.Where(x => !x.ParentCategoryId.HasValue || !entities.Any(y => y.Id == x.ParentCategoryId))
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Code)
            .Select(x => MapTree(x, childrenByParent)).ToList();
        return Result<IReadOnlyList<CatalogCategoryDto>>.Ok(roots);
    }

    public async Task<Result<CatalogCategoryDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repository.GetCategoryAsync(id, ct);
        return entity is null
            ? Result<CatalogCategoryDto>.Fail("CATEGORY_NOT_FOUND", "ไม่พบหมวดหมู่สินค้า")
            : Result<CatalogCategoryDto>.Ok(Map(entity));
    }

    public async Task<Result<CatalogCategoryDto>> CreateAsync(
        CatalogCategoryUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<CatalogCategoryDto>.Fail(forbidden);
        var normalized = Normalize(request);
        var error = Validate(normalized);
        if (error is not null) return Result<CatalogCategoryDto>.Fail(error);
        if (await repository.CategoryCodeExistsAsync(normalized.Code, null, ct))
            return Result<CatalogCategoryDto>.Fail("CATEGORY_CODE_DUPLICATE", "รหัสหมวดหมู่นี้มีอยู่แล้ว", "code");
        if (normalized.ParentCategoryId.HasValue &&
            await repository.GetCategoryAsync(normalized.ParentCategoryId.Value, ct) is not { IsActive: true })
            return Result<CatalogCategoryDto>.Fail("CATEGORY_PARENT_NOT_FOUND", "ไม่พบหมวดหมู่หลักที่เปิดใช้งาน", "parentCategoryId");
        var entity = new CatalogCategory
        {
            Code = normalized.Code, Name = normalized.Name, ParentCategoryId = normalized.ParentCategoryId,
            SortOrder = normalized.SortOrder, CreatedDate = Now, LastUpdated = Now
        };
        await repository.AddCategoryAsync(entity, ct);
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(CatalogCategory),
            "category.created", $"เพิ่มหมวดหมู่ {entity.Code} {entity.Name}", Now), ct);
        var saveError = await SaveAsync<CatalogCategoryDto>(ct);
        return saveError ?? Result<CatalogCategoryDto>.Ok(Map(entity));
    }

    public async Task<Result<CatalogCategoryDto>> UpdateAsync(
        Guid id, CatalogCategoryUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<CatalogCategoryDto>.Fail(forbidden);
        var normalized = Normalize(request);
        var error = Validate(normalized);
        if (error is not null) return Result<CatalogCategoryDto>.Fail(error);
        var entity = await repository.GetCategoryAsync(id, ct);
        if (entity is null) return Result<CatalogCategoryDto>.Fail("CATEGORY_NOT_FOUND", "ไม่พบหมวดหมู่สินค้า");
        if (await repository.CategoryCodeExistsAsync(normalized.Code, id, ct))
            return Result<CatalogCategoryDto>.Fail("CATEGORY_CODE_DUPLICATE", "รหัสหมวดหมู่นี้มีอยู่แล้ว", "code");
        if (normalized.ParentCategoryId.HasValue)
        {
            if (await repository.GetCategoryAsync(normalized.ParentCategoryId.Value, ct) is not { IsActive: true })
                return Result<CatalogCategoryDto>.Fail("CATEGORY_PARENT_NOT_FOUND", "ไม่พบหมวดหมู่หลักที่เปิดใช้งาน", "parentCategoryId");
            if (await repository.CategoryWouldCycleAsync(id, normalized.ParentCategoryId.Value, ct))
            return Result<CatalogCategoryDto>.Fail("CATEGORY_CIRCULAR_REFERENCE", "ไม่สามารถย้ายหมวดหมู่ไปอยู่ใต้ตัวเองหรือหมวดย่อยของตัวเองได้", "parentCategoryId");
        }
        entity.Code = normalized.Code; entity.Name = normalized.Name;
        entity.ParentCategoryId = normalized.ParentCategoryId; entity.SortOrder = normalized.SortOrder; entity.LastUpdated = Now;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(CatalogCategory),
            "category.updated", $"แก้ไขหมวดหมู่ {entity.Code} {entity.Name}", Now), ct);
        var saveError = await SaveAsync<CatalogCategoryDto>(ct);
        return saveError ?? Result<CatalogCategoryDto>.Ok(Map(entity));
    }

    public async Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<bool>.Fail(forbidden);
        var entity = await repository.GetCategoryAsync(id, ct);
        if (entity is null) return Result<bool>.Fail("CATEGORY_NOT_FOUND", "ไม่พบหมวดหมู่สินค้า");
        // [BIZ] ปิดหมวดที่ยังมีหมวดย่อย active ไม่ได้ แม้ client จะซ่อนปุ่มแล้วก็ตาม
        if (!request.IsActive && await repository.CategoryHasChildrenAsync(id, activeOnly: true, ct))
            return Result<bool>.Fail("CATEGORY_HAS_CHILDREN", "ไม่สามารถปิดใช้งานหมวดหมู่ที่ยังมีหมวดย่อยเปิดใช้งานอยู่ได้");
        entity.IsActive = request.IsActive; entity.LastUpdated = Now;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(CatalogCategory),
            request.IsActive ? "category.activated" : "category.deactivated",
            $"{(request.IsActive ? "เปิด" : "ปิด")}ใช้งานหมวดหมู่ {entity.Code}", Now), ct);
        await repository.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    public async Task<Result<bool>> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<bool>.Fail(forbidden);
        var entity = await repository.GetCategoryAsync(id, ct);
        if (entity is null) return Result<bool>.Fail("CATEGORY_NOT_FOUND", "ไม่พบหมวดหมู่สินค้า");
        // [BIZ] ไม่ลบ hierarchy ที่ยังมีลูก เพื่อไม่ให้ tree ขาดกลาง
        if (await repository.CategoryHasChildrenAsync(id, activeOnly: false, ct))
            return Result<bool>.Fail("CATEGORY_HAS_CHILDREN", "ไม่สามารถลบหมวดหมู่ที่ยังมีหมวดย่อยอยู่ได้");
        if (await repository.CategoryInUseAsync(id, ct))
            return Result<bool>.Fail("CATEGORY_IN_USE", "ไม่สามารถลบหมวดหมู่ที่ยังถูกใช้กับสินค้าได้ กรุณาปิดใช้งานแทน");
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(CatalogCategory),
            "category.deleted", $"ลบหมวดหมู่ {entity.Code} {entity.Name}", Now), ct);
        repository.RemoveCategory(entity);
        await repository.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    private async Task<Result<T>?> SaveAsync<T>(CancellationToken ct)
    {
        try { await repository.SaveChangesAsync(ct); return null; }
        catch (MasterDataConflictException ex)
        {
            return Result<T>.Fail(MasterDataSupport.ConflictCode(ex, "CATEGORY_CODE_DUPLICATE"), "รหัสหมวดหมู่นี้มีอยู่แล้ว");
        }
    }

    private static CatalogCategoryDto Map(CatalogCategory x) => new(x.Id, x.Code, x.Name,
        x.ParentCategoryId, x.SortOrder, x.Children.Count != 0, x.IsActive, x.CreatedDate, x.LastUpdated,
        x.Children.Select(Map).ToList());
    private static CatalogCategoryDto MapTree(CatalogCategory x, IReadOnlyDictionary<Guid, List<CatalogCategory>> childrenByParent) =>
        new(x.Id, x.Code, x.Name, x.ParentCategoryId, x.SortOrder, childrenByParent.ContainsKey(x.Id), x.IsActive,
            x.CreatedDate, x.LastUpdated, childrenByParent.TryGetValue(x.Id, out var children)
                ? children.Select(child => MapTree(child, childrenByParent)).ToList() : []);
    private static CatalogCategoryUpsertRequest Normalize(CatalogCategoryUpsertRequest x) => x with
    { Code = MasterDataSupport.Code(x.Code), Name = x.Name.Trim() };
    private static ApiError? Validate(CatalogCategoryUpsertRequest x) =>
        MasterDataSupport.Required(x.Code, 30, "CATEGORY", "รหัสหมวดหมู่", "code") ??
        MasterDataSupport.Required(x.Name, 200, "CATEGORY", "ชื่อหมวดหมู่", "name") ??
        (x.SortOrder < 0 ? new ApiError("CATEGORY_SORT_ORDER_INVALID", "ลำดับต้องไม่น้อยกว่า 0", "sortOrder") : null);
}
