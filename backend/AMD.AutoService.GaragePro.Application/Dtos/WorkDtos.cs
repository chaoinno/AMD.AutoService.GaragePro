using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record WorkIntervalDto(
    Guid Id,
    Guid JobId,
    string JobNo,
    string VehicleRegistration,
    long TechnicianStaffId,
    string TechnicianName,
    string Kind,
    DateTime StartedAt,
    DateTime? EndedAt,
    string? EndReason,
    /// <summary>วินาทีที่ลงมือจริง — null เมื่อคาบยังเปิดอยู่ (ให้ client เดินนาฬิกาเองจาก StartedAt)</summary>
    long? DurationSeconds,
    bool IsRework,
    bool IsAutoCapped,
    bool IsVoided,
    DateTime? EditedAt,
    string? EditReason,
    string? VoidReason);

/// <summary>
/// ผลของการเริ่มจับเวลา — <see cref="ClosedPrevious"/> ไม่ null เมื่อระบบปิดคาบของคันเดิมให้ในคำขอเดียวกัน
/// แอปใช้ค่านี้ขึ้นข้อความ "หยุดเวลา {ทะเบียน} ที่ {เวลา} แล้ว" (docs/09 §9)
/// </summary>
public sealed record StartWorkResultDto(
    WorkIntervalDto Current,
    WorkIntervalDto? ClosedPrevious,
    DateTime ServerNow);

/// <summary>
/// สถานะการจับเวลาของผู้เรียกตอนนี้ — <see cref="Current"/> null = ไม่ได้กำลังทำงานอยู่
/// [BIZ] ServerNow ต้องมาคู่กันเสมอ เพราะแอปห้ามใช้นาฬิกาเครื่องคำนวณเวลาที่ผ่านไป (docs/09 §5 กฎข้อ 3)
/// </summary>
public sealed record CurrentWorkDto(
    WorkIntervalDto? Current,
    DateTime ServerNow,
    /// <summary>คาบที่เพิ่งถูกระบบตัดให้เพราะลืมกดหยุด — ให้แอปแจ้งผู้ใช้ได้ว่าเกิดอะไรขึ้น</summary>
    WorkIntervalDto? AutoCappedPrevious);

public sealed record StartWorkRequest(Guid RequestId);
public sealed record PauseWorkRequest(Guid RequestId, string? Reason);
public sealed record ResumeWorkRequest(Guid RequestId);
public sealed record StopWorkRequest(Guid RequestId, string? Reason);
public sealed record EditWorkIntervalRequest(DateTime StartedAt, DateTime EndedAt, string Reason);
public sealed record VoidWorkIntervalRequest(string Reason);

public static class WorkMapper
{
    public static WorkIntervalDto ToDto(WorkInterval x, Job? job = null) => new(
        x.Id,
        x.JobId,
        job?.JobNo ?? "",
        job?.VehicleRegistration ?? "",
        x.TechnicianStaffId,
        x.TechnicianName,
        ToKindToken(x.Kind),
        x.StartedAt,
        x.EndedAt,
        x.EndReason is null ? null : ToReasonToken(x.EndReason.Value),
        x.Duration is null ? null : (long)x.Duration.Value.TotalSeconds,
        x.IsRework,
        x.IsAutoCapped,
        x.VoidedAt is not null,
        x.EditedAt,
        x.EditReason,
        x.VoidReason);

    public static string ToKindToken(WorkIntervalKind kind) => kind switch
    {
        WorkIntervalKind.Pause => "pause",
        _ => "work"
    };

    public static string ToReasonToken(WorkEndReason reason) => reason switch
    {
        WorkEndReason.SwitchedJob => "switchedJob",
        WorkEndReason.Paused => "paused",
        WorkEndReason.Resumed => "resumed",
        WorkEndReason.WaitParts => "waitParts",
        WorkEndReason.SentToQc => "sentToQc",
        WorkEndReason.JobClosed => "jobClosed",
        WorkEndReason.ShiftClosed => "shiftClosed",
        WorkEndReason.AutoCapped => "autoCapped",
        _ => "manual"
    };
}
