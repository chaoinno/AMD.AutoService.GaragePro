using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Common;

public sealed record SalePromotion(PromotionValueKind Kind, decimal Value, decimal? MaxAmount = null, decimal? MinSubtotal = null);

public sealed record SaleCalculationLine(
    decimal UnitPrice,
    int Quantity,
    decimal DiscountPercent,
    SalePromotion? Promotion,
    decimal GrossAmount,
    decimal DiscountAmount,
    decimal PromotionAmount,
    decimal NetAmount);

public sealed record SaleCalculation(
    IReadOnlyList<SaleCalculationLine> Lines,
    decimal GrossAmount,
    decimal LineDiscountAmount,
    decimal LinePromotionAmount,
    decimal SubtotalAmount,
    decimal BillDiscountAmount,
    decimal BillPromotionAmount,
    decimal NetAmount,
    decimal VatRate,
    decimal VatAmount,
    decimal TotalAmount);

public static class SaleCalculator
{
    public const decimal DefaultVatRate = 0.07m;

    public static SaleCalculationLine CalculateLine(decimal unitPrice, int quantity, decimal discountPercent, SalePromotion? promotion)
    {
        var gross = Round(unitPrice * quantity);
        var discount = Round(gross * discountPercent / 100m);
        var promotionBase = Math.Max(0m, gross - discount);
        var promo = PromotionAmount(promotionBase, promotion);
        return new(unitPrice, quantity, discountPercent, promotion, gross, discount, promo, Round(Math.Max(0m, promotionBase - promo)));
    }

    public static SaleCalculation Calculate(
        IEnumerable<(decimal UnitPrice, int Quantity, decimal DiscountPercent, SalePromotion? Promotion)> lines,
        BillDiscountType billDiscountType = BillDiscountType.None,
        decimal billDiscountValue = 0m,
        SalePromotion? billPromotion = null,
        bool vatIncluded = true,
        decimal vatRate = DefaultVatRate)
    {
        var calculatedLines = lines.Select(x => CalculateLine(x.UnitPrice, x.Quantity, x.DiscountPercent, x.Promotion)).ToList();
        var gross = Round(calculatedLines.Sum(x => x.GrossAmount));
        var lineDiscount = Round(calculatedLines.Sum(x => x.DiscountAmount));
        var linePromotion = Round(calculatedLines.Sum(x => x.PromotionAmount));
        var subtotal = Round(calculatedLines.Sum(x => x.NetAmount));
        var billDiscount = billDiscountType switch
        {
            BillDiscountType.Percent => Round(subtotal * billDiscountValue / 100m),
            BillDiscountType.Amount => Round(billDiscountValue),
            _ => 0m
        };
        billDiscount = Math.Min(Math.Max(0m, billDiscount), subtotal);
        var billBase = Math.Max(0m, subtotal - billDiscount);
        var billPromotionAmount = billPromotion is not null && (!billPromotion.MinSubtotal.HasValue || billBase >= billPromotion.MinSubtotal.Value)
            ? PromotionAmount(billBase, billPromotion)
            : 0m;
        var net = Round(Math.Max(0m, billBase - billPromotionAmount));
        var vat = vatIncluded ? Round(net * vatRate) : 0m;
        return new(calculatedLines, gross, lineDiscount, linePromotion, subtotal, billDiscount, billPromotionAmount, net, vatRate, vat, Round(net + vat));
    }

    private static decimal PromotionAmount(decimal baseAmount, SalePromotion? promotion)
    {
        if (promotion is null) return 0m;
        var amount = promotion.Kind == PromotionValueKind.Percent
            ? Round(baseAmount * promotion.Value / 100m)
            : Round(promotion.Value);
        if (promotion.MaxAmount.HasValue) amount = Math.Min(amount, Math.Max(0m, promotion.MaxAmount.Value));
        return Math.Min(Math.Max(0m, amount), baseAmount);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}