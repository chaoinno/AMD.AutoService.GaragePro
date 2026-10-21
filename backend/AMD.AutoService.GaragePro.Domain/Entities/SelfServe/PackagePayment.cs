namespace AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

/// <summary>
/// สถานะรายการแจ้งชำระ (sp_PackagePayment.Status) · เก็บเป็นข้อความแบบ enum อื่นของ sp_*
/// · เทียบกับ BranchPaymentHistory ของ GaragePro เดิม: Pending = 1 · Approved = 2 · Cancelled = 0 · Rejected เป็นสถานะใหม่
///   (ระบบเดิมไม่มีการปฏิเสธ) · ไม่มีค่า -1 "ยกเลิกอัตโนมัติ" ของระบบเดิม (docs/17 §3)
/// </summary>
public enum PackagePaymentStatus
{
    Pending,
    Approved,
    Rejected,
    Cancelled
}

/// <summary>เหตุผลปฏิเสธ 5 ตัวเลือกตายตัว (docs/17 §2 ข้อ 8) · ใน API เป็น snake_case ตัวพิมพ์เล็ก (slip_unreadable ฯลฯ)</summary>
public enum PackagePaymentRejectReason
{
    SlipUnreadable,
    AmountMismatch,
    NotReceived,
    Duplicate,
    Other
}

/// <summary>
/// หนึ่งรายการแจ้งชำระค่าแพ็กเกจของอู่ที่สมัครเอง (sp_PackagePayment · docs/17 §3)
/// · [BIZ] รอตรวจได้ครั้งละ 1 รายการต่ออู่ บังคับด้วย filtered unique index (UX_sp_PackagePayment_OnePending) ไม่ใช่แค่ใน service
/// · [BIZ] วันหมดใหม่คำนวณ<b>ตอนอนุมัติ</b> ไม่ใช่ตอนแจ้ง: <see cref="PrevEndsAt"/>/<see cref="PeriodStartsAt"/>/<see cref="PeriodEndsAt"/>
///   ว่างจนกว่าทีมงานอนุมัติ (รายการที่อนุมัติทีหลังจึงต่อจากวันหมดล่าสุดเสมอ)
/// · ไม่มีการลบแถว: ยกเลิก/ปฏิเสธเปลี่ยนสถานะ · purge อู่เปลี่ยนรายการรอตรวจเป็น Cancelled และล้าง <see cref="Note"/>
/// · เวลาทั้งหมดเป็น UTC (sp_* เก็บ UTC)
/// </summary>
public class PackagePayment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }

    /// <summary>idempotency key จาก client (≤ 100 ตัว) · unique ร่วมกับ <see cref="WorkspaceId"/> · ส่งซ้ำได้แถวเดิม</summary>
    public string RequestId { get; set; } = string.Empty;

    public PackagePaymentStatus Status { get; set; } = PackagePaymentStatus.Pending;

    public int MonthCount { get; set; }

    /// <summary>ยอดที่ลูกค้าแจ้งว่าโอนจริง</summary>
    public decimal Amount { get; set; }

    /// <summary>ราคามาตรฐาน ณ ตอนแจ้ง (สำเนา) · null = ยังไม่ตั้งราคา</summary>
    public decimal? ExpectedAmount { get; set; }

    /// <summary>วันเวลาที่โอนตามที่ลูกค้าแจ้ง (UTC)</summary>
    public DateTime PaidAt { get; set; }

    /// <summary>relative path บน FTP (root เดียวกับไฟล์แนบ) · ขึ้นต้นด้วย {shard}/{branch}/package-slip/ เสมอ</summary>
    public string SlipPath { get; set; } = string.Empty;

    /// <summary>SHA-256 (hex ตัวเล็ก 64 ตัว) ของ<b>ไฟล์ต้นฉบับที่อัปโหลด</b> ก่อนแปลงเป็น JPEG · ไว้เตือนสลิปซ้ำ</summary>
    public string SlipSha256 { get; set; } = string.Empty;

    /// <summary>ชนิดของไฟล์ที่เก็บจริง (image/jpeg หรือ application/pdf)</summary>
    public string SlipContentType { get; set; } = string.Empty;

    public long SlipSizeBytes { get; set; }

    /// <summary>หมายเหตุจากลูกค้า (≤ 500)</summary>
    public string? Note { get; set; }

    /// <summary>[User].Id ของผู้แจ้งจาก JWT · ห้ามรับจาก body</summary>
    public long SubmittedByUserId { get; set; }

    public string SubmittedByUserName { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; }

    /// <summary>วันหมดเดิม ณ ตอนอนุมัติ (UTC) · null = ยังไม่อนุมัติ หรือเดิมไม่มีวันหมด</summary>
    public DateTime? PrevEndsAt { get; set; }

    /// <summary>ช่วงที่ต่อให้จริง (เติมตอนอนุมัติ) · <see cref="PeriodEndsAt"/> = EndPackageDate ใหม่ (เที่ยงคืนไทยของวันแรกที่หมดสิทธิ์)</summary>
    public DateTime? PeriodStartsAt { get; set; }

    public DateTime? PeriodEndsAt { get; set; }

    /// <summary>ผู้อนุมัติ/ปฏิเสธ เช่น "spadmin:noi" หรือ "admin:ชื่อ#id"</summary>
    public string? ReviewedBy { get; set; }

    public DateTime? ReviewedAt { get; set; }

    public PackagePaymentRejectReason? RejectReason { get; set; }

    /// <summary>หมายเหตุของทีมงาน · ลูกค้าเห็นเฉพาะตอนปฏิเสธ (หมายเหตุตอนอนุมัติเป็นของทีมงาน)</summary>
    public string? ReviewNote { get; set; }

    public DateTime? CancelledAt { get; set; }

    /// <summary>ผู้ยกเลิก: ชื่อผู้ใช้ของ Manager หรือผู้กระทำของ purge</summary>
    public string? CancelledBy { get; set; }
}
