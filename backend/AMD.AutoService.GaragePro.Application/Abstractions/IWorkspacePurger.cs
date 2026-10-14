using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// คำขอลบข้อมูลอู่ทดลองใช้หนึ่งอู่ (docs/12 §6 วันที่ 120)
/// · <paramref name="ShardKey"/> ว่าง = shard self-serve ที่ตั้งค่าไว้ · ส่ง shard อื่นมา = ถูกปฏิเสธเสมอ
/// · <paramref name="AllowPaid"/> = ทีมงานยืนยันว่าจะลบอู่ที่จ่ายเงินแล้วจริง (job รายวันไม่ส่งค่านี้)
/// </summary>
public sealed record WorkspacePurgeRequest(
    Guid WorkspaceId,
    string Actor,
    string? ShardKey = null,
    bool AllowPaid = false);

/// <summary>ผลการลบ — จำนวนแถวต่อตาราง + ไฟล์ที่ลบ/ไม่พบ/ลบไม่สำเร็จ (ไฟล์ที่ล้มเก็บ path ไว้ให้ทีมงานลบเอง)</summary>
public sealed record WorkspacePurgeReport(
    Guid WorkspaceId,
    int WorkspaceNo,
    int? BranchId,
    bool AlreadyPurged,
    IReadOnlyDictionary<string, int> DeletedRows,
    int FilesDeleted,
    int FilesMissing,
    IReadOnlyList<string> FilesFailed);

/// <summary>
/// ลบข้อมูลทั้งหมดของอู่ใน shard self-serve: svc_* ของสาขา → ไฟล์แนบบน FTP → แถวรูปร่าง Garage (Branch/Staff/User/ลูกค้า/รถ)
/// → sp_Workspace = Purged · sp_Account = Purged โดย<b>คง NormalizedEmail</b> (tombstone กันสมัครซ้ำ — docs/12 §7)
/// · ทำซ้ำได้ (re-runnable): รอบที่ล้มกลางทางรันใหม่แล้วไปต่อจากที่ค้าง
/// · [SECURITY] ต้องปฏิเสธทุกกรณีที่ปลายทางอาจไม่ใช่ฐาน GarageSelfServe — ลบผิดฐาน = ลบข้อมูลลูกค้าจริง
/// </summary>
public interface IWorkspacePurger
{
    Task<Result<WorkspacePurgeReport>> PurgeAsync(WorkspacePurgeRequest request, CancellationToken ct = default);

    /// <summary>
    /// อู่ที่ S2 รื้อทิ้งเพราะเปิดค้าง (IWorkspaceProvisioningMaintenance — Status=Purged แต่ svc_* ที่ขั้นตั้งค่าสร้างไว้ยังอยู่:
    /// หมวดหมู่/คลังเริ่มต้น · กะ · override บทบาท) — ลบ svc_* ของสาขานั้นอย่างเดียว ไม่แตะ sp_Account (เจ้าของสมัครใหม่ได้)
    /// </summary>
    Task<Result<WorkspacePurgeReport>> PurgeAbandonedServiceRowsAsync(Guid workspaceId, string actor,
        CancellationToken ct = default);
}
