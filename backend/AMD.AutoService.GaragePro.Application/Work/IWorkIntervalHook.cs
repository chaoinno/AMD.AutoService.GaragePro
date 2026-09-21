using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Work;

/// <summary>
/// จุดที่ระบบอื่นปิดช่วงเวลาให้อัตโนมัติ (docs/09-technician-time-tracking.md §6) — แยกจาก
/// <see cref="IWorkTimeService"/> โดยเจตนาเพื่อให้กราฟ dependency ไม่เป็นวงกลม:
///
///   JobService      ──▶ IWorkIntervalHook ──▶ IWorkIntervalRepository
///   AuthService     ──▶ IWorkIntervalHook
///   WorkTimeService ──▶ IWorkIntervalHook, IWorkIntervalRepository, IJobService
///
/// ตัวที่ implement interface นี้ <b>ต้องไม่รู้จัก IJobService</b> ไม่งั้นจะได้วงกลมเดิมที่ซ่อนอยู่ใต้
/// interface สองอัน (และห้ามแก้ด้วย Lazy&lt;T&gt;/IServiceProvider ซึ่งเป็นการกลบ ไม่ใช่การแก้)
///
/// [BIZ] ทุกเมธอดที่นี่ <b>ห้ามเรียก SaveChanges</b> — repository ทุกตัวแชร์ ServiceDbContext เดียวกัน
/// ต่อ request การ mutate ที่นี่จึงถูก commit ไปพร้อม SaveChanges ของผู้เรียก เป็น transaction เดียวกันจริง
/// ซึ่งคือสิ่งที่ §6 ต้องการ (เปลี่ยนสถานะจ๊อบแล้วคาบต้องปิด — ไม่มีทางสำเร็จแค่อย่างเดียว)
/// </summary>
public interface IWorkIntervalHook
{
    /// <summary>
    /// ปิดทุกคาบที่เปิดอยู่ของจ๊อบนี้ — หลายแถวได้เพราะช่างหลายคนรุมคันเดียวกันได้ (docs/09 §5.3)
    /// คืนจำนวนคาบที่ปิดไป
    /// </summary>
    Task<int> CloseOpenForJobAsync(Guid jobId, WorkEndReason reason, CancellationToken ct = default);

    /// <summary>
    /// ปิดคาบที่เปิดอยู่ของช่างคนหนึ่ง — ใช้ตอนปิด/เปิดกะ ซึ่งคนกดกับเจ้าของคาบเป็นคนละคนเสมอ
    /// (RoleMapper.CanCloseShift บล็อกช่างไม่ให้ปิดกะตัวเอง) จึงต้องรับ shard/สาขาจาก session ไม่ใช่จาก JWT
    /// </summary>
    Task<WorkInterval?> CloseOpenForTechnicianAsync(
        string shardKey, int branchId, long staffId,
        WorkEndReason reason, DateTime endedAt, bool autoCapped, long endedByUserId,
        CancellationToken ct = default);
}
