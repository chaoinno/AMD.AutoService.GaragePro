using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Suppliers;

public interface ISupplierService
{
    Task<Result<PagedResult<SupplierDto>>> SearchAsync(string? keyword, bool includeInactive, int page = 1, int pageSize = 25, CancellationToken ct = default);
    Task<Result<SupplierDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<SupplierDto>> CreateAsync(SupplierUpsertRequest request, CancellationToken ct = default);
    Task<Result<SupplierDto>> UpdateAsync(Guid id, SupplierUpsertRequest request, CancellationToken ct = default);
    Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CatalogItemSupplierDto>>> GetForCatalogItemAsync(Guid catalogItemId, CancellationToken ct = default);
    Task<Result<CatalogItemSupplierDto>> UpsertForCatalogItemAsync(Guid catalogItemId, Guid supplierId,
        CatalogItemSupplierUpsertRequest request, CancellationToken ct = default);
    Task<Result<bool>> RemoveFromCatalogItemAsync(Guid catalogItemId, Guid supplierId, CancellationToken ct = default);
}

public sealed class SupplierService(
    IMasterDataRepository repository, ICatalogRepository catalog, ICurrentUser user, TimeProvider clock) : ISupplierService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<PagedResult<SupplierDto>>> SearchAsync(
        string? keyword, bool includeInactive, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 100);
        var all = (await repository.SearchSuppliersAsync(MasterDataSupport.Clean(keyword), includeInactive, ct)).Select(Map).ToList();
        var totalPages = (int)Math.Ceiling(all.Count / (double)pageSize);
        var items = all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Result<PagedResult<SupplierDto>>.Ok(new(items, page, pageSize, all.Count, totalPages));
    }

    public async Task<Result<SupplierDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repository.GetSupplierAsync(id, ct);
        return entity is null
            ? Result<SupplierDto>.Fail("SUPPLIER_NOT_FOUND", "ไม่พบซัพพลายเออร์")
            : Result<SupplierDto>.Ok(Map(entity));
    }

    public async Task<Result<SupplierDto>> CreateAsync(SupplierUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<SupplierDto>.Fail(forbidden);
        var normalized = Normalize(request);
        var error = Validate(normalized);
        if (error is not null) return Result<SupplierDto>.Fail(error);
        if (await repository.SupplierCodeExistsAsync(normalized.Code, null, ct))
            return Result<SupplierDto>.Fail("SUPPLIER_CODE_DUPLICATE", "รหัสซัพพลายเออร์นี้มีอยู่แล้ว", "code");

        var entity = new Supplier
        {
            Code = normalized.Code, Name = normalized.Name, ContactName = normalized.ContactName,
            Phone = normalized.Phone, Email = normalized.Email, Address = normalized.Address,
            TaxId = normalized.TaxId, PaymentTerms = normalized.PaymentTerms, Note = normalized.Note,
            CreatedDate = Now, LastUpdated = Now
        };
        await repository.AddSupplierAsync(entity, ct);
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(Supplier),
            "supplier.created", $"เพิ่มซัพพลายเออร์ {entity.Code} {entity.Name}", Now), ct);
        var saveError = await SaveAsync<SupplierDto>("SUPPLIER_CODE_DUPLICATE", "รหัสซัพพลายเออร์นี้มีอยู่แล้ว", ct);
        return saveError ?? Result<SupplierDto>.Ok(Map(entity));
    }

    public async Task<Result<SupplierDto>> UpdateAsync(Guid id, SupplierUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<SupplierDto>.Fail(forbidden);
        var normalized = Normalize(request);
        var error = Validate(normalized);
        if (error is not null) return Result<SupplierDto>.Fail(error);
        var entity = await repository.GetSupplierAsync(id, ct);
        if (entity is null) return Result<SupplierDto>.Fail("SUPPLIER_NOT_FOUND", "ไม่พบซัพพลายเออร์");
        if (await repository.SupplierCodeExistsAsync(normalized.Code, id, ct))
            return Result<SupplierDto>.Fail("SUPPLIER_CODE_DUPLICATE", "รหัสซัพพลายเออร์นี้มีอยู่แล้ว", "code");

        entity.Code = normalized.Code; entity.Name = normalized.Name; entity.ContactName = normalized.ContactName;
        entity.Phone = normalized.Phone; entity.Email = normalized.Email; entity.Address = normalized.Address;
        entity.TaxId = normalized.TaxId; entity.PaymentTerms = normalized.PaymentTerms; entity.Note = normalized.Note;
        entity.LastUpdated = Now;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(Supplier),
            "supplier.updated", $"แก้ไขซัพพลายเออร์ {entity.Code} {entity.Name}", Now), ct);
        var saveError = await SaveAsync<SupplierDto>("SUPPLIER_CODE_DUPLICATE", "รหัสซัพพลายเออร์นี้มีอยู่แล้ว", ct);
        return saveError ?? Result<SupplierDto>.Ok(Map(entity));
    }

    public async Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<bool>.Fail(forbidden);
        var entity = await repository.GetSupplierAsync(id, ct);
        if (entity is null) return Result<bool>.Fail("SUPPLIER_NOT_FOUND", "ไม่พบซัพพลายเออร์");
        entity.IsActive = request.IsActive; entity.LastUpdated = Now;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(Supplier),
            request.IsActive ? "supplier.activated" : "supplier.deactivated",
            $"{(request.IsActive ? "เปิด" : "ปิด")}ใช้งานซัพพลายเออร์ {entity.Code}", Now), ct);
        await repository.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    public async Task<Result<IReadOnlyList<CatalogItemSupplierDto>>> GetForCatalogItemAsync(
        Guid catalogItemId, CancellationToken ct = default)
    {
        if (await catalog.GetAsync(user.ShardKey, user.BranchId, catalogItemId, ct) is null)
            return Result<IReadOnlyList<CatalogItemSupplierDto>>.Fail("CATALOG_NOT_FOUND", "ไม่พบสินค้าในสาขาปัจจุบัน");
        return Result<IReadOnlyList<CatalogItemSupplierDto>>.Ok(
            (await repository.GetItemSuppliersAsync(catalogItemId, ct)).Select(MapLink).ToList());
    }

    public async Task<Result<CatalogItemSupplierDto>> UpsertForCatalogItemAsync(
        Guid catalogItemId, Guid supplierId, CatalogItemSupplierUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<CatalogItemSupplierDto>.Fail(forbidden);
        if (request.SupplierItemCode?.Trim().Length > 60)
            return Result<CatalogItemSupplierDto>.Fail("SUPPLIER_ITEM_CODE_TOO_LONG", "รหัสสินค้าของซัพพลายเออร์ต้องยาวไม่เกิน 60 ตัวอักษร", "supplierItemCode");
        if (request.SupplierCost < 0 || request.LeadTimeDays < 0 || request.MinOrderQty < 0)
            return Result<CatalogItemSupplierDto>.Fail("SUPPLIER_LINK_VALUE_INVALID", "ราคา ระยะเวลารอ และจำนวนสั่งขั้นต่ำต้องไม่น้อยกว่า 0");
        if (await catalog.GetAsync(user.ShardKey, user.BranchId, catalogItemId, ct) is null)
            return Result<CatalogItemSupplierDto>.Fail("CATALOG_NOT_FOUND", "ไม่พบสินค้าในสาขาปัจจุบัน");
        var supplier = await repository.GetSupplierAsync(supplierId, ct);
        if (supplier is null || !supplier.IsActive)
            return Result<CatalogItemSupplierDto>.Fail("SUPPLIER_NOT_FOUND", "ไม่พบซัพพลายเออร์ที่เปิดใช้งาน");

        var link = await repository.GetItemSupplierAsync(catalogItemId, supplierId, ct);
        if (request.IsPreferred && await repository.PreferredSupplierExistsAsync(catalogItemId, link?.Id, ct))
            return Result<CatalogItemSupplierDto>.Fail("SUPPLIER_PREFERRED_DUPLICATE", "สินค้าแต่ละรายการมีซัพพลายเออร์หลักได้เพียงหนึ่งราย", "isPreferred");
        if (link is null)
        {
            link = new CatalogItemSupplier { CatalogItemId = catalogItemId, SupplierId = supplierId, Supplier = supplier, CreatedDate = Now };
            await repository.AddItemSupplierAsync(link, ct);
        }
        link.SupplierItemCode = MasterDataSupport.Clean(request.SupplierItemCode);
        link.SupplierCost = request.SupplierCost; link.LeadTimeDays = request.LeadTimeDays;
        link.MinOrderQty = request.MinOrderQty; link.IsPreferred = request.IsPreferred;
        link.IsActive = request.IsActive; link.LastUpdated = Now;
        await repository.AddEventAsync(MasterDataSupport.Event(user, link.Id, nameof(CatalogItemSupplier),
            "catalog.supplier.updated", $"ผูกสินค้าเข้ากับซัพพลายเออร์ {supplier.Code}", Now), ct);
        var saveError = await SaveAsync<CatalogItemSupplierDto>("SUPPLIER_LINK_DUPLICATE", "สินค้านี้ผูกกับซัพพลายเออร์รายนี้แล้ว", ct);
        return saveError ?? Result<CatalogItemSupplierDto>.Ok(MapLink(link));
    }

    public async Task<Result<bool>> RemoveFromCatalogItemAsync(
        Guid catalogItemId, Guid supplierId, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<bool>.Fail(forbidden);
        if (await catalog.GetAsync(user.ShardKey, user.BranchId, catalogItemId, ct) is null)
            return Result<bool>.Fail("CATALOG_NOT_FOUND", "ไม่พบสินค้าในสาขาปัจจุบัน");
        var link = await repository.GetItemSupplierAsync(catalogItemId, supplierId, ct);
        if (link is null) return Result<bool>.Fail("SUPPLIER_LINK_NOT_FOUND", "ไม่พบการผูกสินค้ากับซัพพลายเออร์");
        await repository.AddEventAsync(MasterDataSupport.Event(user, link.Id, nameof(CatalogItemSupplier),
            "catalog.supplier.removed", $"ยกเลิกการผูกสินค้ากับซัพพลายเออร์ {link.Supplier?.Code ?? supplierId.ToString()}", Now), ct);
        repository.RemoveItemSupplier(link);
        await repository.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    private async Task<Result<T>?> SaveAsync<T>(string fallbackCode, string message, CancellationToken ct)
    {
        try { await repository.SaveChangesAsync(ct); return null; }
        catch (MasterDataConflictException ex)
        {
            var code = MasterDataSupport.ConflictCode(ex, fallbackCode);
            var resolved = code == "SUPPLIER_PREFERRED_DUPLICATE"
                ? "สินค้าแต่ละรายการมีซัพพลายเออร์หลักได้เพียงหนึ่งราย" : message;
            return Result<T>.Fail(code, resolved);
        }
    }

    private static SupplierDto Map(Supplier x) => new(x.Id, x.Code, x.Name, x.ContactName, x.Phone,
        x.Email, x.Address, x.TaxId, x.PaymentTerms, x.Note, x.IsActive, x.CreatedDate, x.LastUpdated);

    private static CatalogItemSupplierDto MapLink(CatalogItemSupplier x) => new(x.Id, x.CatalogItemId,
        x.SupplierId, x.Supplier?.Code ?? string.Empty, x.Supplier?.Name ?? string.Empty, x.SupplierItemCode,
        x.SupplierCost, x.LeadTimeDays, x.MinOrderQty, x.IsPreferred, x.IsActive, x.CreatedDate, x.LastUpdated);

    private static SupplierUpsertRequest Normalize(SupplierUpsertRequest x) => x with
    {
        Code = MasterDataSupport.Code(x.Code), Name = x.Name.Trim(), ContactName = MasterDataSupport.Clean(x.ContactName),
        Phone = MasterDataSupport.Clean(x.Phone), Email = MasterDataSupport.Clean(x.Email), Address = MasterDataSupport.Clean(x.Address),
        TaxId = MasterDataSupport.Clean(x.TaxId), PaymentTerms = MasterDataSupport.Clean(x.PaymentTerms), Note = MasterDataSupport.Clean(x.Note)
    };

    private static ApiError? Validate(SupplierUpsertRequest x) =>
        MasterDataSupport.Required(x.Code, 30, "SUPPLIER", "รหัสซัพพลายเออร์", "code") ??
        MasterDataSupport.Required(x.Name, 300, "SUPPLIER", "ชื่อซัพพลายเออร์", "name") ??
        MasterDataSupport.Optional(x.ContactName, 150, "SUPPLIER", "ชื่อผู้ติดต่อ", "contactName") ??
        MasterDataSupport.Optional(x.Phone, 30, "SUPPLIER", "เบอร์โทรศัพท์", "phone") ??
        MasterDataSupport.Optional(x.Email, 150, "SUPPLIER", "อีเมล", "email") ??
        MasterDataSupport.Optional(x.Address, 500, "SUPPLIER", "ที่อยู่", "address") ??
        MasterDataSupport.Optional(x.TaxId, 20, "SUPPLIER", "เลขผู้เสียภาษี", "taxId") ??
        MasterDataSupport.Optional(x.PaymentTerms, 100, "SUPPLIER", "เงื่อนไขชำระเงิน", "paymentTerms") ??
        MasterDataSupport.Optional(x.Note, 500, "SUPPLIER", "หมายเหตุ", "note");
}
