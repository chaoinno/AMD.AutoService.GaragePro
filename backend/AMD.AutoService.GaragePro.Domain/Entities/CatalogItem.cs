using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// แคตตาล็อกอะไหล่และค่าแรงของงาน service
/// สร้างใหม่ทั้งหมด — Gpscode ของ legacy เป็นอะไหล่ตัวถัง ใช้แทนไม่ได้ (docs/05 §2)
/// </summary>
public class CatalogItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public LineType Type { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>รุ่นรถที่ใช้ได้ / มาตรฐานชั่วโมงสำหรับค่าแรง</summary>
    public string? Compatibility { get; set; }
    public string Unit { get; set; } = string.Empty;

    public decimal Cost { get; set; }
    public decimal Price { get; set; }
    /// <summary>ชั่วโมงมาตรฐาน (เฉพาะค่าแรง)</summary>
    public decimal? StandardHours { get; set; }

    // ---- สต็อก: [BIZ] Available = OnHand − Reserved (ไม่รวม OnOrder, ไม่รวม Damaged) ----
    public int OnHand { get; set; }
    public int Reserved { get; set; }
    public int OnOrder { get; set; }
    public int Damaged { get; set; }
    public string? EtaNote { get; set; }

    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }

    public bool IsActive { get; set; } = true;
    public bool StockManaged { get; set; }
    public bool PurchasingLocked { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public Guid? CategoryId { get; set; }
    public CatalogCategory? Category { get; set; }
    public Guid? WarehouseId { get; set; }
    public Warehouse? Warehouse { get; set; }
    public ICollection<CatalogItemSupplier> Suppliers { get; set; } = [];

    public int Available => OnHand - Reserved;
}
