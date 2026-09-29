using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Promotions;

public interface IPromotionService
{
    Task<Result<IReadOnlyList<PromotionDto>>> SearchAsync(string? keyword, bool includeInactive, CancellationToken ct = default);
    Task<Result<PromotionDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<PromotionDto>> CreateAsync(PromotionUpsertRequest request, CancellationToken ct = default);
    Task<Result<PromotionDto>> UpdateAsync(Guid id, PromotionUpsertRequest request, CancellationToken ct = default);
    Task<Result<bool>> SetStatusAsync(Guid id, PromotionStatusRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<PromotionDto>>> ApplicableAsync(PromotionScope scope, CancellationToken ct = default);
}

public sealed class PromotionService(
    IPromotionRepository repository, ICurrentUser user, TimeProvider clock) : IPromotionService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<IReadOnlyList<PromotionDto>>> SearchAsync(string? keyword, bool includeInactive, CancellationToken ct = default)
    {
        var items = await repository.SearchAsync(MasterDataSupport.Clean(keyword), includeInactive, ct);
        return Result<IReadOnlyList<PromotionDto>>.Ok(items.Select(PromotionMapper.ToDto).ToList());
    }

    public async Task<Result<PromotionDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var item = await repository.GetAsync(id, ct);
        return item is null
            ? Result<PromotionDto>.Fail("PROMOTION_NOT_FOUND", "ไม่พบโปรโมชันนี้ในสาขาปัจจุบัน")
            : Result<PromotionDto>.Ok(PromotionMapper.ToDto(item));
    }

    public async Task<Result<PromotionDto>> CreateAsync(PromotionUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<PromotionDto>.Fail(forbidden);
        var error = Validate(request);
        if (error is not null) return Result<PromotionDto>.Fail(error);
        var normalized = Normalize(request);
        if (await repository.CodeExistsAsync(user.ShardKey, user.BranchId, normalized.Code, null, ct))
            return Result<PromotionDto>.Fail("PROMOTION_CODE_DUPLICATE", "รหัสโปรโมชันนี้มีอยู่แล้ว", "code");

        var entity = Build(normalized, null);
        await repository.AddAsync(entity, ct);
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(Promotion), "promotion.created",
            $"สร้างโปรโมชัน {entity.Code} {entity.Name}", Now), ct);
        var result = await SaveAsync<PromotionDto>(ct);
        return result ?? Result<PromotionDto>.Ok(PromotionMapper.ToDto(entity));
    }

    public async Task<Result<PromotionDto>> UpdateAsync(Guid id, PromotionUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<PromotionDto>.Fail(forbidden);
        var entity = await repository.GetAsync(id, ct);
        if (entity is null) return Result<PromotionDto>.Fail("PROMOTION_NOT_FOUND", "ไม่พบโปรโมชันนี้ในสาขาปัจจุบัน");
        var error = Validate(request);
        if (error is not null) return Result<PromotionDto>.Fail(error);
        var normalized = Normalize(request);
        if (await repository.CodeExistsAsync(user.ShardKey, user.BranchId, normalized.Code, id, ct))
            return Result<PromotionDto>.Fail("PROMOTION_CODE_DUPLICATE", "รหัสโปรโมชันนี้มีอยู่แล้ว", "code");
        Apply(entity, normalized);
        entity.UpdatedAt = Now;
        entity.UpdatedByUserId = user.UserId;
        entity.UpdatedByName = user.UserName;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(Promotion), "promotion.updated",
            $"แก้ไขโปรโมชัน {entity.Code} {entity.Name}", Now), ct);
        var result = await SaveAsync<PromotionDto>(ct);
        return result ?? Result<PromotionDto>.Ok(PromotionMapper.ToDto(entity));
    }

    public async Task<Result<bool>> SetStatusAsync(Guid id, PromotionStatusRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<bool>.Fail(forbidden);
        var entity = await repository.GetAsync(id, ct);
        if (entity is null) return Result<bool>.Fail("PROMOTION_NOT_FOUND", "ไม่พบโปรโมชันนี้ในสาขาปัจจุบัน");
        entity.IsActive = request.IsActive;
        entity.UpdatedAt = Now;
        entity.UpdatedByUserId = user.UserId;
        entity.UpdatedByName = user.UserName;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(Promotion),
            request.IsActive ? "promotion.activated" : "promotion.deactivated",
            $"{(request.IsActive ? "เปิด" : "ปิด")}ใช้งานโปรโมชัน {entity.Code}", Now), ct);
        await repository.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    public async Task<Result<IReadOnlyList<PromotionDto>>> ApplicableAsync(PromotionScope scope, CancellationToken ct = default)
    {
        var now = Now;
        var items = await repository.SearchAsync(null, false, ct);
        var applicable = items.Where(x => x.IsActive && x.Scope == scope &&
            (!x.StartsAt.HasValue || x.StartsAt <= now) && (!x.EndsAt.HasValue || x.EndsAt >= now));
        return Result<IReadOnlyList<PromotionDto>>.Ok(applicable.Select(PromotionMapper.ToDto).ToList());
    }

    private Promotion Build(PromotionUpsertRequest request, Promotion? existing)
    {
        var entity = existing ?? new Promotion { LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId,
            CreatedAt = Now, CreatedByUserId = user.UserId, CreatedByName = user.UserName };
        Apply(entity, request);
        entity.UpdatedAt = Now;
        entity.UpdatedByUserId = user.UserId;
        entity.UpdatedByName = user.UserName;
        return entity;
    }

    private static void Apply(Promotion entity, PromotionUpsertRequest request)
    {
        entity.Code = request.Code;
        entity.Name = request.Name.Trim();
        entity.Kind = request.Kind;
        entity.Value = request.Value;
        entity.MaxAmount = request.MaxAmount;
        entity.Scope = request.Scope;
        entity.MinSubtotal = request.MinSubtotal;
        entity.StartsAt = request.StartsAt;
        entity.EndsAt = request.EndsAt;
    }

    private static PromotionUpsertRequest Normalize(PromotionUpsertRequest request) => request with
    {
        Code = MasterDataSupport.Code(request.Code), Name = request.Name.Trim()
    };

    private static ApiError? Validate(PromotionUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length > 30)
            return new("PROMOTION_CODE_INVALID", "กรุณาระบุรหัสโปรโมชันไม่เกิน 30 ตัวอักษร", "code");
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
            return new("PROMOTION_NAME_INVALID", "กรุณาระบุชื่อโปรโมชันไม่เกิน 200 ตัวอักษร", "name");
        if (request.Value <= 0m || (request.Kind == PromotionValueKind.Percent && request.Value > 100m))
            return new("PROMOTION_VALUE_INVALID", "ค่าของโปรโมชันไม่ถูกต้อง", "value");
        if (request.Scope == PromotionScope.Line && request.MinSubtotal is not null)
            return new("PROMOTION_MIN_SUBTOTAL_INVALID", "โปรโมชันระดับรายการห้ามกำหนดยอดขั้นต่ำ", "minSubtotal");
        if (request.EndsAt.HasValue && request.StartsAt.HasValue && request.EndsAt < request.StartsAt)
            return new("PROMOTION_DATE_INVALID", "วันสิ้นสุดต้องไม่ก่อนวันเริ่มต้น", "endsAt");
        return null;
    }

    private async Task<Result<T>?> SaveAsync<T>(CancellationToken ct)
    {
        try { await repository.SaveChangesAsync(ct); return null; }
        catch (MasterDataConflictException ex)
        { return Result<T>.Fail(MasterDataSupport.ConflictCode(ex, "PROMOTION_CODE_DUPLICATE"), "รหัสโปรโมชันนี้มีอยู่แล้ว"); }
    }
}