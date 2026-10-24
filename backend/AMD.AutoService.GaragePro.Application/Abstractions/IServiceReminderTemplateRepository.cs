using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IServiceReminderTemplateRepository
{
    /// <summary>สาขานี้เคยมีรายการตั้งต้นแล้วหรือยัง (นับทั้งที่ปิดใช้งาน) — ใช้ตัดสินใจ seed ชุดตั้งต้นครั้งเดียว</summary>
    Task<bool> AnyAsync(string shardKey, int branchId, CancellationToken ct = default);

    /// <summary>เรียงตาม SortOrder แล้วชื่อ — ไม่ track (ใช้แสดงผลอย่างเดียว)</summary>
    Task<IReadOnlyList<ServiceReminderTemplate>> SearchAsync(
        string shardKey, int branchId, bool includeInactive, CancellationToken ct = default);

    Task<ServiceReminderTemplate?> GetAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default);

    Task<bool> NameExistsAsync(
        string shardKey, int branchId, string name, Guid? excludingId, CancellationToken ct = default);

    Task AddAsync(ServiceReminderTemplate template, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default);

    /// <summary>ยิง MasterDataConflictException เมื่อชนชื่อซ้ำ (constraint UX_svc_ServiceReminderTemplate_Name)</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>ทิ้งแถวที่ยัง Added ค้างใน context หลัง SaveChanges ล้ม (seed ชนกัน) เพื่อไม่ให้ถูกบันทึกซ้ำโดยไม่ตั้งใจ</summary>
    void DiscardPending();
}
