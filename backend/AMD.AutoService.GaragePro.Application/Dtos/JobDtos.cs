using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;

namespace AMD.AutoService.GaragePro.Application.Dtos;

/// <summary>จ๊อบสำหรับหน้าจอ — ทุก field มาจาก svc_Job โดยตรง ไม่มีการ live-read legacy อีกต่อไป</summary>
public sealed record JobDto(
    Guid JobId,
    string JobNo,
    int BranchId,
    string BranchName,
    long CustomerId,
    string CustomerName,
    string? CustomerPhone,
    long VehicleId,
    string? VehicleImagePath,
    string VehicleRegistration,
    string? VehicleModel,
    string? VehicleVin,
    DateTime CreatedAt,
    DateTime? PromiseAt,
    DateTime? AppointmentAt,
    DateTime? ActualArrivalAt,
    int JobTypeId,
    string? JobTypeName,
    string Status,
    string StatusLabel,
    bool IsOverdue,
    int? MileageAtIntake = null);

public sealed record JobSearchQuery(
    string ShardKey,
    int BranchId,
    string? Keyword,
    int Take,
    DateTime? BeforeCreatedAt,
    Guid? BeforeJobId,
    int? JobTypeId,
    JobStatus? Status);

/// <summary>ปฏิทินจ๊อบวางตามวันไหน — วันนัดเข้า (AppointmentAt) หรือวันนัดส่งมอบ (PromiseAt)</summary>
public enum JobCalendarDateField
{
    Appointment,
    Promise
}

/// <summary>ช่วงเวลาที่ต้องการดูปฏิทิน — กรองด้วยฟิลด์วันที่ที่เลือก (ไม่ว่าง) ไม่ใช่ JobTypeId
/// (JobTypeId ถูกเปลี่ยนเป็น "ปิดจ๊อบ" เองตอนถึงสถานะจบ — ดู JobService.TransitionAsync)</summary>
public sealed record JobAppointmentQuery(
    string ShardKey,
    int BranchId,
    DateTime FromUtc,
    DateTime ToUtc,
    string? Keyword,
    JobStatus? Status,
    int Take,
    JobCalendarDateField DateField = JobCalendarDateField.Appointment);

public sealed record JobCalendarDto(IReadOnlyList<JobDto> Items, bool Truncated, int Limit);

public sealed record CreateJobRequest(
    long CustomerId,
    long VehicleId,
    int JobTypeId,
    string? SenderName,
    string? SenderPhoneNumber,
    string? Detail,
    DateTimeOffset? AppointmentAt = null,
    DateTimeOffset? PromiseAt = null,
    int? MileageAtIntake = null);

public sealed record UpdateJobAppointmentRequest(DateTimeOffset AppointmentAt);

/// <summary>ตั้ง/เลื่อนวันเวลานัดส่งมอบรถคืนลูกค้า (Job.PromiseAt)</summary>
public sealed record UpdateJobPromiseRequest(DateTimeOffset PromiseAt);

/// <summary>บันทึก/แก้เลขไมล์ขณะรับรถ (Job.MileageAtIntake) — กม.</summary>
public sealed record UpdateJobMileageRequest(int MileageAtIntake);

/// <summary>ประวัติการเปลี่ยนวันนัด (นัดเข้า/นัดส่งมอบ) ของจ๊อบ — อ่านจาก ActivityEvent
/// Field = "appointment" | "promise" · From เป็น null เมื่อเป็นการตั้งค่าครั้งแรก</summary>
public sealed record JobScheduleChangeDto(
    long Id,
    string Field,
    DateTime? From,
    DateTime? To,
    string DescriptionTh,
    string PerformedByName,
    string Source,
    DateTime OccurredAt);

/// <summary>แปลงงานนัดหมาย (JobTypeId=10) เป็นรถในอู่ (JobTypeId=9) พร้อมบันทึกวันเวลาที่รถเข้าอู่จริง</summary>
/// <param name="MileageAtIntake">เลขไมล์ตอนรถเข้าอู่ — บังคับถ้าจ๊อบยังไม่มีค่า (รถนัดหมายเปิดจ๊อบตอนรถยังไม่มา)</param>
public sealed record ConvertToInShopRequest(DateTimeOffset ActualArrivalAt, int? MileageAtIntake = null);

/// <param name="ExistingOpenJobNo">เลขจ๊อบที่ยังไม่ปิดของรถคันเดียวกันที่มีอยู่ก่อนเปิดจ๊อบนี้ (ถ้ามี) —
/// ให้หน้าจอเตือนว่าเปิดซ้อนกับงานเดิม ไม่ใช่ข้อผิดพลาด</param>
public sealed record CreatedJobDto(Guid JobId, string JobNo, string? ExistingOpenJobNo = null);

/// <summary>
/// จำนวนจ๊อบที่ "ยังไม่ปิด" แยกตามสถานะ สำหรับหน้าหลักของมือถือและ badge เมนู
/// นับเฉพาะสถานะที่ยังเดินต่อได้ (ไม่รวม Completed/Cancelled) เพราะจ๊อบที่ปิดแล้วสะสมไปเรื่อยๆ
/// ตัวเลขจะโตไม่มีเพดานและไม่บอกอะไรกับคนที่ต้องลงมือทำงานต่อ — ถ้าต้องการยอดรวมย้อนหลังให้ใช้ /reports
/// </summary>
public sealed record JobCountsDto(
    int TotalOpen,
    int Overdue,
    IReadOnlyList<JobStatusCountDto> ByStatus);

/// <summary>ผลนับดิบจาก repository — service เป็นคนแปลง JobStatus เป็น token/ข้อความไทย</summary>
public sealed record JobStatusTally(JobStatus Status, int Count, int Overdue);

public sealed record JobStatusOptionDto(string Token, string Label);

public sealed record TransitionJobRequest(string ToStatus, string? Reason);

public sealed record JobTransitionResultDto(string Status, string StatusLabel);

public static class JobMapper
{
    public static JobDto ToDto(Job job, DateTime nowUtc) => new(
        JobId: job.Id,
        JobNo: job.JobNo,
        BranchId: job.BranchId,
        BranchName: job.BranchName,
        CustomerId: job.CustomerId,
        CustomerName: job.CustomerName,
        CustomerPhone: job.CustomerPhone,
        VehicleId: job.VehicleId,
        VehicleImagePath: job.VehicleImagePath,
        VehicleRegistration: job.VehicleRegistration,
        VehicleModel: job.VehicleModel,
        VehicleVin: job.VehicleVin,
        CreatedAt: job.CreatedAt,
        PromiseAt: job.PromiseAt,
        AppointmentAt: job.AppointmentAt,
        ActualArrivalAt: job.ActualArrivalAt,
        JobTypeId: job.JobTypeId,
        JobTypeName: job.JobTypeName,
        Status: JobStateMachine.ToToken(job.Status),
        StatusLabel: JobStateMachine.Describe(job.Status),
        IsOverdue: job.IsOverdue(nowUtc),
        MileageAtIntake: job.MileageAtIntake);
}
