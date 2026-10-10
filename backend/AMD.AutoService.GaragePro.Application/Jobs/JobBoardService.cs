using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;

namespace AMD.AutoService.GaragePro.Application.Jobs;

/// <summary>
/// บอร์ดสถานะรถในอู่สำหรับ monitor — **ดูอย่างเดียว** ไม่มีการเปลี่ยนสถานะผ่านบอร์ด
/// [BIZ] transition เกือบทุกเส้นมี guard ที่คำนวณจากข้อมูลจริง (ลายเซ็น/QC/ใบเสร็จ/ส่งมอบ) หรือจำกัด role/source
/// การลากวางจึงจะเด้งกลับหรือกลายเป็นช่อง manual-override — เปลี่ยนสถานะต้องทำในการ์ดจ๊อบเท่านั้น
/// เฉพาะรถในอู่: จ๊อบที่ปิดแล้วถูกเปลี่ยนเป็นประเภท "ปิดจ๊อบ" เอง (กฎข้อ 14) จึงไม่หลุดเข้ามาปน
/// </summary>
public sealed class JobBoardService(
    IJobBoardRepository board,
    IJobChatRepository chat,
    ICurrentUser user,
    TimeProvider clock)
{
    internal const int BoardRowLimit = 300;

    public async Task<Result<JobBoardDto>> GetAsync(string? keyword, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var rows = await board.GetOpenInShopJobsAsync(user.ShardKey, user.BranchId, keyword, BoardRowLimit + 1, ct);
        var truncated = rows.Count > BoardRowLimit;
        var jobs = truncated ? rows.Take(BoardRowLimit).ToList() : rows;
        var jobIds = jobs.Select(j => j.Id).ToList();

        IReadOnlyDictionary<Guid, DateTime> lastChange = new Dictionary<Guid, DateTime>();
        ILookup<Guid, JobBoardWorkerDto> workers = Array.Empty<JobBoardWorkerDto>().ToLookup(_ => Guid.Empty);
        ILookup<Guid, string> awaiting = Array.Empty<string>().ToLookup(_ => Guid.Empty);
        Dictionary<Guid, Guid> latestChat = [];

        if (jobIds.Count > 0)
        {
            lastChange = await board.GetLastStatusChangeAsync(jobIds, ct);

            workers = (await board.GetOpenWorkIntervalsAsync(jobIds, ct))
                .OrderBy(w => w.StartedAt)
                .ToLookup(w => w.JobId, w => new JobBoardWorkerDto(
                    w.TechnicianName, w.Kind == WorkIntervalKind.Pause ? "pause" : "work", w.StartedAt));

            awaiting = JobQuotations.AwaitingCustomer(await board.GetSentQuotationsAsync(jobIds, ct))
                .OrderBy(q => q.Version)
                .ToLookup(q => q.JobId, q => q.Code);

            latestChat = (await chat.GetLatestPerJobAsync(user.ShardKey, user.BranchId, jobIds, ct))
                .ToDictionary(m => m.JobId, m => m.MessageId);
        }

        var cards = jobs
            .Select(job => new JobBoardCardDto(
                JobMapper.ToDto(job, now),
                lastChange.TryGetValue(job.Id, out var since) ? since : job.CreatedAt,
                workers[job.Id].ToList(),
                awaiting[job.Id].ToList(),
                latestChat.TryGetValue(job.Id, out var messageId) ? messageId : null))
            .ToList();

        // [UI] อยู่ในสถานะนานที่สุดขึ้นก่อน — ลำดับในคอลัมน์บอกคอขวดเอง ไม่ต้องมีเกณฑ์สีที่ยังไม่มีใครยืนยัน
        var columns = JobService.OpenStatusOrder
            .Select(status =>
            {
                var token = JobStateMachine.ToToken(status);
                return new JobBoardColumnDto(
                    token,
                    JobStateMachine.Describe(status),
                    cards.Where(c => c.Job.Status == token)
                        .OrderBy(c => c.StatusSince)
                        .ThenBy(c => c.Job.JobNo, StringComparer.Ordinal)
                        .ToList());
            })
            .ToList();

        return Result<JobBoardDto>.Ok(new JobBoardDto(columns, cards.Count, truncated, BoardRowLimit, now));
    }
}
