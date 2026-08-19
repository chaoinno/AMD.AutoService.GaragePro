using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.StateMachine;

/// <summary>
/// หนึ่งเส้นทางการเปลี่ยนสถานะงาน พร้อมกฎและผู้มีสิทธิ์
/// อ้างอิงตรงจาก transition table ใน docs/01-workflow.md §1
/// </summary>
public sealed record JobTransition(
    JobStatus From,
    JobStatus To,
    string RuleTh,
    UserRole[] AllowedRoles,
    EventSource[] AllowedSources,
    JobGuard Guard);

/// <summary>
/// เงื่อนไขที่ต้องเป็นจริงก่อนเปลี่ยนสถานะ
/// ทุกตัวเป็น flag ที่ Application layer คำนวณมาให้ — Domain ไม่แตะ I/O
/// </summary>
[Flags]
public enum JobGuard
{
    None = 0,

    /// <summary>ผลตรวจครบทุกรายการบังคับ และรายการ "ไม่เกี่ยวข้อง" มีเหตุผลครบ</summary>
    InspectionComplete = 1 << 0,

    /// <summary>ใบเสนอราคาผ่าน validate: ทุกบรรทัดมีราคา + ช่าง และไม่มีรายการซ้ำ</summary>
    QuotationValid = 1 << 1,

    /// <summary>ลูกค้าตัดสินใจครบทุกบรรทัด และเซ็นแล้ว (ลายเซ็นผูกกับเวอร์ชันนั้น)</summary>
    AllLinesDecidedAndSigned = 1 << 2,

    /// <summary>มีรายการที่ลูกค้าอนุมัติอย่างน้อยหนึ่งรายการ</summary>
    HasApprovedLines = 1 << 3,

    /// <summary>คำขออะไหล่ระบุเหตุผลและ ETA ครบ</summary>
    PartsRequestComplete = 1 << 4,

    /// <summary>รับของเข้าคลัง (GRN) และเบิกให้งานนี้แล้ว — ของชำรุดไม่นับ</summary>
    PartsReceivedAndIssued = 1 << 5,

    /// <summary>ทุกรายการที่อนุมัติเสร็จ และมีรูปก่อน–หลังครบทุกรายการ</summary>
    AllTasksDoneWithPhotos = 1 << 6,

    /// <summary>QC ผ่านทุกหัวข้อ และผลทดลองขับปกติ</summary>
    QcPassed = 1 << 7,

    /// <summary>QC ตีกลับ พร้อมระบุปัญหาและความสำคัญ</summary>
    QcFailReported = 1 << 8,

    /// <summary>ยอดคงเหลือเป็นศูนย์ หรือมีลูกหนี้ที่ผู้จัดการอนุมัติแล้ว</summary>
    BalanceSettled = 1 << 9,

    /// <summary>ออกเอกสาร (ใบเสร็จ/ใบกำกับ) แล้ว</summary>
    DocumentIssued = 1 << 10,

    /// <summary>ส่งมอบรถและรับลายเซ็นคืนรถแล้ว</summary>
    VehicleHandedOver = 1 << 11,

    /// <summary>มีเหตุผลการยกเลิกและผู้อนุมัติ</summary>
    CancelReasonAndApprover = 1 << 12
}
