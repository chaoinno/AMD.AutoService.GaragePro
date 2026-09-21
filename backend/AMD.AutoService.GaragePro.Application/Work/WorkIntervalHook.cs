using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Work;

/// <summary>
/// ดูสัญญาและเหตุผลเรื่อง dependency ทั้งหมดที่ <see cref="IWorkIntervalHook"/>
/// คลาสนี้ตั้งใจให้เล็กและไม่รู้จักอะไรนอกจาก repository กับนาฬิกา
/// </summary>
public sealed class WorkIntervalHook(
    IWorkIntervalRepository repository,
    TimeProvider clock) : IWorkIntervalHook
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<int> CloseOpenForJobAsync(Guid jobId, WorkEndReason reason, CancellationToken ct = default)
    {
        var open = await repository.GetOpenByJobAsync(jobId, ct);
        foreach (var interval in open)
            Close(interval, reason, Now, autoCapped: false, endedByUserId: null);

        return open.Count;
    }

    public async Task<WorkInterval?> CloseOpenForTechnicianAsync(
        string shardKey, int branchId, long staffId,
        WorkEndReason reason, DateTime endedAt, bool autoCapped, long endedByUserId,
        CancellationToken ct = default)
    {
        var interval = await repository.GetOpenByTechnicianAsync(shardKey, branchId, staffId, ct);
        if (interval is null) return null;

        Close(interval, reason, endedAt, autoCapped, endedByUserId);
        return interval;
    }

    /// <summary>
    /// ป้องกัน EndedAt ย้อนก่อน StartedAt ซึ่ง CK_WorkInterval_Range จะปฏิเสธที่ฐานข้อมูลอยู่แล้ว —
    /// เกิดได้จริงเมื่อเพดานตัดคาบ (เวลาสิ้นกะ) ย้อนหลังกว่าเวลาที่ช่างเพิ่งกดเริ่ม
    /// </summary>
    private static void Close(
        WorkInterval interval, WorkEndReason reason, DateTime endedAt, bool autoCapped, long? endedByUserId)
    {
        interval.EndedAt = endedAt < interval.StartedAt ? interval.StartedAt : endedAt;
        interval.EndReason = reason;
        interval.IsAutoCapped = autoCapped;
        if (endedByUserId is long id) interval.EndedByUserId = id;
    }
}
