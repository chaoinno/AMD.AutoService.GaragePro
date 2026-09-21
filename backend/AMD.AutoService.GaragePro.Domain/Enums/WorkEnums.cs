namespace AMD.AutoService.GaragePro.Domain.Enums;

/// <summary>
/// ชนิดของช่วงเวลา — คิดเวลาทำงานจาก <see cref="Work"/> เท่านั้น ส่วน <see cref="Pause"/> เก็บไว้ดูว่า
/// พักเพราะอะไรนานแค่ไหน ไม่เข้าการคำนวณ (docs/09-technician-time-tracking.md §5.6)
/// </summary>
public enum WorkIntervalKind
{
    Work = 1,
    Pause = 2
}

/// <summary>
/// เหตุผลที่ช่วงเวลาถูกปิด — ครึ่งหนึ่งมาจากการกดของช่างเอง อีกครึ่งมาจากระบบปิดให้ตาม docs/09 §6
/// </summary>
public enum WorkEndReason
{
    /// <summary>ช่างเริ่มจับเวลาคันใหม่ ระบบปิดคันเดิมให้ในคำขอเดียวกัน</summary>
    SwitchedJob = 1,
    Paused = 2,
    Resumed = 3,

    /// <summary>ระบบปิดให้เพราะจ๊อบเปลี่ยนสถานะ (docs/09 §6)</summary>
    WaitParts = 4,
    SentToQc = 5,
    JobClosed = 6,
    ShiftClosed = 7,

    /// <summary>ช่างกดหยุดเอง</summary>
    Manual = 8,

    /// <summary>
    /// ลืมกดหยุด — ระบบตัดให้ที่เวลาสิ้นกะ คาบชนิดนี้ไม่เข้าการคำนวณเมตริกใดๆ
    /// แต่นับเข้า "ความครบของข้อมูล" เป็นฝั่งที่ขาด (docs/09 §5.6)
    /// </summary>
    AutoCapped = 9
}
