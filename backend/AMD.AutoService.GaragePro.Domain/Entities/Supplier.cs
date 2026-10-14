namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// ซัพพลายเออร์ของสาขา — [BIZ] ข้อมูลหลักแยกต่อ (ชาร์ด, สาขา) เหมือนคลัง/สินค้า เดิมเป็นข้อมูลกลางทั้งตาราง
/// ทำให้ทุกอู่เห็น/แก้ซัพพลายเออร์ของกันและกันได้ และรหัสชนกันข้ามอู่ (migration ScopeMasterDataByBranch)
/// </summary>
public class Supplier
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string LegacyShardKey { get; set; } = string.Empty;
    public int LegacyBranchId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? ContactName { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? TaxId { get; set; }
    public string? PaymentTerms { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public ICollection<CatalogItemSupplier> CatalogItems { get; set; } = [];
}
