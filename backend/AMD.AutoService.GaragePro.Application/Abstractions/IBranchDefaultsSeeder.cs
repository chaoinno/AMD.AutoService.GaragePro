namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// สร้างข้อมูลหลักชุดเริ่มต้นของสาขาที่ยังว่าง (หมวดหมู่ SVC/PART/FLUID · คลัง MAIN — B2) แบบ idempotent
/// · เรียกจาก DevSeed และตอน provision workspace ใหม่ (S2) ภายใน scope ของผู้ใช้นั้น (ActivityEvent ต้องมี Source/ผู้กระทำ)
/// · ต้องไม่สร้างซ้ำเมื่อสาขามีข้อมูลอยู่แล้ว และต้องไม่แตะสาขาอื่น
/// </summary>
public interface IBranchDefaultsSeeder
{
    Task EnsureAsync(string shardKey, int branchId, CancellationToken ct = default);
}
