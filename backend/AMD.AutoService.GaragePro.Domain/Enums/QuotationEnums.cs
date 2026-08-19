namespace AMD.AutoService.GaragePro.Domain.Enums;

/// <summary>สถานะใบเสนอราคา — docs/01-workflow.md §3.3</summary>
public enum QuotationStatus
{
    Draft = 1,      // draft       ฉบับร่าง
    Sent,           // sent        ส่งให้ลูกค้าแล้ว
    Partial,        // partial     อนุมัติบางส่วน
    Approved,       // approved    อนุมัติครบ
    Rejected,       // rejected    ลูกค้าไม่อนุมัติ
    Superseded,     // superseded  ถูกแทนที่
    Expired         // expired     หมดอายุ
}

/// <summary>ผลการตัดสินใจของลูกค้าต่อบรรทัด</summary>
public enum LineApprovalStatus
{
    Pending = 1,    // รออนุมัติ
    Approved,       // อนุมัติ
    Rejected        // ไม่อนุมัติ
}

/// <summary>ที่มาของรายการ — แยกกลุ่มบนหน้าอนุมัติของลูกค้า</summary>
public enum LineSource
{
    Customer = 1,   // ลูกค้าขอ
    Technician      // ช่างแนะนำ
}

/// <summary>ประเภทรายการ</summary>
public enum LineType
{
    Part = 1,       // อะไหล่
    Labor           // ค่าแรง
}
