using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class JobChatRepository(ServiceDbContext db) : IJobChatRepository
{
    public async Task<IReadOnlyList<JobChatMessage>> GetPageAsync(
        Guid jobId, DateTime? beforeAt, Guid? beforeId, DateTime? afterAt, Guid? afterId, int take,
        CancellationToken ct = default)
    {
        var q = db.JobChatMessages
            .Include(m => m.Mentions)
            .Include(m => m.ReplyToMessage)
            .Where(m => m.JobId == jobId);

        if (beforeAt is not null && beforeId is not null)
        {
            var at = beforeAt.Value;
            var id = beforeId.Value;
            q = q.Where(m => m.CreatedAt < at || (m.CreatedAt == at && m.Id.CompareTo(id) < 0));
        }

        if (afterAt is not null && afterId is not null)
        {
            var at = afterAt.Value;
            var id = afterId.Value;
            q = q.Where(m => m.CreatedAt > at || (m.CreatedAt == at && m.Id.CompareTo(id) > 0));

            // ทิศทาง poll ต่อ: เรียงเก่า→ใหม่แล้ว Take ตัวที่เก่าสุดของ "ใหม่" ก่อน กัน gap ถ้ามีข้อความ
            // เข้ามาระหว่างรอบ poll มากกว่า take — cursor รอบถัดไปจะต่อจากตัวสุดท้ายของ batch นี้เสมอ ไม่มีตกหล่น
            return await q
                .OrderBy(m => m.CreatedAt)
                .ThenBy(m => m.Id)
                .Take(take)
                .ToListAsync(ct);
        }

        return await q
            .OrderByDescending(m => m.CreatedAt)
            .ThenByDescending(m => m.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<JobChatMessage?> GetByIdAsync(Guid messageId, CancellationToken ct = default) =>
        db.JobChatMessages
          .Include(m => m.Mentions)
          .FirstOrDefaultAsync(m => m.Id == messageId, ct);

    public async Task AddAsync(JobChatMessage message, CancellationToken ct = default) =>
        await db.JobChatMessages.AddAsync(message, ct);

    public async Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) =>
        await db.ActivityEvents.AddAsync(evt, ct);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
