using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Staff;

/// <summary>
/// ค่าเริ่มต้นของพนักงานที่เจ้าของอู่ (sp1) เพิ่มจากฟอร์มแบบง่าย (ชื่อ/นามสกุล/เบอร์/บทบาท) — แผน §3 "Staff (sp1)"
/// · id ทั้งหมดเป็นของ Garage db2 (ตรวจแล้ว 2026-10-14) — shard sp1 คัดลอกแถวอ้างอิงมาด้วย id เดิม
///   (deploy/selfserve/05-copy-reference-rows.sql) ยกเว้น Gender 3 "ไม่ระบุ" ที่มีเฉพาะใน sp1
/// · ค่าที่ใช้ร่วมกับการเปิดอู่ (เพศ · ตำแหน่ง · หน่วยงานผู้จัดการ/งานซ่อมทั่วไป) อ้างจาก <see cref="SelfServeStaffDefaults"/> ของ S2
///   ที่เดียว — ที่นี่เพิ่มเฉพาะหน่วยงานของบทบาทอื่น
/// · [BIZ] เลือก ตำแหน่ง/หน่วยงาน ให้ RoleMapper (ทางสำรองเมื่อไม่มีแถว svc_UserRoleOverride) ได้บทบาทเดียวกับที่เลือกเสมอ:
///   Manager = ผู้จัดการ · Lead = หัวหน้าแผนก + ฝ่ายโรงงาน · Technician = ฝ่ายโรงงาน · FrontDesk = ฝ่ายฟรอนท์/รับรถ ·
///   Office = ฝ่ายแบคออฟฟิต/ธุรการ · Cashier = ฝ่ายแบคออฟฟิต/บัญชี
/// </summary>
public static class SelfServeStaffRoles
{
    public const int GenderUnspecifiedId = SelfServeStaffDefaults.GenderUnspecifiedId;

    public const int SectorRepairControlId = 66;  // ควบคุมงานซ่อม · ฝ่ายโรงงาน (หัวหน้าช่าง)
    public const int SectorReceptionId = 13;      // รับรถ · ฝ่ายฟรอนท์
    public const int SectorOfficeId = 61;         // ธุรการ · ฝ่ายแบคออฟฟิต
    public const int SectorAccountingId = 22;     // บัญชี · ฝ่ายแบคออฟฟิต

    /// <summary>ตำแหน่ง + แผนกหลักของบทบาท</summary>
    public static (int PositionId, int SectorId) For(UserRole role) => role switch
    {
        UserRole.Manager => (SelfServeStaffDefaults.PositionManagerId, SelfServeStaffDefaults.SectorManagerId),
        UserRole.Lead => (SelfServeStaffDefaults.PositionLeadId, SectorRepairControlId),
        UserRole.Technician => (SelfServeStaffDefaults.PositionStaffId, SelfServeStaffDefaults.SectorGeneralRepairId),
        UserRole.FrontDesk => (SelfServeStaffDefaults.PositionStaffId, SectorReceptionId),
        UserRole.Office => (SelfServeStaffDefaults.PositionStaffId, SectorOfficeId),
        UserRole.Cashier => (SelfServeStaffDefaults.PositionStaffId, SectorAccountingId),
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "ไม่รู้จักบทบาท")
    };

    /// <summary>
    /// prefix ของชื่อผู้ใช้: <c>SP{WorkspaceNo:D4}.T</c> สำหรับงานช่าง (ช่าง/หัวหน้าช่าง) · <c>.S</c> สำหรับบทบาทอื่น
    /// (เจ้าของอู่ = <c>.M01</c> ตั้งโดย S2 ตอนเปิดอู่) — ต้องตรงกับ <c>LoginIdentityResolver</c> ไม่งั้น login ไป db2
    /// </summary>
    public static string UserNamePrefix(int workspaceNo, UserRole role) =>
        $"SP{workspaceNo:D4}.{(role is UserRole.Technician or UserRole.Lead ? 'T' : 'S')}";

    /// <summary>เลขลำดับสองหลักขึ้นไป (T01…T99, T100…) — ยังอยู่ในรูปแบบที่ LoginIdentityResolver รับ (≤10 ตัวหลังจุด)</summary>
    public static string UserName(int workspaceNo, UserRole role, int sequence) =>
        $"{UserNamePrefix(workspaceNo, role)}{sequence:D2}";
}
