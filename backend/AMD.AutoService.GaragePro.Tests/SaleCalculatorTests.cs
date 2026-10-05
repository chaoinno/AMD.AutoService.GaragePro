using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

public class SaleCalculatorTests
{
    [Fact]
    public void Calculates_line_discount_then_percent_promotion()
    {
        var result = SaleCalculator.CalculateLine(100m, 3, 10m, new SalePromotion(PromotionValueKind.Percent, 10m));
        Assert.Equal(300m, result.GrossAmount);
        Assert.Equal(30m, result.DiscountAmount);
        Assert.Equal(27m, result.PromotionAmount);
        Assert.Equal(243m, result.NetAmount);
    }

    [Fact]
    public void Calculates_bill_discount_promotion_and_vat()
    {
        var result = SaleCalculator.Calculate(
            [(100m, 2, 0m, (SalePromotion?)null)],
            BillDiscountType.Amount, 10m,
            new SalePromotion(PromotionValueKind.Percent, 10m, MinSubtotal: 150m));
        Assert.Equal(200m, result.SubtotalAmount);
        Assert.Equal(10m, result.BillDiscountAmount);
        Assert.Equal(19m, result.BillPromotionAmount);
        Assert.Equal(171m, result.NetAmount);
        Assert.Equal(11.97m, result.VatAmount);
        Assert.Equal(182.97m, result.TotalAmount);
    }

    [Fact]
    public void Does_not_apply_bill_promotion_below_minimum()
    {
        var result = SaleCalculator.Calculate(
            [(100m, 1, 0m, (SalePromotion?)null)],
            billPromotion: new SalePromotion(PromotionValueKind.Amount, 20m, MinSubtotal: 200m));
        Assert.Equal(100m, result.NetAmount);
        Assert.Equal(0m, result.BillPromotionAmount);
    }

    [Fact]
    public void Vat_can_be_excluded()
    {
        var result = SaleCalculator.Calculate([(99.995m, 1, 0m, (SalePromotion?)null)], vatIncluded: false);
        Assert.Equal(100m, result.NetAmount);
        Assert.Equal(0m, result.VatAmount);
        Assert.Equal(100m, result.TotalAmount);
    }
}