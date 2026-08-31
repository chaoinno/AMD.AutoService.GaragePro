namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// ตัวนับเลขจ๊อบรายวันต่อสาขา — คีย์ผสม (LegacyShardKey, BranchId, CounterDate)
/// ใช้สร้าง JobNo รูปแบบ JB{yyMMdd}{BranchId:D4}{seq:D3} เริ่มนับใหม่ทุกวัน
/// อ้างอิง: Infrastructure/Persistence/JobNumberGenerator.cs
/// </summary>
public class JobNumberCounter
{
    public string LegacyShardKey { get; set; } = "db2";
    public int BranchId { get; set; }
    public DateOnly CounterDate { get; set; }
    public int LastSequence { get; set; }
}
