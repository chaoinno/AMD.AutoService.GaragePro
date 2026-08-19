namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// กะการทำงานของสาขา — Garage DB เดิมไม่มีตารางกะ (มีแค่ TimeAttendance รายคน)
/// จึงเก็บในฐานของระบบใหม่ และผูกกลับด้วย (shard, branch)
/// </summary>
public class Shift
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }

    public string Name { get; set; } = string.Empty;
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    /// <summary>หัวหน้ากะ — อ้าง Staff.Id ของ Garage DB เดิม</summary>
    public long? SupervisorStaffId { get; set; }
    public string? SupervisorName { get; set; }

    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
