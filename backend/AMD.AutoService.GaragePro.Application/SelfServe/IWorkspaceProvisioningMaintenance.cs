namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>ผลการล้างอู่ที่เปิดค้าง — <see cref="Errors"/> เป็นข้อความสั้นต่ออู่ (ไม่มีความลับ) ไว้ลง log/สรุป LINE ของ job รายวัน</summary>
public sealed record ProvisioningCleanupResult(
    int Examined,
    int Abandoned,
    int Skipped,
    IReadOnlyList<string> Errors);

/// <summary>
/// งานดูแลการเปิดอู่ (S2 implement · job รายวันของ S4 และ spadmin ของ S5 เป็นผู้เรียก)
/// · อู่ที่ค้าง Provisioning นานเกิน <c>olderThan</c> (นับจากครั้งล่าสุดที่ถูกแตะ) ถือว่าถูกทิ้ง:
///   รื้อแถว shadow ที่สร้างไปแล้ว (Staff/[User]/StaffSectorPosition · Branch.Status = 0) แล้วตั้ง workspace เป็น Purged
///   พร้อมเหตุผลใน ProvisionError + sp_WorkspaceAudit "provision-abandoned"
/// · ข้ามอู่ที่กำลังถูกเปิดอยู่จริงในขณะนั้น (ถือล็อกเดียวกับ <see cref="IWorkspaceProvisioner"/>)
/// · sp_Account ไม่ถูกแตะ — เจ้าของสมัครใหม่ได้ (ไม่ใช่การลบข้อมูลแบบ purge ของอู่ที่หมดอายุ)
/// </summary>
public interface IWorkspaceProvisioningMaintenance
{
    Task<ProvisioningCleanupResult> CleanupStuckAsync(TimeSpan olderThan, CancellationToken ct = default);
}
