namespace AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

/// <summary>
/// หนึ่งแถวต่อหนึ่งอู่ที่สมัครเอง (sp_Workspace) — ผูกกับ Branch ของ shard sp1
/// · <see cref="WorkspaceNo"/> = IDENTITY(1001,1) ฐานข้อมูลออกเลขให้ ใช้เป็นรหัสอู่ (SP1001) และ prefix ของชื่อผู้ใช้ (SP1001.M01)
/// · [BIZ] วันหมดอายุตัวจริงอยู่ที่ Branch.EndPackageDate (แบบ legacy — PlanPolicy อ่านจากที่นั่นที่เดียว)
///   <see cref="TrialStartsAt"/>/<see cref="TrialEndsAt"/> เป็นสำเนา (UTC) ไว้ให้ job รายวัน/รายงานกรองได้โดยไม่ต้องข้ามไปอ่าน Branch
///   ทุกจุดที่เลื่อนวัน (spadmin extend/set-plan) ต้องแก้ทั้งสองที่
/// · ไม่มีการลบแถว — purge ตั้ง Status=Purged (ledger/audit อ้างถึงตลอด)
/// </summary>
public class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>ฐานข้อมูลเป็นผู้ออกค่า (IDENTITY) — อย่าตั้งเอง ค่าจะถูกอ่านกลับหลัง SaveChanges</summary>
    public int WorkspaceNo { get; set; }

    /// <summary>Branch.Id ใน shard sp1 — null ระหว่าง provisioning ก่อนสร้าง Branch · unique เมื่อมีค่า</summary>
    public int? BranchId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>เบอร์โทรรูปแบบ E.164 (+66…) — ซ้ำได้ แต่ติดธง <see cref="DuplicatePhoneFlag"/></summary>
    public string? Phone { get; set; }

    public int? ProvinceId { get; set; }

    /// <summary>ขนาดอู่ที่ผู้สมัครเลือก (ค่าตามที่ฟอร์มสมัครกำหนด) — ใช้ประกอบการขายเท่านั้น</summary>
    public string? Size { get; set; }

    public WorkspacePlanKind Plan { get; set; } = WorkspacePlanKind.Trial;
    public WorkspaceStatus Status { get; set; } = WorkspaceStatus.Provisioning;

    /// <summary>ขั้นล่าสุดที่ทำสำเร็จของ WorkspaceProvisioner (S2) — ใช้ resume เมื่อคำขอเดิมถูกส่งซ้ำ</summary>
    public int ProvisionStep { get; set; }

    public string? ProvisionError { get; set; }

    /// <summary>requestId จาก client — คู่ (OwnerAccountId, SignupRequestId) unique = idempotency key ของการเปิดอู่</summary>
    public string? SignupRequestId { get; set; }

    public DateTime? TrialStartsAt { get; set; }
    public DateTime? TrialEndsAt { get; set; }

    public Guid? OwnerAccountId { get; set; }

    public DateTime? SampleDataLoadedAt { get; set; }
    public string? SampleDataError { get; set; }

    /// <summary>[BIZ] เบอร์นี้เคยใช้สมัครอู่อื่นแล้ว — ไม่บล็อก แค่แจ้งทีมขาย</summary>
    public bool DuplicatePhoneFlag { get; set; }

    public DateTime? MobileLoginSeenAt { get; set; }
    public DateTime? ExtensionRequestedAt { get; set; }

    public DateTime? ConvertedAt { get; set; }
    public string? ConvertedToShardKey { get; set; }
    public int? ConvertedToBranchId { get; set; }

    /// <summary>หลังเวลานี้ job รายวันลบข้อมูลของอู่ได้ (ตั้งตอนหมดอายุ/แปลงแล้ว)</summary>
    public DateTime? PurgeAfter { get; set; }

    public DateTime? PurgedAt { get; set; }

    /// <summary>ช่องทางที่มาของผู้สมัคร (ref/landing) + UTM จากลิงก์โฆษณา</summary>
    public string? Source { get; set; }

    public string? UtmSource { get; set; }
    public string? UtmMedium { get; set; }
    public string? UtmCampaign { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
