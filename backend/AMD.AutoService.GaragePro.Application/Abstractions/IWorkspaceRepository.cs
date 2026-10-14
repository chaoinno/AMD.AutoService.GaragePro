using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// ข้อมูลของอู่ที่สมัครเอง — สองแหล่งในฐาน GarageSelfServe เดียวกัน:
/// · ตาราง <c>sp_*</c> ผ่าน SelfServeDbContext (entity ที่คืนไปถูก track — แก้ property แล้วเรียก <see cref="SaveChangesAsync"/>)
/// · ตาราง "รูปร่างเหมือน Garage" (Branch/[User]/Staff…) ผ่าน Dapper ที่ <c>LegacyShards:ConnectionStrings:&lt;shard&gt;</c>
///   แบบเดียวกับคลาส legacy (คีย์นั้นต้องชี้ฐานเดียวกับ <c>SelfServe:ConnectionString</c> — deploy/selfserve/README.md)
/// · partial: งานอื่น (S3 ข้อมูลตัวอย่าง) เพิ่มเมธอดในไฟล์ <c>IWorkspaceRepository.&lt;ชื่อ&gt;.cs</c> ของตัวเองได้โดยไม่ชนไฟล์นี้
/// </summary>
public partial interface IWorkspaceRepository
{
    // ---------- sp_Workspace / sp_Account (EF) ----------

    Task<SelfServeAccount?> GetAccountAsync(Guid accountId, CancellationToken ct = default);

    Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>idempotency key ของการเปิดอู่ — (OwnerAccountId, SignupRequestId) unique</summary>
    Task<Workspace?> FindBySignupRequestAsync(Guid ownerAccountId, string signupRequestId, CancellationToken ct = default);

    /// <summary>ทุก workspace ของบัญชีนี้ (รวมที่ถูกล้างแล้ว) — ใช้บังคับ "หนึ่งบัญชีหนึ่งอู่"</summary>
    Task<IReadOnlyList<Workspace>> ListByOwnerAsync(Guid ownerAccountId, CancellationToken ct = default);

    /// <summary>มี workspace อื่น (ไม่นับ <paramref name="exceptWorkspaceId"/>) ใช้เบอร์นี้แล้วหรือยัง — ใช้ติดธง DuplicatePhoneFlag</summary>
    Task<bool> PhoneUsedByAnotherWorkspaceAsync(string phoneE164, Guid? exceptWorkspaceId, CancellationToken ct = default);

    /// <summary>
    /// INSERT แถวใหม่แล้ว save ทันที (ฐานออก <see cref="Workspace.WorkspaceNo"/> ให้) — ถ้าชน idempotency key
    /// (คำขอเดิมมาพร้อมกันจาก instance อื่น) คืนแถวที่มีอยู่แทน ไม่ throw
    /// </summary>
    Task<Workspace> AddWorkspaceAsync(Workspace workspace, CancellationToken ct = default);

    /// <summary>อ่านค่าล่าสุดจากฐานทับ entity (track ใหม่ถ้าหลุดไปแล้ว) — หลังได้ล็อก ค่าที่อ่านก่อนหน้าอาจเก่าแล้ว</summary>
    Task ReloadAsync(Workspace workspace, CancellationToken ct = default);

    /// <summary>workspace ที่ยังเป็น Provisioning และไม่ถูกแตะตั้งแต่ก่อน <paramref name="untouchedSinceUtc"/></summary>
    Task<IReadOnlyList<Workspace>> ListStuckProvisioningAsync(DateTime untouchedSinceUtc, CancellationToken ct = default);

    void AddAudit(WorkspaceAudit audit);

    Task SaveChangesAsync(CancellationToken ct = default);

    /// <summary>
    /// บันทึกว่าการเปิดอู่ล้มที่ขั้นไหน (<see cref="Workspace.ProvisionError"/> + <see cref="Workspace.UpdatedAt"/> ตามค่าใน entity)
    /// และเขียน log พร้อม exception เต็ม (Application ไม่มี logger)
    /// · ทิ้งการแก้ไขที่ค้างใน context ทั้งหมดก่อน (entity ทุกตัวหลุดจากการ track) แล้ว UPDATE เฉพาะสองคอลัมน์นั้น —
    ///   กันสถานะครึ่งๆ ของขั้นที่ล้ม (เช่นบัญชีถูกตั้ง Active แต่ workspace ยังไม่) ถูก save ไปด้วย
    /// · ห้าม throw — ถูกเรียกระหว่างจัดการ error อยู่แล้ว
    /// </summary>
    Task RecordProvisionFailureAsync(Workspace workspace, string error, Exception exception, CancellationToken ct = default);

