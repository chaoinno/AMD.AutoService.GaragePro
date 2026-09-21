using AMD.AutoService.GaragePro.Application.Work;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// อยู่ในไฟล์ของตัวเองแทนที่จะเป็น nested class ตามธรรมเนียมของไฟล์เทสต์อื่น เพราะถูกใช้ทั้งใน
/// JobServiceTests และ AuthServiceTests และต้องตรวจได้ว่าถูกเรียกด้วยอะไรบ้าง
/// </summary>
internal sealed class FakeWorkIntervalHook : IWorkIntervalHook
{
    public List<(Guid JobId, WorkEndReason Reason)> ClosedJobs { get; } = [];
    public List<(string ShardKey, int BranchId, long StaffId, WorkEndReason Reason, bool AutoCapped)> ClosedTechnicians { get; } = [];

    public Task<int> CloseOpenForJobAsync(Guid jobId, WorkEndReason reason, CancellationToken ct = default)
    {
        ClosedJobs.Add((jobId, reason));
        return Task.FromResult(0);
    }

    public Task<WorkInterval?> CloseOpenForTechnicianAsync(
        string shardKey, int branchId, long staffId,
        WorkEndReason reason, DateTime endedAt, bool autoCapped, long endedByUserId,
        CancellationToken ct = default)
    {
        ClosedTechnicians.Add((shardKey, branchId, staffId, reason, autoCapped));
        return Task.FromResult<WorkInterval?>(null);
    }
}
