namespace AMD.AutoService.GaragePro.Domain.Entities;

public class CatalogItemSupplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CatalogItemId { get; set; }
    public CatalogItem? CatalogItem { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string? SupplierItemCode { get; set; }
    public decimal? SupplierCost { get; set; }
    public int? LeadTimeDays { get; set; }
    public int? MinOrderQty { get; set; }
    public bool IsPreferred { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
