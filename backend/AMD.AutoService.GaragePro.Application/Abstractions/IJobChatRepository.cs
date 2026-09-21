using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IJobChatRepository
{
    /// <summary>
    /// หน้าเดียวสองทิศทาง: beforeAt/beforeId = โหลดข้อความเก่ากว่า (เลื่อนขึ้น), afterAt/afterId = ข้อความใหม่กว่า
    /// (poll ต่อ) — ห้ามส่งทั้งคู่พร้อมกัน ไม่ส่งเลยคือหน้าล่าสุด เรียงจากใหม่→เก่าเสมอ (ก็อป tie-break จาก JobRepository.SearchAsync)
    /// </summary>
    Task<IReadOnlyList<JobChatMessage>> GetPageAsync(
        Guid jobId, DateTime? beforeAt, Guid? beforeId, DateTime? afterAt, Guid? afterId, int take,
        CancellationToken ct = default);

    Task<JobChatMessage?> GetByIdAsync(Guid messageId, CancellationToken ct = default);

    /// <summary>
    /// ข้อความล่าสุดของแต่ละจ๊อบในชุดที่ขอ — ใช้แสดงจุด "มีข้อความใหม่" บนการ์ดจ๊อบในคิวงาน
    /// โดยยิงครั้งเดียวแทนที่จะยิงทีละคัน · จ๊อบที่ยังไม่มีข้อความเลยจะไม่อยู่ในผลลัพธ์
    ///
    /// สโคปด้วย shard/สาขาในคิวรีเลย (จ๊อบนอกสาขาหลุดออกไปเอง) เพราะถ้าตรวจทีละจ๊อบแบบ
    /// ValidateJobScopeAsync จะกลายเป็น N คิวรีซึ่งเป็นสิ่งที่ endpoint นี้ตั้งใจกำจัด
    ///
    /// <b>นับข้อความที่ถูกลบด้วย</b> — ต้องตรงกับที่หน้าแชทใช้ตอน markSeen (มันเก็บ id ของข้อความ
    /// ล่าสุดรวมที่ถูกลบ) ไม่งั้นถ้าข้อความล่าสุดถูกลบ จุดแดงจะค้างตลอดกาลเพราะ id ไม่มีวันตรงกัน
    /// </summary>
    Task<IReadOnlyList<JobChatLatest>> GetLatestPerJobAsync(
        string shardKey, int branchId, IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default);

    Task AddAsync(JobChatMessage message, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>ข้อความล่าสุดของจ๊อบหนึ่ง — พอสำหรับให้ client เทียบกับ id ที่อ่านถึงแล้วในเครื่อง</summary>
public sealed record JobChatLatest(Guid JobId, Guid MessageId, DateTime CreatedAt);
