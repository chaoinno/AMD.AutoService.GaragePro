namespace AMD.AutoService.GaragePro.Domain.Enums;

/// <summary>สถานะบิลขายหน้าร้าน — docs/11-retail-sale-pos.md</summary>
public enum SaleStatus
{
    Draft = 1,
    Completed,
    Voided,
    Cancelled
}

/// <summary>รูปแบบส่วนลดท้ายบิล — Amount หมายถึงลดเป็นบาท</summary>
public enum BillDiscountType
{
    None = 0,
    Percent,
    Amount
}

/// <summary>
/// รูปแบบค่าของโปรโมชัน — docs/11-retail-sale-pos.md
/// [BIZ] แยกจาก enum <see cref="PromotionKind"/> ของใบเสนอราคา (3 แบบ hard-code) ตามคำขอผู้ใช้
/// ที่ให้ผู้จัดการสร้างโปรเองได้ — ห้ามรวมสอง enum นี้เข้าด้วยกัน
/// </summary>
public enum PromotionValueKind
{
    Percent = 1,
    Amount
}

/// <summary>ขอบเขตการใช้โปรโมชัน — Line = ต่อบรรทัด, Bill = ท้ายบิล (ต้องถึง MinSubtotal)</summary>
public enum PromotionScope
{
    Line = 1,
    Bill
}
