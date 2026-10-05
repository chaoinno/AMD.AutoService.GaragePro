using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record SaleDto(
    Guid Id, string Status, Guid WarehouseId, long? LegacyCustomerId, string? CustomerName, string? CustomerPhone,
    bool VatIncluded, string BillDiscountType, decimal BillDiscountValue, Guid? BillPromotionId, string? BillPromotionCode, string? BillPromotionName,
    decimal GrossAmount, decimal LineDiscountAmount, decimal LinePromotionAmount, decimal SubtotalAmount,
    decimal BillDiscountAmount, decimal BillPromotionAmount, decimal NetAmount, decimal VatRate, decimal VatAmount,
    decimal TotalAmount, decimal? CostTotal, string? ReceiptNo, DateTime CreatedAt, DateTime? CompletedAt,
    DateTime? VoidedAt, string? VoidReason, IReadOnlyList<SaleLineDto> Lines, IReadOnlyList<SalePaymentDto> Payments);

public sealed record SaleLineDto(
    Guid Id, Guid CatalogItemId, string Code, string Name, string Unit, int Quantity, decimal UnitPrice,
    decimal DiscountPercent, Guid? PromotionId, string? PromotionCode, string? PromotionName,
    decimal DiscountAmount, decimal PromotionAmount, decimal NetAmount, decimal? CostAmount, int AvailableInWarehouse);

public sealed record SalePaymentDto(Guid Id, string Method, decimal Amount, string? Reference, string ReceivedByName, DateTime ReceivedAt);

public sealed record SaleSearchQuery(string? Status, DateTime? From, DateTime? To, string? Q, int Page = 1, int PageSize = 50);
public sealed record SaleSearchResult(IReadOnlyList<SaleDto> Items, int Total, int Page, int PageSize);

public sealed record CreateSaleRequest(
    Guid WarehouseId, long? LegacyCustomerId, string? CustomerName, string? CustomerPhone, bool VatIncluded = true);

public sealed record UpdateSaleRequest(
    Guid WarehouseId, long? LegacyCustomerId, string? CustomerName, string? CustomerPhone,
    BillDiscountType BillDiscountType, decimal BillDiscountValue, Guid? BillPromotionId, bool VatIncluded);

public sealed record AddSaleLineRequest(Guid CatalogItemId, int Quantity = 1, decimal DiscountPercent = 0m, Guid? PromotionId = null);
public sealed record UpdateSaleLineRequest(int Quantity, decimal DiscountPercent, Guid? PromotionId);
public sealed record CheckoutSaleRequest(Guid RequestId, IReadOnlyList<SalePaymentInput> Payments);
public sealed record SalePaymentInput(string Method, decimal Amount, string? Reference);
public sealed record VoidSaleRequest(Guid RequestId, string Reason);

public static class SaleMapper
{
    public static SaleDto ToDto(Domain.Entities.Sale sale, bool canSeeCost, IReadOnlyDictionary<Guid, int>? available = null) => new(
        sale.Id, sale.Status.ToString().ToLowerInvariant(), sale.WarehouseId, sale.LegacyCustomerId, sale.CustomerName,
        sale.CustomerPhone, sale.VatIncluded, sale.BillDiscountType.ToString().ToLowerInvariant(), sale.BillDiscountValue,
        sale.BillPromotionId, sale.BillPromotionCode, sale.BillPromotionName, sale.GrossAmount, sale.LineDiscountAmount, sale.LinePromotionAmount, sale.SubtotalAmount,
        sale.BillDiscountAmount, sale.BillPromotionAmount, sale.NetAmount, sale.VatRate, sale.VatAmount, sale.TotalAmount,
        canSeeCost ? sale.CostTotal : null, sale.ReceiptNo, sale.CreatedAt, sale.CompletedAt, sale.VoidedAt, sale.VoidReason,
        sale.Lines.OrderBy(x => x.Code).Select(x => new SaleLineDto(
            x.Id, x.CatalogItemId, x.Code, x.Name, x.Unit, x.Quantity, x.UnitPrice, x.DiscountPercent, x.PromotionId,
            x.PromotionCode, x.PromotionName, x.DiscountAmount, x.PromotionAmount, x.NetAmount,
            canSeeCost ? x.CostAmount : null, available?.GetValueOrDefault(x.CatalogItemId) ?? 0)).ToList(),
        sale.Payments.OrderBy(x => x.ReceivedAt).Select(x => new SalePaymentDto(
            x.Id, PosMapper.ToMethodToken(x.Method), x.Amount, x.Reference, x.ReceivedByName, x.ReceivedAt)).ToList());
}