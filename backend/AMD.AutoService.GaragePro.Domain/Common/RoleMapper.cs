using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Common;

/// <summary>
/// เดาบทบาทในระบบใหม่จากโครงองค์กรของ Garage DB เดิม
///
/// [ASSUME] ทั้งชุดนี้เป็นการเดา ต้องให้แต่ละอู่ยืนยัน — ถ้าไม่ตรงให้ใช้ svc_UserRoleOverride
/// แก้รายคนได้โดยไม่ต้องแก้โค้ด
///
/// โครงเดิมที่ใช้ตัดสิน (ตรวจกับ db2 แล้ว 2026-08-19):
///   Department: ฝ่ายบริหาร · ฝ่ายฟรอนท์ · ฝ่ายโรงงาน · ฝ่ายแบคออฟฟิต · ฝ่ายอะไหล่
///   Position:   ผู้บริหาร · ผู้จัดการ · หัวหน้าแผนก · พนักงาน · นักศึกษา · ที่ปรึกษา
///   Sector:     รับรถ · ประเมินราคา · เคาะ · พ่นสี · QC · อะไหล่ · บัญชี · สารบรรณ ...
/// </summary>
public static class RoleMapper
{
    /// <summary>
    /// ลำดับการตัดสิน: override (จัดการนอกฟังก์ชันนี้) → IsAdministrator → ตำแหน่ง → แผนก/หน่วยงาน → ค่าเริ่มต้น
    /// </summary>
    public static UserRole Resolve(
        bool isAdministrator,
        string? positionName,
        string? sectorName,
        string? departmentName)
    {
        if (isAdministrator) return UserRole.Manager;

        var position = Normalize(positionName);
        var sector = Normalize(sectorName);
        var department = Normalize(departmentName);

        // ตำแหน่งระดับบริหารมีสิทธิ์เต็มของสาขาเสมอ ไม่ว่าอยู่แผนกไหน
        if (position is "ผู้บริหาร" or "ผู้จัดการ")
            return UserRole.Manager;

        // หัวหน้าแผนกในฝ่ายโรงงาน = หัวหน้าช่าง (เห็นรายงานแบบไม่มีตัวเงิน)
        if (position == "หัวหน้าแผนก" && department == "ฝ่ายโรงงาน")
            return UserRole.Lead;

        // QC และงานหน้าโรงซ่อมทั้งหมดเป็นช่าง
        if (sector.Contains("qc", StringComparison.OrdinalIgnoreCase))
            return UserRole.Technician;

        if (department == "ฝ่ายโรงงาน")
            return UserRole.Technician;

        // ฝ่ายฟรอนท์: รับรถ = หน้าร้าน · ประเมินราคา/อะไหล่ = ธุรการ
        if (department == "ฝ่ายฟรอนท์")
        {
            if (sector.Contains("รับรถ")) return UserRole.FrontDesk;
            if (sector.Contains("ประเมินราคา") || sector.Contains("อะไหล่")) return UserRole.Office;
            return UserRole.FrontDesk;
        }

        // การเงิน/บัญชี = แคชเชียร์ · สารบรรณและอื่นๆ ในแบคออฟฟิศ = ธุรการ
        if (department == "ฝ่ายแบคออฟฟิต")
        {
            if (sector.Contains("บัญชี") || sector.Contains("การเงิน")) return UserRole.Cashier;
            return UserRole.Office;
        }

        if (department == "ฝ่ายอะไหล่" || sector.Contains("อะไหล่"))
            return UserRole.Office;

        if (department == "ฝ่ายบริหาร")
            return UserRole.Manager;

        // ไม่รู้จัก → ให้สิทธิ์ต่ำสุดที่ยังทำงานได้ ดีกว่าเดาสูงเกิน
        return UserRole.FrontDesk;
    }

    public static string DescribeTh(UserRole role) => role switch
    {
        UserRole.FrontDesk => "พนักงานหน้าร้าน",
        UserRole.Technician => "ช่างเทคนิค",
        UserRole.Office => "ธุรการ / จัดซื้อ",
        UserRole.Cashier => "แคชเชียร์",
        UserRole.Manager => "ผู้จัดการสาขา",
        UserRole.Lead => "หัวหน้าช่าง",
        _ => role.ToString()
    };

    /// <summary>[BIZ] ช่างไม่มีสิทธิ์ปิดกะ (docs/01-workflow.md §4)</summary>
    public static bool CanCloseShift(UserRole role) =>
        role is not (UserRole.Technician or UserRole.Lead);

    private static string Normalize(string? value) => (value ?? string.Empty).Trim();
}
