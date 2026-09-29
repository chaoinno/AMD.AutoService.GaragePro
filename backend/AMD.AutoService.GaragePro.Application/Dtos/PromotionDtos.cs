using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record PromotionDto(
    Guid Id, string Code, string Name, string Kind, decimal Value, decimal? MaxAmount, string Scope,
    decimal? MinSubtotal, DateTime? StartsAt, DateTime? EndsAt, bool IsActive, DateTime CreatedAt, DateTime UpdatedAt);

public sealed record PromotionUpsertRequest(
    string Code, string Name, PromotionValueKind Kind, decimal Value, decimal? MaxAmount, PromotionScope Scope,
    decimal? MinSubtotal, DateTime? StartsAt, DateTime? EndsAt);

public sealed record PromotionStatusRequest(bool IsActive);

public static class PromotionMapper
{
    public static PromotionDto ToDto(Domain.Entities.Promotion p) => new(
        p.Id, p.Code, p.Name, p.Kind.ToString().ToLowerInvariant(), p.Value, p.MaxAmount,
        p.Scope.ToString().ToLowerInvariant(), p.MinSubtotal, p.StartsAt, p.EndsAt, p.IsActive, p.CreatedAt, p.UpdatedAt);
}