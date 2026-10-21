namespace AMD.AutoService.GaragePro.Application.Internal;

/// <summary>
/// สาขากลุ่ม Service (BranchGroupId 7) ของ shard GaragePro (db2) ตามที่อ่านจาก Garage เดิม · วันที่แปลงเป็น UTC แล้ว
/// (คอลัมน์ในตารางเป็นเวลาไทยไม่มีโซน ดู <see cref="SelfServe.BranchPackageDates"/>)
/// </summary>
public sealed record GarageProBranchFacts(
    int Id,
    string Name,
    string? Phone,
    int Status,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    DateTime? CreatedAtUtc);

/// <summary>จ๊อบของหนึ่งสาขา (svc_Job) · <see cref="JobsSince"/> = จ๊อบที่เปิดตั้งแต่เวลาที่ถาม · <see cref="LastJobAt"/> = เปิดล่าสุดตลอดกาล</summary>
public sealed record SiteJobActivity(int BranchId, int JobsSince, DateTime? LastJobAt);

/// <summary>
/// ข้อมูลของ API ภายใน (docs/17 §8.6) ที่อยู่นอก SelfServeDbContext · Infrastructure implement
/// · [RISK] Garage เดิมมี lock convoy: อ่านเฉพาะคอลัมน์ที่ใช้ + <c>WITH (READUNCOMMITTED)</c> · ไม่มีคำสั่งเขียน
/// · จ๊อบอ่านจาก svc_Job ของฐานที่ shard นั้นใช้ (GarageService สำหรับ db2 · GarageSelfServe สำหรับ sp1) ผ่าน scope ของ shard นั้นเอง
///   ไม่พึ่ง ICurrentUser ของคำขอ (คำขอภายในไม่มี JWT)
/// </summary>
public interface IInternalSiteReader
{
    /// <summary>ทุกสาขากลุ่ม Service ของ shard นี้ (ทั้งเปิดและปิดใช้งาน) เรียงตาม Id</summary>
    Task<IReadOnlyList<GarageProBranchFacts>> ReadGarageProBranchesAsync(string shardKey, CancellationToken ct = default);

    /// <summary>ต่อสาขาใน query เดียว (GROUP BY BranchId) · สาขาที่ไม่มีจ๊อบเลยไม่อยู่ใน dictionary</summary>
    Task<IReadOnlyDictionary<int, SiteJobActivity>> ReadJobActivityAsync(string shardKey, DateTime sinceUtc,
        CancellationToken ct = default);
}
