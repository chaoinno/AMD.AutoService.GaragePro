using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

public class Sale
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Draft;
    public Guid WarehouseId { get; set; }
    public long? LegacyCustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public bool VatIncluded { get; set; } = true;
    public BillDiscountType BillDiscountType { get; set; }
    public decimal BillDiscountValue { get; set; }
    public Guid? BillPromotionId { get; set; }
    public string? BillPromotionCode { get; set; }
    public string? BillPromotionName { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal LineDiscountAmount { get; set; }
    public decimal LinePromotionAmount { get; set; }
    public decimal SubtotalAmount { get; set; }
    public decimal BillDiscountAmount { get; set; }
    public decimal BillPromotionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal CostTotal { get; set; }
    public string? ReceiptNo { get; set; }
    public Guid? CheckoutRequestId { get; set; }
    public string? CheckoutRequestHash { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? CompletedByUserId { get; set; }
    public string? CompletedByName { get; set; }
    public DateTime? VoidedAt { get; set; }
    public long? VoidedByUserId { get; set; }
    public string? VoidedByName { get; set; }
    public string? VoidReason { get; set; }
    public Guid? VoidRequestId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public long CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = "";
    public byte[] RowVersion { get; set; } = [];

    public ICollection<SaleLine> Lines { get; set; } = [];
    public ICollection<SalePayment> Payments { get; set; } = [];
}

public class SaleLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public Guid CatalogItemId { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public Guid? PromotionId { get; set; }
    public string? PromotionCode { get; set; }
    public string? PromotionName { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal PromotionAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal? CostAmount { get; set; }
    public Sale? Sale { get; set; }
}

public class SalePayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SaleId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public DateTime ReceivedAt { get; set; }
    public long ReceivedByUserId { get; set; }
    public string ReceivedByName { get; set; } = "";
    public Sale? Sale { get; set; }
}

public class SaleReceiptNumberCounter
{
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public int Year { get; set; }
    public int LastSequence { get; set; }
}