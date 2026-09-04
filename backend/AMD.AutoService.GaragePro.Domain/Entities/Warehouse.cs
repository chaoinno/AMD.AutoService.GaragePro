namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>คลังหลัก/ตำแหน่งเก็บเริ่มต้นเท่านั้น จำนวนสต็อกยังอยู่ที่ CatalogItem</summary>
public class Warehouse
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? LegacyShardKey { get; set; }
    public int? LegacyBranchId { get; set; }
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public ICollection<CatalogItem> CatalogItems { get; set; } = [];
}
