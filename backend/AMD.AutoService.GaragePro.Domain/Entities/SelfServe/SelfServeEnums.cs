namespace AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

// enum ของตาราง sp_* ทั้งหมดเก็บเป็น string (ชื่อ enum ตามตัว เช่น "Trial", "Active") ไม่ใช่ตัวเลข
// · อ่านในฐานได้ตรงๆ เวลาไล่ปัญหา/ทำรายงาน · เพิ่มค่าใหม่ท้าย enum ไม่ทำให้ข้อมูลเก่าความหมายเพี้ยน
// · [RISK] โค้ด Dapper ที่เขียนตารางเหล่านี้ต้องส่ง .ToString() เอง — Dapper ส่ง enum เป็น int โดยปริยาย
//   ซึ่งไม่ error ตอนเขียน (คอลัมน์ nvarchar รับ "1" ได้) แต่ EF จะอ่านแถวนั้นกลับไม่ได้

/// <summary>วิธีพิสูจน์ตัวตนของบัญชี (sp_Account.Provider)</summary>
public enum SelfServeAccountProvider
{
    Email,
    Google
}

/// <summary>
/// สถานะบัญชีสมัครเอง (sp_Account.Status)
/// · Pending = ส่ง OTP แล้วยังไม่ยืนยัน · Verified = ยืนยันอีเมล/Google แล้ว ยังไม่ได้เปิดอู่
/// · Active = มี workspace + [User] แล้ว · Suspended = ทีมขายระงับ
/// · Purged = ลบข้อมูลแล้ว — [BIZ] แถวไม่ถูกลบ (คง NormalizedEmail ไว้กันสมัครซ้ำ docs/12 §6)
/// </summary>
public enum SelfServeAccountStatus
{
    Pending,
    Verified,
    Active,
    Suspended,
    Purged
}

/// <summary>
/// แพ็กเกจของ workspace (sp_Workspace.Plan) — คนละชนิดกับ PlanKind ของ Application (นั่นคือผลที่คำนวณแล้ว
/// รวม legacy) · Converted = ย้ายข้อมูลหลักไปเป็นสาขาใน db2 แล้ว (แผน S5) รอลบตาม PurgeAfter
/// </summary>
public enum WorkspacePlanKind
{
    Trial,
    Paid,
    Suspended,
    Converted
}

/// <summary>
/// สถานะวงจรชีวิตของ workspace (sp_Workspace.Status)
/// · Provisioning = กำลังสร้าง/ค้างกลางทาง (เข้าสู่ระบบไม่ได้) · Active = ใช้งานได้
/// · Expired = job รายวันพบว่าเลยวันหมดอายุ (ข้อมูลยังอยู่ อ่านอย่างเดียว) · Purged = ลบข้อมูลแล้ว
/// [BIZ] สิทธิ์อ่าน/เขียนตัดสินจากวันที่ใน Branch (PlanPolicy) ไม่ใช่จาก Expired — Expired เป็นแค่ป้ายของ job
/// </summary>
public enum WorkspaceStatus
{
    Provisioning,
    Active,
    Expired,
    Purged
}
