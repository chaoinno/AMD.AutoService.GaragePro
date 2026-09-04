namespace AMD.AutoService.GaragePro.Domain.Entities;

public class CatalogCategory
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public Guid? ParentCategoryId { get; set; }
    public CatalogCategory? ParentCategory { get; set; }
    public ICollection<CatalogCategory> Children { get; set; } = [];
    public int? SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public ICollection<CatalogItem> CatalogItems { get; set; } = [];
}
