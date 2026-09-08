namespace AMD.AutoService.GaragePro.Domain.Entities;

// PR and PO share document/line storage, but have separate lifecycles and API routes.
public class PurchaseDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "PR";
    public string Number { get; set; } = "";
    public string Status { get; set; } = "draft";
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public Guid? SourceRequestId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public Guid WarehouseId { get; set; }
    public string WarehouseName { get; set; } = "";
    public DateTime? RequiredDate { get; set; }
    public string? Note { get; set; }
    public string? PaymentTerms { get; set; }
    public string? CancelReason { get; set; }
    public long CreatedBy { get; set; }
    public string CreatedByName { get; set; } = "";
    public long? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public byte[] RowVersion { get; set; } = [];
    public List<PurchaseLine> Lines { get; set; } = [];
}

public class PurchaseLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    public Guid CatalogItemId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitCost { get; set; }
    public int ReceivedGood { get; set; }
    public int ReceivedDamaged { get; set; }
}

public class GoodsReceipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PurchaseOrderId { get; set; }
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public string Number { get; set; } = "";
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public string DeliveryNumber { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
    public string ReceivedByName { get; set; } = "";
    public List<GoodsReceiptLine> Lines { get; set; } = [];
}

public class GoodsReceiptLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GoodsReceiptId { get; set; }
    public Guid PurchaseLineId { get; set; }
    public int GoodQuantity { get; set; }
    public int DamagedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public string? Note { get; set; }
}

public class StockLot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public Guid CatalogItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? GoodsReceiptLineId { get; set; }
    public string DocumentNumber { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
    public int ReceivedQuantity { get; set; }
    public int RemainingQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public byte[] RowVersion { get; set; } = [];
}

public class StockMovement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public Guid CatalogItemId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? StockLotId { get; set; }
    public Guid OperationId { get; set; }
    public string RequestHash { get; set; } = "";
    public string DocumentNumber { get; set; } = "";
    public string Type { get; set; } = "";
    public int Quantity { get; set; }
    public int DamagedQuantity { get; set; }
    // Before/after are good-stock balances for this item across the current branch.
    public int BalanceBefore { get; set; }
    public int BalanceAfter { get; set; }
    public decimal UnitCost { get; set; }
    public string Reason { get; set; } = "";
    public string PerformedByName { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}

public class PurchaseNumberCounter
{
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public string Kind { get; set; } = "";
    public DateTime Date { get; set; }
    public int Sequence { get; set; }
}
