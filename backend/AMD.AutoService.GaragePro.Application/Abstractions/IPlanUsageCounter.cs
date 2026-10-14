namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// นับจำนวนที่ใช้ไปแล้วของแต่ละลิมิตแพ็กเกจ (docs/12 §5) — ใช้โดย IWorkspacePolicy.CheckLimitAsync และหน้า status ของ selfserve
/// · พนักงาน = เฉพาะที่ยังใช้งาน (ปิดใช้งานแล้วคืนโควตา)
/// · สินค้า/บริการ · ลูกค้า · จ๊อบ = ทุกแถวที่เคยสร้าง รวมที่ปิด/ลบแบบ soft แล้ว — ลิมิตมีไว้กันต้นทุน/สแปม
///   ถ้านับเฉพาะที่ active จะสร้าง-ลบวนได้ไม่จำกัด
/// · ลูกค้านับตามกติกาการมองเห็นเดียวกับหน้ารายการลูกค้า (BranchDataScope) เพราะ Customer ไม่มีคอลัมน์สาขา
/// </summary>
public interface IPlanUsageCounter
{
    Task<int> CountAsync(string shardKey, int branchId, PlanLimitKind kind, CancellationToken ct = default);
}
