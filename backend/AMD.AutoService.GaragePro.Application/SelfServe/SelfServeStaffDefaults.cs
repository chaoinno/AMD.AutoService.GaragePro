namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// id ของตารางอ้างอิงพนักงานที่ shard self-serve ใช้ตอนสร้างพนักงานเอง (เจ้าของอู่ตอนเปิดอู่ · พนักงานที่เจ้าของเพิ่ม)
/// · ยืนยันจาก db2 จริง 2026-10-14 — shard sp1 คัดลอกตารางเหล่านี้มาโดยคง Id เดิม (deploy/selfserve/05-copy-reference-rows.sql)
///   ค่าจึงตรงกันทั้งสอง shard และย้ายพนักงานกลับ db2 (S5) ได้โดยไม่ต้องแปลง id
/// · ตำแหน่ง/หน่วยงานเลือกให้ RoleMapper (fallback เมื่อไม่มี svc_UserRoleOverride) ได้บทบาทเดียวกับที่ตั้งใจ —
///   แต่ตัวตัดสินจริงของ sp1 คือ override เสมอ (ไม่พึ่งชื่อภาษาไทยในตาราง)
/// · B5 มีตารางจับคู่บทบาทของตัวเองที่ Application/Staff/SelfServeStaffRoles.cs (ค่าเดียวกัน) — แก้ค่าที่ใดต้องแก้อีกที่ด้วย
/// </summary>
public static class SelfServeStaffDefaults
{
    /// <summary>Gender(3, 'ไม่ระบุ') — มีเฉพาะใน sp1 (Garage มีแค่ 1/2) · ฟอร์มสมัคร/เพิ่มพนักงานไม่ถามเพศ</summary>
    public const int GenderUnspecifiedId = 3;

    /// <summary>Position 3 = ผู้จัดการ → RoleMapper ได้ Manager</summary>
    public const int PositionManagerId = 3;

    /// <summary>Position 5 = พนักงาน</summary>
    public const int PositionStaffId = 5;

    /// <summary>Position 4 = หัวหน้าแผนก (+ หน่วยงานในฝ่ายโรงงาน → RoleMapper ได้ Lead)</summary>
    public const int PositionLeadId = 4;

    /// <summary>Sector 34 = ผู้จัดการ (ฝ่ายบริหาร)</summary>
    public const int SectorManagerId = 34;

    /// <summary>Sector 76 = งานซ่อมทั่วไป (ฝ่ายโรงงาน → RoleMapper ได้ Technician)</summary>
    public const int SectorGeneralRepairId = 76;
}
