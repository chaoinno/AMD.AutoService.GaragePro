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

    Task AddAsync(JobChatMessage message, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
