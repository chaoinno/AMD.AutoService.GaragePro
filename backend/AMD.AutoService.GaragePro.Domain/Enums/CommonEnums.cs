namespace AMD.AutoService.GaragePro.Domain.Enums;

/// <summary>แหล่งที่มาของเหตุการณ์ — [BIZ] บังคับทุก ActivityEvent</summary>
public enum EventSource
{
    Mobile = 1,
    Web,
    System
}

/// <summary>บทบาทผู้ใช้ — docs/01-workflow.md §4</summary>
public enum UserRole
{
    FrontDesk = 1,  // พนักงานหน้าร้าน
    Technician,     // ช่างเทคนิค
    Office,         // ธุรการ / จัดซื้อ
    Cashier,        // แคชเชียร์
    Manager,        // ผู้จัดการสาขา
    Lead            // หัวหน้าช่าง (รายงานแบบไม่มีตัวเงิน)
}
