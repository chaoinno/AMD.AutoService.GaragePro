using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IJobRepository
{
    Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>งานที่ยังไม่ปิดของรถคันนี้ — ใช้กันเปิดจ๊อบซ้ำ</summary>
    Task<Job?> GetOpenByVehicleAsync(
        string shardKey, int branchId, long vehicleId, CancellationToken ct = default);

    Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default);

    /// <summary>งานนัดหมายในช่วงเวลาที่กำหนด (AppointmentAt ไม่ว่าง) เรียงตามเวลานัดจากน้อยไปมาก — ใช้มุมมองปฏิทิน</summary>
    Task<IReadOnlyList<Job>> GetAppointmentsAsync(JobAppointmentQuery query, CancellationToken ct = default);

    /// <summary>จำนวนงานที่ยังไม่ปิด (ไม่รวม Completed/Cancelled) — ใช้แสดงตัวเลขในเมนู</summary>
    Task<int> CountOpenAsync(string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default);

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
