using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Domain.Common;

public static class PurchasingRules
{
    public static string? NextStatus(string kind, string status, string action) => (kind, status, action) switch
    {
        (_, "draft", "submit") => "pending",
        (_, "pending", "approve") => "approved",
        (_, "pending", "return") => "draft",
        ("PR", "approved", "convert") => "converted",
        ("PO", "approved", "send") => "sent",
        ("PO", "sent" or "partial", "receive") => "partial",
        (_, "draft" or "pending" or "approved", "cancel") => "cancelled",
        ("PO", "sent" or "partial", "cancel") => "cancelled",
        _ => null
    };

    public static int Outstanding(PurchaseLine line) => line.Quantity - line.ReceivedGood - line.ReceivedDamaged;

    // Plan first, mutate only once the entire requested quantity has been validated.
    public static IReadOnlyList<(StockLot Lot, int Quantity)> Allocate(IEnumerable<StockLot> lots, int quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        var remaining = quantity;
        var allocations = new List<(StockLot, int)>();
        foreach (var lot in lots.Where(x => x.RemainingQuantity > 0).OrderBy(x => x.ReceivedAt).ThenBy(x => x.Id))
        {
            var take = Math.Min(remaining, lot.RemainingQuantity);
            allocations.Add((lot, take));
            remaining -= take;
            if (remaining == 0) return allocations;
        }
        throw new InvalidOperationException("จำนวนในล็อต FIFO ไม่เพียงพอ");
    }
}
