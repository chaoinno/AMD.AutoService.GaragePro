using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// เข้าถึงช่วงเวลาทำงานของช่าง — สโคปด้วย shard/สาขาจาก JWT เสมอ ยกเว้นเมธอดที่ระบุ shard/branch มาเอง
/// (ใช้ตอนปิดกะ ซึ่งคนกดกับเจ้าของคาบเป็นคนละคน)
/// </summary>
public interface IWorkIntervalRepository
{
    Task<WorkInterval?> GetAsync(Guid id, CancellationToken ct = default);

    /// <summary>คาบที่ยังเปิดอยู่ของช่างคนนี้ — มีได้ไม่เกินหนึ่ง (บังคับด้วย UX_WorkInterval_OneOpenPerTech)</summary>
    Task<WorkInterval?> GetOpenByTechnicianAsync(long staffId, CancellationToken ct = default);

    /// <summary>เวอร์ชันที่ระบุ shard/สาขาเอง — ใช้ตอนปิดกะที่ทำแทนช่างคนอื่น</summary>
    Task<WorkInterval?> GetOpenByTechnicianAsync(
        string shardKey, int branchId, long staffId, CancellationToken ct = default);

    /// <summary>ทุกคาบที่เปิดอยู่ของจ๊อบนี้ — หลายแถวได้เพราะช่างหลายคนรุมคันเดียวกันได้ (docs/09 §5.3)</summary>
    Task<IReadOnlyList<WorkInterval>> GetOpenByJobAsync(Guid jobId, CancellationToken ct = default);

    Task<WorkInterval?> GetByRequestIdAsync(Guid requestId, CancellationToken ct = default);

    Task<IReadOnlyList<WorkInterval>> GetByJobAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>คาบของทั้งสาขาในช่วงเวลาหนึ่ง เรียงใหม่ไปเก่า — ใช้หน้าตรวจคุณภาพข้อมูล</summary>
    Task<IReadOnlyList<WorkInterval>> SearchAsync(
        DateTime fromUtc, DateTime toUtc, bool onlyNeedsReview, int take, CancellationToken ct = default);

    Task AddAsync(WorkInterval interval, CancellationToken ct = default);

    /// <summary>กะของ session นั้น — ใช้หาเพดานตัดคาบที่ลืมปิด · null เมื่อไม่มี session หรือหากะไม่เจอ</summary>
    Task<Shift?> GetShiftForSessionAsync(Guid sessionId, CancellationToken ct = default);

    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>
/// ชนกฎ unique ของ svc_WorkInterval — repository แปลง SqlException 2601/2627 มาเป็นตัวนี้เพื่อไม่ให้
/// Application รู้จัก SqlClient · ผู้เรียกต้อง re-read ด้วย RequestId เพื่อแยกว่าชนเพราะอะไร
/// (ห้ามแยกด้วยการ parse ชื่อ index จากข้อความ — เปราะและขึ้นกับภาษา/collation ของเซิร์ฟเวอร์)
/// </summary>
public sealed class WorkIntervalConflictException(string messageTh) : Exception(messageTh);
