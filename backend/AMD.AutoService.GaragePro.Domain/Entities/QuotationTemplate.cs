using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// เทมเพลตใบเสนอราคา — ชุดรายการมาตรฐาน (เช่น "เช็คระยะ 10,000 กม.") ที่ผู้จัดการเตรียมไว้ล่วงหน้า
/// เพื่อเพิ่มลงใบเสนอราคาได้หลายบรรทัดในคลิกเดียว แทนการคีย์รายการเดิมซ้ำทุกครั้ง
/// [BIZ] สร้าง/แก้ไข = ผู้จัดการเท่านั้น (ตาม CatalogService.EnsureCanManage) · นำไปใช้ = ทุกคนที่แก้ใบเสนอราคาได้
/// อ้างอิง: docs/08-quotation-template.md
/// </summary>
public class QuotationTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    public ICollection<QuotationTemplateLine> Lines { get; set; } = [];
}

/// <summary>
/// บรรทัดในเทมเพลต — มิเรอร์ฟิลด์ของ UpsertLineRequest ตรงๆ เพื่อให้ตอนนำไปใช้เป็นการแปลงตรงไปตรงมา
/// ไม่มี AssignedTechnicianId โดยตั้งใจ — ช่างที่ตั้งไว้ล่วงหน้าอาจลาออกไปแล้ว และการฝังไว้จะไปกลบ
/// gate QUOTE_LINE_NO_TECHNICIAN ที่ QuotationValidator บังคับอยู่แล้วด้วยข้อมูลเก่า
/// </summary>
public class QuotationTemplateLine
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationTemplateId { get; set; }

    public int Sequence { get; set; }

    /// <summary>ว่าง = รายการนอกแคตตาล็อก (sentinel เดียวกับ QuotationLine.CatalogCode)</summary>
    public string CatalogCode { get; set; } = string.Empty;

    /// <summary>สำหรับบรรทัดแคตตาล็อก: เก็บไว้แสดงผล/พรีวิวเท่านั้น — "ห้ามอ่านตอนนำไปใช้จริง" เพราะราคา/ชื่อ
    /// ต้องอ่านสดจากแคตตาล็อกเสมอ (กันชื่อ/ราคาเก่าค้าง) · สำหรับบรรทัดนอกแคตตาล็อก: เป็นค่าจริงที่ใช้ตรงๆ</summary>
    public string Name { get; set; } = string.Empty;
    public LineType Type { get; set; }
    public string? Unit { get; set; }

    public decimal Quantity { get; set; } = 1;

    /// <summary>null = ใช้ราคาสดจากแคตตาล็อก (เฉพาะบรรทัดแคตตาล็อก) · มีค่า = override ราคา ·
    /// บรรทัดนอกแคตตาล็อกต้องมีค่าเสมอ (บังคับตอน validate)</summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>ต้นทุน — เฉพาะบรรทัดนอกแคตตาล็อก (บรรทัดแคตตาล็อกอ่านต้นทุนสดจากแคตตาล็อกเสมอ)</summary>
    public decimal? UnitCost { get; set; }

    /// <summary>ชั่วโมงมาตรฐาน — เฉพาะบรรทัดนอกแคตตาล็อกประเภทค่าแรง</summary>
    public decimal? StandardHours { get; set; }

    public decimal DiscountPercent { get; set; }
    public PromotionKind Promotion { get; set; } = PromotionKind.None;
    public LineSource Source { get; set; } = LineSource.Customer;
    public string? Note { get; set; }

    public QuotationTemplate? QuotationTemplate { get; set; }
}
