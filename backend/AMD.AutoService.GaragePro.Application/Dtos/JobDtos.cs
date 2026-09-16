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
    int JobTypeId,
    string? JobTypeName,
    string Status,
    string StatusLabel,
    bool IsOverdue);

public sealed record JobSearchQuery(
    string ShardKey,
    int BranchId,
    string? Keyword,
    int Take,
    DateTime? BeforeCreatedAt,
    Guid? BeforeJobId,
    int? JobTypeId,
    JobStatus? Status);

public sealed record CreateJobRequest(
    long CustomerId,
    long VehicleId,
    int JobTypeId,
    string? SenderName,
    string? SenderPhoneNumber,
    string? Detail);

public sealed record CreatedJobDto(Guid JobId, string JobNo);

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
        JobTypeId: job.JobTypeId,
        JobTypeName: job.JobTypeName,
        Status: JobStateMachine.ToToken(job.Status),
        StatusLabel: JobStateMachine.Describe(job.Status),
        IsOverdue: job.IsOverdue(nowUtc));
}
