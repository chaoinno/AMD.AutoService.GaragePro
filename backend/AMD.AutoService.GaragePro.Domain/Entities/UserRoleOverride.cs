using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// แก้บทบาทของผู้ใช้รายคน — ทับค่าที่เดามาจากแผนก/ตำแหน่งใน Garage DB เดิม
///
/// [ASSUME] การ map แผนก→บทบาท เป็นการเดาจากโครงองค์กรเดิม ต้องให้แต่ละอู่ยืนยัน
/// ตารางนี้คือทางออกเมื่อค่าที่เดามาไม่ตรง — ไม่ต้องแก้โค้ด
/// </summary>
public class UserRoleOverride
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }
    public long LegacyUserId { get; set; }

    public UserRole Role { get; set; }

    public string? Note { get; set; }
    public long SetByUserId { get; set; }
    public DateTime SetAt { get; set; } = DateTime.UtcNow;
}
