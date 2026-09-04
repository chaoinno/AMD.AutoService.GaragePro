using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Warehouses;

public interface IWarehouseService
{
    Task<Result<IReadOnlyList<WarehouseDto>>> SearchAsync(string? keyword, bool includeInactive, CancellationToken ct = default);
    Task<Result<WarehouseDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<WarehouseDto>> CreateAsync(WarehouseUpsertRequest request, CancellationToken ct = default);
    Task<Result<WarehouseDto>> UpdateAsync(Guid id, WarehouseUpsertRequest request, CancellationToken ct = default);
    Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default);
}

public sealed class WarehouseService(
    IMasterDataRepository repository, ILegacyReader legacy, ICurrentUser user, TimeProvider clock) : IWarehouseService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<IReadOnlyList<WarehouseDto>>> SearchAsync(
        string? keyword, bool includeInactive, CancellationToken ct = default) =>
        Result<IReadOnlyList<WarehouseDto>>.Ok((await repository.SearchWarehousesAsync(
            user.ShardKey, user.BranchId, MasterDataSupport.Clean(keyword), includeInactive, ct)).Select(Map).ToList());

    public async Task<Result<WarehouseDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repository.GetWarehouseAsync(user.ShardKey, user.BranchId, id, ct);
        return entity is null
            ? Result<WarehouseDto>.Fail("WAREHOUSE_NOT_FOUND", "ไม่พบคลังในสาขาปัจจุบัน")
            : Result<WarehouseDto>.Ok(Map(entity));
    }

    public async Task<Result<WarehouseDto>> CreateAsync(WarehouseUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<WarehouseDto>.Fail(forbidden);
        var normalized = Normalize(request);
        var error = Validate(normalized);
        if (error is not null) return Result<WarehouseDto>.Fail(error);
        if (await repository.WarehouseCodeExistsAsync(normalized.Code, null, ct))
            return Result<WarehouseDto>.Fail("WAREHOUSE_CODE_DUPLICATE", "รหัสคลังนี้มีอยู่แล้ว", "code");

        // [SECURITY] shard/branch มาจาก JWT เท่านั้น และตรวจว่ามีสาขาจริงใน legacy แบบ read-only
        if (await legacy.GetBranchAsync(user.ShardKey, user.BranchId, ct) is null)
            return Result<WarehouseDto>.Fail("WAREHOUSE_BRANCH_NOT_FOUND", "ไม่พบสาขาของผู้ใช้ในระบบเดิม");

        var entity = new Warehouse
        {
            Code = normalized.Code, Name = normalized.Name, Address = normalized.Address,
            LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId,
            CreatedDate = Now, LastUpdated = Now
        };
        await repository.AddWarehouseAsync(entity, ct);
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(Warehouse),
            "warehouse.created", $"เพิ่มคลัง {entity.Code} {entity.Name}", Now), ct);
        var saveError = await SaveAsync<WarehouseDto>(ct);
        return saveError ?? Result<WarehouseDto>.Ok(Map(entity));
    }

    public async Task<Result<WarehouseDto>> UpdateAsync(Guid id, WarehouseUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<WarehouseDto>.Fail(forbidden);
        var normalized = Normalize(request);
        var error = Validate(normalized);
        if (error is not null) return Result<WarehouseDto>.Fail(error);
        var entity = await repository.GetWarehouseAsync(user.ShardKey, user.BranchId, id, ct);
        if (entity is null) return Result<WarehouseDto>.Fail("WAREHOUSE_NOT_FOUND", "ไม่พบคลังในสาขาปัจจุบัน");
        if (await repository.WarehouseCodeExistsAsync(normalized.Code, id, ct))
            return Result<WarehouseDto>.Fail("WAREHOUSE_CODE_DUPLICATE", "รหัสคลังนี้มีอยู่แล้ว", "code");
        entity.Code = normalized.Code; entity.Name = normalized.Name; entity.Address = normalized.Address; entity.LastUpdated = Now;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(Warehouse),
            "warehouse.updated", $"แก้ไขคลัง {entity.Code} {entity.Name}", Now), ct);
        var saveError = await SaveAsync<WarehouseDto>(ct);
        return saveError ?? Result<WarehouseDto>.Ok(Map(entity));
    }

    public async Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<bool>.Fail(forbidden);
        var entity = await repository.GetWarehouseAsync(user.ShardKey, user.BranchId, id, ct);
        if (entity is null) return Result<bool>.Fail("WAREHOUSE_NOT_FOUND", "ไม่พบคลังในสาขาปัจจุบัน");
        entity.IsActive = request.IsActive; entity.LastUpdated = Now;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(Warehouse),
            request.IsActive ? "warehouse.activated" : "warehouse.deactivated",
            $"{(request.IsActive ? "เปิด" : "ปิด")}ใช้งานคลัง {entity.Code}", Now), ct);
        await repository.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    private async Task<Result<T>?> SaveAsync<T>(CancellationToken ct)
    {
        try { await repository.SaveChangesAsync(ct); return null; }
        catch (MasterDataConflictException ex)
        {
            return Result<T>.Fail(MasterDataSupport.ConflictCode(ex, "WAREHOUSE_CODE_DUPLICATE"), "รหัสคลังนี้มีอยู่แล้ว");
        }
    }

    private static WarehouseDto Map(Warehouse x) => new(x.Id, x.Code, x.Name, x.LegacyShardKey,
        x.LegacyBranchId, x.Address, x.IsActive, x.CreatedDate, x.LastUpdated);
    private static WarehouseUpsertRequest Normalize(WarehouseUpsertRequest x) => x with
    { Code = MasterDataSupport.Code(x.Code), Name = x.Name.Trim(), Address = MasterDataSupport.Clean(x.Address) };
    private static ApiError? Validate(WarehouseUpsertRequest x) =>
        MasterDataSupport.Required(x.Code, 30, "WAREHOUSE", "รหัสคลัง", "code") ??
        MasterDataSupport.Required(x.Name, 200, "WAREHOUSE", "ชื่อคลัง", "name") ??
        MasterDataSupport.Optional(x.Address, 500, "WAREHOUSE", "ที่อยู่", "address");
}
