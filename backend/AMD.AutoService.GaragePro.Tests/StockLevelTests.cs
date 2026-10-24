using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class StockLevelTests
{
    [Fact]
    public void Unset_min_never_warns_even_when_out_of_stock()
    {
        Assert.False(StockLevel.IsBelowMin(0, 0, null));
        Assert.False(StockLevel.IsBelowMin(new CatalogItem { Type = LineType.Part, OnHand = 0 }));
    }

    [Theory]
    [InlineData(6, 0, 5, false)]
    [InlineData(5, 0, 5, true)]   // เท่ากับ min = ถึงจุดต่ำสุด
    [InlineData(4, 0, 5, true)]
    [InlineData(0, 0, 0, true)]   // min 0 เตือนเมื่อหมด
    public void Below_min_when_available_is_at_or_under_min(int onHand, int reserved, int min, bool expected) =>
        Assert.Equal(expected, StockLevel.IsBelowMin(onHand, reserved, min));

    [Fact]
    public void Reserved_stock_pushes_item_to_min()
    {
        // คงเหลือ 8 แต่ถูกจอง 4 → พร้อมใช้ 4 ≤ 5
        Assert.False(StockLevel.IsBelowMin(8, 0, 5));
        Assert.True(StockLevel.IsBelowMin(8, 4, 5));
    }

    [Fact]
    public void Damaged_and_on_order_do_not_count_as_available()
    {
        var item = new CatalogItem { Type = LineType.Part, OnHand = 3, Reserved = 0, OnOrder = 100, Damaged = 50, MinStock = 5 };
        Assert.True(StockLevel.IsBelowMin(item));
    }

    [Fact]
    public void Suggested_order_subtracts_available_and_on_order()
    {
        Assert.Equal(7, StockLevel.SuggestedOrderQty(onHand: 3, reserved: 0, onOrder: 0, maxStock: 10));
        Assert.Equal(2, StockLevel.SuggestedOrderQty(onHand: 3, reserved: 0, onOrder: 5, maxStock: 10));
        Assert.Equal(9, StockLevel.SuggestedOrderQty(onHand: 3, reserved: 2, onOrder: 0, maxStock: 10));
    }

    [Fact]
    public void Suggested_order_is_never_negative_and_null_without_max()
    {
        Assert.Equal(0, StockLevel.SuggestedOrderQty(onHand: 20, reserved: 0, onOrder: 0, maxStock: 10));
        Assert.Equal(0, StockLevel.SuggestedOrderQty(onHand: 3, reserved: 0, onOrder: 50, maxStock: 10));
        Assert.Null(StockLevel.SuggestedOrderQty(onHand: 3, reserved: 0, onOrder: 0, maxStock: null));
    }

    [Fact]
    public void Expression_matches_the_in_memory_rule()
    {
        var predicate = StockLevel.BelowMin.Compile();
        var items = new[]
        {
            new CatalogItem { Type = LineType.Part, OnHand = 5, MinStock = 5 },
            new CatalogItem { Type = LineType.Part, OnHand = 6, MinStock = 5 },
            new CatalogItem { Type = LineType.Part, OnHand = 0, MinStock = null },
            new CatalogItem { Type = LineType.Part, OnHand = 8, Reserved = 4, MinStock = 5 },
            new CatalogItem { Type = LineType.Labor, OnHand = 0, MinStock = 5 },
        };
        foreach (var item in items)
            Assert.Equal(StockLevel.IsBelowMin(item), predicate(item));
    }
}
