using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IJobRepository
{
    Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>งานที่ยังไม่ปิดของรถคันนี้ (ตัวใดตัวหนึ่ง) — ใช้เตือนตอนเปิดจ๊อบซ้อน ไม่ได้บล็อก</summary>
    Task<Job?> GetOpenByVehicleAsync(
        string shardKey, int branchId, long vehicleId, CancellationToken ct = default);

    Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default);

    /// <summary>งานที่มีวันนัดตาม query.DateField (นัดเข้า/นัดส่งมอบ) อยู่ในช่วงเวลาที่กำหนด
    /// เรียงตามเวลานัดจากน้อยไปมาก — ใช้มุมมองปฏิทิน</summary>
    Task<IReadOnlyList<Job>> GetAppointmentsAsync(JobAppointmentQuery query, CancellationToken ct = default);

    /// <summary>ActivityEvent ของจ๊อบนี้ที่เป็นชนิดตามที่ระบุ เรียงใหม่สุดก่อน
    /// default คืนว่างเพื่อไม่ต้องไล่แก้ fake ในไฟล์เทสต์ที่ไม่เกี่ยวข้อง (ทำแบบเดียวกับ ICurrentUser.StaffId) —
    /// JobRepository ตัวจริง override เสมอ</summary>
    Task<IReadOnlyList<ActivityEvent>> GetEventsAsync(
        Guid jobId, IReadOnlyCollection<string> eventTypes, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ActivityEvent>>([]);

    /// <summary>จำนวนงานที่ยังไม่ปิด (ไม่รวม Completed/Cancelled) — ใช้แสดงตัวเลขในเมนู</summary>
    Task<int> CountOpenAsync(string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default);

    /// <summary>นับจ๊อบที่ยังไม่ปิด แยกตามสถานะ พร้อมจำนวนที่เกินเวลานัดส่งในแต่ละสถานะ</summary>
    Task<IReadOnlyList<JobStatusTally>> CountOpenByStatusAsync(
        string shardKey, int branchId, int? jobTypeId, DateTime nowUtc, CancellationToken ct = default);

    Task AddAsync(Job job, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>สร้างเลขจ๊อบแบบ concurrency-safe ทั้งหมดในฐาน GarageService — ไม่พึ่ง legacy อีกต่อไป</summary>
public interface IJobNumberGenerator
{
    /// <summary>รูปแบบ JB{yyMMdd}{BranchId:D4}{ลำดับ:D3} — ลำดับเริ่มนับใหม่ทุกวันต่อสาขา</summary>
    Task<string> NextAsync(string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default);
}