    /// <summary>ล้าง cache แพ็กเกจของสาขา (WorkspacePolicy) หลังเปลี่ยนสถานะ workspace — ไม่งั้นผลเก่าค้างได้ถึง 60 วิ</summary>
    void InvalidatePlanCache(int branchId);

    /// <summary>
    /// ล็อกระดับฐานข้อมูล (sp_getapplock) ให้มีการเปิดอู่ของบัญชีเดียวกันทีละคำขอ ข้ามทุก instance ของ API
    /// · คืน null เมื่อรอครบ <paramref name="timeout"/> แล้วยังไม่ได้ (TimeSpan.Zero = ไม่รอ) · dispose = ปล่อยล็อก
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireProvisioningLockAsync(string lockKey, TimeSpan timeout, CancellationToken ct = default);

    // ---------- shadow rows (Dapper · ตารางรูปร่างเหมือน Garage) ----------

    /// <summary>
    /// INSERT Branch ใหม่ แล้วผูก <c>sp_Workspace.BranchId</c> + วันทดลองใช้ (สำเนา UTC) + <paramref name="provisionStep"/>
    /// ใน transaction เดียวกัน — ล้มกลางทางไม่เหลือ Branch กำพร้า และ retry ไม่สร้าง Branch ซ้ำ
    /// · ปฏิเสธ (throw) เมื่อ workspace มี BranchId อยู่แล้ว · หลังสำเร็จ entity ที่ส่งมามีค่าล่าสุดแล้ว
    /// </summary>
    Task<int> CreateShadowBranchAsync(string shardKey, Workspace workspace, ShadowBranchSpec branch, int provisionStep,
        DateTime nowUtc, CancellationToken ct = default);

    /// <summary>[User] ตามชื่อผู้ใช้ ที่พนักงานอยู่สาขานี้ — null = ยังไม่มี</summary>
    Task<ShadowUserRef?> FindShadowUserAsync(string shardKey, int branchId, string userName, CancellationToken ct = default);

    /// <summary>
    /// รื้ออู่ที่เปิดไม่สำเร็จ: ลบ Staff/[User]/StaffSectorPosition/StaffRepairTypeNPoint (+ ChangeLog ที่อ้างถึง) ของสาขา
    /// แล้วตั้ง Branch.Status = 0 — transaction เดียว · ไม่แตะตาราง svc_* ของสาขา (ปล่อยให้งานล้างข้อมูลของ S4)
    /// </summary>
    Task<ShadowCleanupResult> RemoveShadowBranchAsync(string shardKey, int branchId, DateTime nowUtc, CancellationToken ct = default);
}

/// <summary>
/// ค่าของแถว Branch ใหม่ — วันที่ส่งเป็น UTC แล้วแปลงเป็นเวลาไทยที่นี่ที่เดียวผ่าน <see cref="BranchPackageDates"/>
/// (คอลัมน์แพ็กเกจของ Branch เป็นเวลาไทยแบบ legacy · <c>sp_Workspace.TrialStartsAt/TrialEndsAt</c> เป็นสำเนา UTC ของค่าเดียวกัน)
/// </summary>
public sealed record ShadowBranchSpec(
    string Name,
    string? PhoneNumber,
    string? Email,
    int? ProvinceId,
    string PinCode,
    DateTime PackageStartsAtUtc,
    int PackageDays,
    DateTime CreatedAtUtc)
{
    public DateTime PackageEndsAtUtc => PackageStartsAtUtc.AddDays(PackageDays);

    public DateTime StartPackageDateLocal => BranchPackageDates.ToBranchLocal(PackageStartsAtUtc);
    public DateTime EndPackageDateLocal => BranchPackageDates.ToBranchLocal(PackageEndsAtUtc);
    public DateTime CreatedDateLocal => BranchPackageDates.ToBranchLocal(CreatedAtUtc);
}

public sealed record ShadowUserRef(long UserId, long StaffId, string UserName, bool IsAdministrator);

public sealed record ShadowCleanupResult(int StaffRemoved, int UsersRemoved);
