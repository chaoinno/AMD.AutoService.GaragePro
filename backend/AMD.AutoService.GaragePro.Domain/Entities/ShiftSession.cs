using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// การเข้ากะของพนักงาน — [BIZ] ค่าที่เลือกกำหนดคิวงานและคลังทั้งวัน
/// [BIZ] ช่างไม่มีสิทธิ์ปิดกะ (docs/01-workflow.md §4)
/// </summary>
public class ShiftSession
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }
    public long LegacyUserId { get; set; }
    public long? LegacyStaffId { get; set; }

    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public UserRole Role { get; set; }

    public Guid ShiftId { get; set; }
    public string ShiftName { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;

    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAt { get; set; }
    public long? ClosedByUserId { get; set; }

    public EventSource OpenedFrom { get; set; }

    public bool IsOpen => ClosedAt is null;
}
