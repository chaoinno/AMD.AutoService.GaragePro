using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Jobs;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;

namespace AMD.AutoService.GaragePro.Application.Work;

/// <summary>ค่าที่ยังเป็น [ASSUME] ของ docs/09 §12 — ตั้งทับได้ที่ section "Work" ของ configuration</summary>
public sealed class WorkTimeOptions
{
    /// <summary>[ASSUME] แก้เวลาย้อนหลังได้กี่วัน — ให้ทันรอบเดือน แต่ไม่เปิดให้แก้ของเก่าไม่รู้จบ</summary>
    public int EditWindowDays { get; set; } = 7;

    /// <summary>
    /// [ASSUME] เพดานคาบที่ลืมปิดเมื่อหากะไม่เจอ — เกิดกับคาบที่เปิดจากเว็บซึ่งไม่มี ShiftSession
    /// (docs/09 §5.6 สมมติว่ามีกะเสมอ ซึ่งไม่จริง)
    /// </summary>
    public int MaxOpenIntervalHours { get; set; } = 12;
}

public interface IWorkTimeService
{
    Task<Result<StartWorkResultDto>> StartAsync(Guid jobId, StartWorkRequest request, CancellationToken ct = default);
    Task<Result<WorkIntervalDto>> PauseAsync(Guid jobId, PauseWorkRequest request, CancellationToken ct = default);
    Task<Result<StartWorkResultDto>> ResumeAsync(Guid jobId, ResumeWorkRequest request, CancellationToken ct = default);
    Task<Result<WorkIntervalDto>> StopAsync(Guid jobId, StopWorkRequest request, CancellationToken ct = default);
    Task<Result<CurrentWorkDto>> GetCurrentAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<WorkIntervalDto>>> GetByJobAsync(Guid jobId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<WorkIntervalDto>>> SearchAsync(
        DateTime? fromDate, DateTime? toDate, bool onlyNeedsReview, CancellationToken ct = default);
    Task<Result<WorkIntervalDto>> EditAsync(Guid id, EditWorkIntervalRequest request, CancellationToken ct = default);
    Task<Result<WorkIntervalDto>> VoidAsync(Guid id, VoidWorkIntervalRequest request, CancellationToken ct = default);
}

/// <summary>
/// จับเวลาการทำงานของช่าง (docs/09-technician-time-tracking.md) — เก็บข้อมูลดิบอย่างเดียว
/// ไม่ผูกเงินและยังไม่มีรายงานในรอบนี้
///
/// [BIZ] เปิด/ปิด/พักคาบเป็นของ Technician เท่านั้น (ยืนยันกับผู้ใช้ 2026-09-21 — หัวหน้าช่างคุม/ตรวจ
/// ไม่ลงมือ) · แก้เวลาย้อนหลังเป็นของ Lead/Manager · ยกเลิกคาบเป็นของ Manager
/// ผลพลอยได้คือไม่ต้องแตะ JobStateMachine เลย เพราะ Approved→InProgress มี Technician + Mobile อยู่แล้ว
///
/// [SECURITY] ตัวตนของ "ช่าง" มาจาก JWT เสมอ — <b>ห้ามรับ staffId จาก request body</b>
/// ไม่งั้นช่างลงเวลาแทนคนอื่นได้
/// </summary>
public sealed class WorkTimeService(
    IWorkIntervalRepository repo,
    IJobRepository jobs,
    IQuotationRepository quotations,
    IJobService jobService,
    ILegacyUserReader legacyUsers,
    ICurrentUser user,
    TimeProvider clock,
    WorkTimeOptions options) : IWorkTimeService
{
    /// <summary>ทุกที่ในโปรเจกต์นี้แปลงเวลาไทยด้วยการบวก/ลบ 7 ชม. ตรงๆ (ดู JobService/ReportsService)</summary>
    private const int ThaiOffsetHours = 7;

    /// ช่วงเริ่มต้นของหน้าตรวจคุณภาพข้อมูลเมื่อไม่ได้ระบุวันที่มา
    private const int DefaultSearchDays = 30;

    /// เพดานแถวของหน้าตรวจ — สาขาหนึ่งมีช่างไม่กี่คน 30 วันไม่ควรเกินนี้ ถ้าชนแปลว่าช่วงกว้างเกินไป
    private const int SearchLimit = 500;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------- เริ่ม / พัก / ทำต่อ / หยุด ----------

    public async Task<Result<StartWorkResultDto>> StartAsync(
        Guid jobId, StartWorkRequest request, CancellationToken ct = default)
    {
        var context = await ResolveAsync(jobId, ct);
        if (!context.Success) return Result<StartWorkResultDto>.Fail(context.Error!);
        var (job, staffId) = context.Data!;

        if (request.RequestId == Guid.Empty)
            return Result<StartWorkResultDto>.Fail(
                "WORK_VALIDATION", "ไม่พบรหัสคำขอ (RequestId)", nameof(request.RequestId));

        // กดรัวๆ ต้องไม่ได้หลายช่วง — เจอแล้วคืนของเดิมโดยไม่เขียนอะไรเพิ่ม
        var replay = await repo.GetByRequestIdAsync(request.RequestId, ct);
        if (replay is not null) return Result<StartWorkResultDto>.Ok(await ReplayAsync(replay, job, ct));

        var rework = ClassifyJobForWork(job.Status);
        if (rework is null)
            return Result<StartWorkResultDto>.Fail(
                "WORK_JOB_NOT_REPAIRABLE",
                $"งานนี้อยู่สถานะ “{JobStateMachine.Describe(job.Status)}” — เริ่มจับเวลาได้เมื่ออนุมัติซ่อมแล้วเท่านั้น");

        // ต้องดันสถานะ "ก่อน" mutate ของเรา เพราะ JobService.TransitionAsync มี SaveChanges ของตัวเอง
        // ถ้าสลับลำดับ SaveChanges ของมันจะ flush คาบที่เรายังสร้างไม่เสร็จไปด้วย
        if (job.Status == JobStatus.Approved)
        {
            var moved = await jobService.TransitionAsync(jobId, new TransitionJobRequest("inprogress", null), ct);
            if (!moved.Success) return Result<StartWorkResultDto>.Fail(moved.Error!);
        }

        var previous = await CloseOpenForCurrentTechnicianAsync(staffId, WorkEndReason.SwitchedJob, ct);
        var interval = NewInterval(job, staffId, WorkIntervalKind.Work, request.RequestId);
        interval.IsRework = rework.Value;
        interval.ClosedPreviousIntervalId = previous?.Id;

        await AttributeJobToTechnicianAsync(job, staffId, ct);
        await repo.AddAsync(interval, ct);
        await WriteEventAsync(job, interval, "work.started",
            interval.IsRework ? "เริ่มจับเวลาแก้ไขงานหลังตรวจ QC" : "เริ่มจับเวลาทำงาน", ct);

        var saved = await CommitAsync(request.RequestId, ct);
        if (!saved.Success) return Result<StartWorkResultDto>.Fail(saved.Error!);

        return Result<StartWorkResultDto>.Ok(new StartWorkResultDto(
            WorkMapper.ToDto(interval, job),
            previous is null ? null : WorkMapper.ToDto(previous, await JobOfAsync(previous.JobId, job, ct)),
            Now));
    }

    public async Task<Result<WorkIntervalDto>> PauseAsync(
        Guid jobId, PauseWorkRequest request, CancellationToken ct = default)
    {
        var context = await ResolveAsync(jobId, ct);
        if (!context.Success) return Result<WorkIntervalDto>.Fail(context.Error!);
        var (job, staffId) = context.Data!;

        if (request.RequestId == Guid.Empty)
            return Result<WorkIntervalDto>.Fail("WORK_VALIDATION", "ไม่พบรหัสคำขอ (RequestId)", nameof(request.RequestId));

        var replay = await repo.GetByRequestIdAsync(request.RequestId, ct);
        if (replay is not null) return Result<WorkIntervalDto>.Ok(WorkMapper.ToDto(replay, job));

        var open = await repo.GetOpenByTechnicianAsync(staffId, ct);
        if (open is null || open.JobId != jobId)
            return Result<WorkIntervalDto>.Fail("WORK_NOT_TRACKING", "ตอนนี้ยังไม่ได้จับเวลางานคันนี้อยู่");
        if (open.Kind == WorkIntervalKind.Pause)
            return Result<WorkIntervalDto>.Fail("WORK_ALREADY_PAUSED", "กำลังพักอยู่แล้ว");

        CloseNow(open, WorkEndReason.Paused);
        var pause = NewInterval(job, staffId, WorkIntervalKind.Pause, request.RequestId);
        pause.IsRework = open.IsRework;
        pause.ClosedPreviousIntervalId = open.Id;

        await repo.AddAsync(pause, ct);
        await WriteEventAsync(job, pause, "work.paused",
            string.IsNullOrWhiteSpace(request.Reason) ? "พักงาน" : $"พักงาน — {request.Reason!.Trim()}", ct);

        var saved = await CommitAsync(request.RequestId, ct);
        return saved.Success
            ? Result<WorkIntervalDto>.Ok(WorkMapper.ToDto(pause, job))
            : Result<WorkIntervalDto>.Fail(saved.Error!);
    }

    public async Task<Result<StartWorkResultDto>> ResumeAsync(
        Guid jobId, ResumeWorkRequest request, CancellationToken ct = default)
    {
        var context = await ResolveAsync(jobId, ct);
        if (!context.Success) return Result<StartWorkResultDto>.Fail(context.Error!);
        var (job, staffId) = context.Data!;

        if (request.RequestId == Guid.Empty)
            return Result<StartWorkResultDto>.Fail("WORK_VALIDATION", "ไม่พบรหัสคำขอ (RequestId)", nameof(request.RequestId));

        var replay = await repo.GetByRequestIdAsync(request.RequestId, ct);
        if (replay is not null) return Result<StartWorkResultDto>.Ok(await ReplayAsync(replay, job, ct));

        var open = await repo.GetOpenByTechnicianAsync(staffId, ct);
        if (open is null || open.JobId != jobId || open.Kind != WorkIntervalKind.Pause)
            return Result<StartWorkResultDto>.Fail("WORK_NOT_PAUSED", "ตอนนี้ไม่ได้อยู่ระหว่างพักงานคันนี้");

        CloseNow(open, WorkEndReason.Resumed);
        var resumed = NewInterval(job, staffId, WorkIntervalKind.Work, request.RequestId);
        resumed.IsRework = open.IsRework;
        resumed.ClosedPreviousIntervalId = open.Id;

        await repo.AddAsync(resumed, ct);
        await WriteEventAsync(job, resumed, "work.resumed", "กลับมาทำงานต่อ", ct);

        var saved = await CommitAsync(request.RequestId, ct);
        return saved.Success
            ? Result<StartWorkResultDto>.Ok(new StartWorkResultDto(
                WorkMapper.ToDto(resumed, job), WorkMapper.ToDto(open, job), Now))
            : Result<StartWorkResultDto>.Fail(saved.Error!);
    }

    public async Task<Result<WorkIntervalDto>> StopAsync(
        Guid jobId, StopWorkRequest request, CancellationToken ct = default)
    {
        var context = await ResolveAsync(jobId, ct);
        if (!context.Success) return Result<WorkIntervalDto>.Fail(context.Error!);
        var (job, staffId) = context.Data!;

        var open = await repo.GetOpenByTechnicianAsync(staffId, ct);
        if (open is null || open.JobId != jobId)
            return Result<WorkIntervalDto>.Fail("WORK_NOT_TRACKING", "ตอนนี้ยังไม่ได้จับเวลางานคันนี้อยู่");

        CloseNow(open, WorkEndReason.Manual);
        await WriteEventAsync(job, open, "work.stopped",
            string.IsNullOrWhiteSpace(request.Reason) ? "หยุดจับเวลา" : $"หยุดจับเวลา — {request.Reason!.Trim()}", ct);
        await repo.SaveChangesAsync(ct);

        return Result<WorkIntervalDto>.Ok(WorkMapper.ToDto(open, job));
    }

    // ---------- อ่าน ----------

    /// <summary>
    /// สถานะการจับเวลาของผู้เรียกตอนนี้ — เปิดให้ทุกบทบาทอ่านของตัวเองได้ (คืน null ถ้าไม่มีคาบ)
    /// ไม่ต้อง 403 เพราะเป็นข้อมูลของตัวเองล้วนและแอปเรียกทุกครั้งที่เปิดขึ้นมา
    ///
    /// [BIZ] เมธอด GET นี้ "เขียน" ได้ในกรณีเดียว: ตัดคาบที่ลืมปิดให้ (auto-cap) — ยอมรับได้เพราะ
    /// idempotent, แตะแถวเดียวที่เป็นของผู้เรียกเอง และเป็นจุดที่รันจริงทุกเช้าโดยไม่ต้องรอใครกดอะไร
    /// ระบบนี้ไม่มี background worker (ไม่มี IHostedService ที่ไหนเลย) — ดูทางเลือกที่พิจารณาแล้วใน docs/09 §5.6
    /// </summary>
    public async Task<Result<CurrentWorkDto>> GetCurrentAsync(CancellationToken ct = default)
    {
        var staff = await CurrentStaffIdAsync(ct);
        if (!staff.Success) return Result<CurrentWorkDto>.Fail(staff.Error!);

        var open = await repo.GetOpenByTechnicianAsync(staff.Data, ct);
        if (open is null) return Result<CurrentWorkDto>.Ok(new CurrentWorkDto(null, Now, null));

        var job = await jobs.GetAsync(open.JobId, ct);
        var cap = await ComputeCapAsync(open, ct);
        if (cap is null) return Result<CurrentWorkDto>.Ok(new CurrentWorkDto(WorkMapper.ToDto(open, job), Now, null));

        CloseAt(open, WorkEndReason.AutoCapped, cap.Value, autoCapped: true);
        if (job is not null)
            await WriteEventAsync(job, open, "work.auto_capped",
                "ระบบปิดช่วงเวลาที่ค้างไว้ให้อัตโนมัติ เพราะเลยเวลาสิ้นกะแล้ว", ct, EventSource.System);
        await repo.SaveChangesAsync(ct);

        return Result<CurrentWorkDto>.Ok(new CurrentWorkDto(null, Now, WorkMapper.ToDto(open, job)));
    }

    /// <summary>
    /// คาบทั้งหมดของจ๊อบหนึ่ง — เปิดให้ทุกบทบาทที่เห็นจ๊อบนั้นอ่านได้ เพราะเป็นข้อมูลปฏิบัติการแบบเดียวกับ
    /// ประวัติกิจกรรม ไม่มีตัวเงินเข้ามาเกี่ยว (ต่างจากรายงานประเมินรายบุคคลที่จะจำกัดสิทธิ์เมื่อทำในรอบหน้า)
    /// </summary>
    public async Task<Result<IReadOnlyList<WorkIntervalDto>>> GetByJobAsync(
        Guid jobId, CancellationToken ct = default)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return Result<IReadOnlyList<WorkIntervalDto>>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");
        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<IReadOnlyList<WorkIntervalDto>>.Fail(
                "JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        var intervals = await repo.GetByJobAsync(jobId, ct);
        return Result<IReadOnlyList<WorkIntervalDto>>.Ok(
            intervals.Select(x => WorkMapper.ToDto(x, job)).ToList());
    }

    /// <summary>
    /// รายการคาบของทั้งสาขา — สำหรับหน้าตรวจคุณภาพข้อมูลของหัวหน้าช่าง/ผู้จัดการ
    /// จำกัดสิทธิ์เท่ากับคนที่แก้เวลาได้ เพราะเป็นข้อมูลการทำงานรายบุคคลของทั้งทีม
    /// ไม่ใช่ข้อมูลปฏิบัติการของจ๊อบเดียวแบบ <see cref="GetByJobAsync"/>
    /// </summary>
    public async Task<Result<IReadOnlyList<WorkIntervalDto>>> SearchAsync(
        DateTime? fromDate, DateTime? toDate, bool onlyNeedsReview, CancellationToken ct = default)
    {
        if (user.Role is not (UserRole.Lead or UserRole.Manager))
            return Result<IReadOnlyList<WorkIntervalDto>>.Fail(
                "WORK_FORBIDDEN", "เฉพาะหัวหน้าช่างหรือผู้จัดการเท่านั้นที่ดูเวลาทำงานของทั้งทีมได้");

        var to = toDate ?? Now;
        var from = fromDate ?? to.AddDays(-DefaultSearchDays);
        if (to < from)
            return Result<IReadOnlyList<WorkIntervalDto>>.Fail(
                "WORK_VALIDATION", "ช่วงวันที่ไม่ถูกต้อง", nameof(toDate));

        var intervals = await repo.SearchAsync(from, to, onlyNeedsReview, SearchLimit, ct);

        // ดึงจ๊อบทีละใบเพราะรายการถูกจำกัดจำนวนไว้แล้วและจ๊อบซ้ำกันเยอะ (คนละคันไม่กี่คัน)
        var cache = new Dictionary<Guid, Job?>();
        var rows = new List<WorkIntervalDto>(intervals.Count);
        foreach (var interval in intervals)
        {
            if (!cache.TryGetValue(interval.JobId, out var job))
            {
                job = await jobs.GetAsync(interval.JobId, ct);
                cache[interval.JobId] = job;
            }
            rows.Add(WorkMapper.ToDto(interval, job));
        }

        return Result<IReadOnlyList<WorkIntervalDto>>.Ok(rows);
    }

    // ---------- แก้ไขย้อนหลัง ----------

    public async Task<Result<WorkIntervalDto>> EditAsync(
        Guid id, EditWorkIntervalRequest request, CancellationToken ct = default)
    {
        if (user.Role is not (UserRole.Lead or UserRole.Manager))
            return Result<WorkIntervalDto>.Fail(
                "WORK_FORBIDDEN", "เฉพาะหัวหน้าช่างหรือผู้จัดการเท่านั้นที่แก้เวลาย้อนหลังได้");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<WorkIntervalDto>.Fail("WORK_VALIDATION", "ต้องระบุเหตุผลที่แก้เวลา", nameof(request.Reason));

        var interval = await repo.GetAsync(id, ct);
        if (interval is null) return Result<WorkIntervalDto>.Fail("WORK_NOT_FOUND", "ไม่พบช่วงเวลาที่ต้องการแก้");
        if (interval.VoidedAt is not null)
            return Result<WorkIntervalDto>.Fail("WORK_ALREADY_VOIDED", "ช่วงเวลานี้ถูกยกเลิกไปแล้ว");
        if (interval.IsOpen)
            return Result<WorkIntervalDto>.Fail(
                "WORK_STILL_OPEN", "ช่วงเวลานี้ยังจับเวลาอยู่ — ต้องให้หยุดก่อนจึงจะแก้ย้อนหลังได้");

        if (request.EndedAt < request.StartedAt)
            return Result<WorkIntervalDto>.Fail(
                "WORK_VALIDATION", "เวลาสิ้นสุดต้องไม่น้อยกว่าเวลาเริ่ม", nameof(request.EndedAt));
        if (request.EndedAt > Now)
            return Result<WorkIntervalDto>.Fail(
                "WORK_VALIDATION", "เวลาสิ้นสุดต้องไม่เป็นอนาคต", nameof(request.EndedAt));

        var deadline = interval.StartedAt.AddDays(options.EditWindowDays);
        if (Now > deadline)
            return Result<WorkIntervalDto>.Fail(
                "WORK_EDIT_WINDOW_EXPIRED",
                $"ช่วงเวลานี้เกิน {options.EditWindowDays} วันแล้ว แก้ย้อนหลังไม่ได้");

        interval.StartedAt = request.StartedAt;
        interval.EndedAt = request.EndedAt;
        interval.EditedByUserId = user.UserId;
        interval.EditedAt = Now;
        interval.EditReason = request.Reason.Trim();
        // คนแก้ยืนยันเวลาจริงให้แล้ว คาบนี้จึงกลับเข้าการคำนวณได้ (docs/09 §5.6)
        interval.IsAutoCapped = false;

        var job = await jobs.GetAsync(interval.JobId, ct);
        if (job is not null)
            await WriteEventAsync(job, interval, "work.edited",
                $"แก้เวลาทำงานของ {interval.TechnicianName} — {interval.EditReason}", ct);
        await repo.SaveChangesAsync(ct);

        return Result<WorkIntervalDto>.Ok(WorkMapper.ToDto(interval, job));
    }

    public async Task<Result<WorkIntervalDto>> VoidAsync(
        Guid id, VoidWorkIntervalRequest request, CancellationToken ct = default)
    {
        if (user.Role != UserRole.Manager)
            return Result<WorkIntervalDto>.Fail("WORK_FORBIDDEN", "เฉพาะผู้จัดการเท่านั้นที่ยกเลิกช่วงเวลาได้");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Result<WorkIntervalDto>.Fail("WORK_VALIDATION", "ต้องระบุเหตุผลที่ยกเลิก", nameof(request.Reason));

        var interval = await repo.GetAsync(id, ct);
        if (interval is null) return Result<WorkIntervalDto>.Fail("WORK_NOT_FOUND", "ไม่พบช่วงเวลาที่ต้องการยกเลิก");
        if (interval.VoidedAt is not null)
            return Result<WorkIntervalDto>.Fail("WORK_ALREADY_VOIDED", "ช่วงเวลานี้ถูกยกเลิกไปแล้ว");

        // ยกเลิกคาบที่ยังเปิดอยู่ต้องปิดด้วย ไม่งั้นมันจะกินโควตา "หนึ่งช่วงเปิดต่อช่าง" ค้างไว้ตลอด
        if (interval.IsOpen) CloseNow(interval, WorkEndReason.Manual);

        interval.VoidedAt = Now;
        interval.VoidedByUserId = user.UserId;
        interval.VoidReason = request.Reason.Trim();

        var job = await jobs.GetAsync(interval.JobId, ct);
        if (job is not null)
            await WriteEventAsync(job, interval, "work.voided",
                $"ยกเลิกช่วงเวลาของ {interval.TechnicianName} — {interval.VoidReason}", ct);
        await repo.SaveChangesAsync(ct);

        return Result<WorkIntervalDto>.Ok(WorkMapper.ToDto(interval, job));
    }

    // ---------- helpers ----------

    /// <summary>
    /// ตรวจสิทธิ์ + จ๊อบ + สาขา แล้วคืน (job, staffId) ให้ครบในทีเดียว — ทุก mutation ต้องผ่านที่นี่
    /// </summary>
    private async Task<Result<(Job Job, long StaffId)>> ResolveAsync(Guid jobId, CancellationToken ct)
    {
        if (user.Role != UserRole.Technician)
            return Result<(Job, long)>.Fail(
                "WORK_FORBIDDEN", "เฉพาะช่างเท่านั้นที่จับเวลาการทำงานได้");

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null) return Result<(Job, long)>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");
        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<(Job, long)>.Fail("JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        var staff = await CurrentStaffIdAsync(ct);
        return staff.Success
            ? Result<(Job, long)>.Ok((job, staff.Data))
            : Result<(Job, long)>.Fail(staff.Error!);
    }

    /// <summary>
    /// Staff.Id ของผู้ใช้ปัจจุบัน — มาจาก claim เป็นหลัก · fallback ไปอ่าน legacy เฉพาะ token ที่ออกก่อน
    /// จะมี claim นี้ (หน้าต่าง ≤12 ชม. ตาม JwtOptions.SessionHours) ซึ่งจะหายไปเองหลังผ่านวันแรก
    /// รวมไว้ที่เดียวเพื่อไม่ให้ fallback กระจายไปทั้ง service
    /// </summary>
    private async Task<Result<long>> CurrentStaffIdAsync(CancellationToken ct)
    {
        if (user.StaffId is long fromClaim && fromClaim > 0) return Result<long>.Ok(fromClaim);

        var legacy = await legacyUsers.FindByIdAsync(user.ShardKey, user.UserId, ct);
        if (legacy?.StaffId is long fromLegacy && fromLegacy > 0) return Result<long>.Ok(fromLegacy);

        return Result<long>.Fail(
            "WORK_NO_STAFF_PROFILE", "บัญชีนี้ไม่ได้ผูกกับข้อมูลพนักงาน — ออกจากระบบแล้วเข้าใหม่อีกครั้ง");
    }

    /// <summary>null = สถานะนี้จับเวลาไม่ได้ · true = เป็นการกลับมาแก้งานหลังเข้า QC (docs/09 §5.5)</summary>
    private static bool? ClassifyJobForWork(JobStatus status) => status switch
    {
        JobStatus.Approved or JobStatus.InProgress or JobStatus.WaitParts => false,
        JobStatus.Qc => true,
        _ => null
    };

    private WorkInterval NewInterval(Job job, long staffId, WorkIntervalKind kind, Guid requestId) => new()
    {
        LegacyShardKey = user.ShardKey,
        LegacyBranchId = user.BranchId,
        JobId = job.Id,
        TechnicianStaffId = staffId,
        TechnicianName = user.UserName,
        Kind = kind,
        StartedAt = Now,
        ShiftSessionId = user.SessionId,
        RequestId = requestId,
        CreatedByUserId = user.UserId
    };

    private void CloseNow(WorkInterval interval, WorkEndReason reason) =>
        CloseAt(interval, reason, Now, autoCapped: false);

    private void CloseAt(WorkInterval interval, WorkEndReason reason, DateTime endedAt, bool autoCapped)
    {
        interval.EndedAt = endedAt < interval.StartedAt ? interval.StartedAt : endedAt;
        interval.EndReason = reason;
        interval.IsAutoCapped = autoCapped;
        interval.EndedByUserId = user.UserId;
    }

    /// <summary>ปิดคาบที่ค้างของช่างคนนี้ — ถ้าเลยเพดานแล้วให้ถือเป็น auto-cap ไม่ใช่การสลับคันปกติ</summary>
    private async Task<WorkInterval?> CloseOpenForCurrentTechnicianAsync(
        long staffId, WorkEndReason reason, CancellationToken ct)
    {
        var open = await repo.GetOpenByTechnicianAsync(staffId, ct);
        if (open is null) return null;

        var cap = await ComputeCapAsync(open, ct);
        if (cap is not null) CloseAt(open, WorkEndReason.AutoCapped, cap.Value, autoCapped: true);
        else CloseNow(open, reason);

        return open;
    }

    /// <summary>
    /// เวลาที่ควรตัดคาบที่ลืมปิด — null = ยังไม่ถึงเวลาตัด
    ///
    /// [BIZ] กะข้ามเที่ยงคืนมีจริง (AuthService.DefaultShifts มี "กะดึก" 22:00→08:00) จึงหา "การเกิดขึ้น
    /// ครั้งแรกของเวลาสิ้นกะที่หลังเวลาเริ่ม" ไม่ใช่เอาวันเดียวกับที่เริ่มเสมอ — ผิดตรงนี้แล้วชั่วโมงจะเพี้ยน
    /// ทั้งระบบโดยไม่มีอาการฟ้อง (บั๊กตระกูลเดียวกับ +7 ชม. ที่เคยเจ็บมาแล้ว)
    /// </summary>
    private async Task<DateTime?> ComputeCapAsync(WorkInterval interval, CancellationToken ct)
    {
        var cap = interval.StartedAt.AddHours(options.MaxOpenIntervalHours);

        if (interval.ShiftSessionId is Guid sessionId)
        {
            var shift = await repo.GetShiftForSessionAsync(sessionId, ct);
            if (shift is not null)
            {
                var startedLocal = interval.StartedAt.AddHours(ThaiOffsetHours);
                var endLocal = startedLocal.Date + shift.EndTime.ToTimeSpan();
                if (endLocal <= startedLocal) endLocal = endLocal.AddDays(1);
                cap = endLocal.AddHours(-ThaiOffsetHours);
            }
        }

        return Now > cap ? cap : null;
    }

    /// <summary>
    /// [BIZ] เขียน Job.AssignedTechnicianId ก็ต่อเมื่อยังว่าง <b>และ</b> ช่างคนนี้มีบรรทัดค่าแรงที่อนุมัติแล้ว
    /// บนจ๊อบนี้จริง — เพราะช่างหลายคนรุมคันเดียวได้ (docs/09 §5.3) คนแรกที่กดจึงไม่ใช่ "ช่างที่รับผิดชอบ"
    /// เสมอไป · คอลัมน์นี้ไม่เคยถูกเขียนมาก่อนเลยในระบบ (เป็นคอลัมน์ตายมาตลอด)
    /// </summary>
    private async Task AttributeJobToTechnicianAsync(Job job, long staffId, CancellationToken ct)
    {
        if (job.AssignedTechnicianId is not null) return;

        var quotation = await quotations.GetLatestForJobAsync(job.Id, ct);
        var owns = quotation?.Lines.Any(l =>
            l.Type == LineType.Labor
            && l.ApprovalStatus == LineApprovalStatus.Approved
            && l.AssignedTechnicianId == staffId) ?? false;

        if (owns) job.AssignedTechnicianId = staffId;
    }

    private async Task<StartWorkResultDto> ReplayAsync(WorkInterval interval, Job job, CancellationToken ct)
    {
        var previous = interval.ClosedPreviousIntervalId is Guid id ? await repo.GetAsync(id, ct) : null;
        return new StartWorkResultDto(
            WorkMapper.ToDto(interval, job),
            previous is null ? null : WorkMapper.ToDto(previous, await JobOfAsync(previous.JobId, job, ct)),
            Now);
    }

    private async Task<Job?> JobOfAsync(Guid jobId, Job known, CancellationToken ct) =>
        jobId == known.Id ? known : await jobs.GetAsync(jobId, ct);

    private Task WriteEventAsync(
        Job job, WorkInterval interval, string eventType, string descriptionTh,
        CancellationToken ct, EventSource? source = null) =>
        repo.AddEventAsync(new ActivityEvent
        {
            JobId = job.Id,
            EntityId = interval.Id,
            EntityType = nameof(WorkInterval),
            EventType = eventType,
            DescriptionTh = descriptionTh,
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = source ?? user.Source,
            OccurredAt = Now
        }, ct);

    /// <summary>
    /// [BIZ] ปิดคาบเดิม + เปิดคาบใหม่ ต้องสำเร็จหรือล้มไปด้วยกัน — SaveChanges ครั้งเดียวคือ implicit
    /// transaction ของ EF อยู่แล้ว ถ้า INSERT ชน UX_WorkInterval_OneOpenPerTech ทั้งก้อนถูก rollback
    /// ⇒ "ถ้า start ล้ม คาบเดิมต้องยังเปิดอยู่" เป็นจริงโดยอัตโนมัติ ไม่ต้องมี transaction ของตัวเอง
    ///
    /// การชนแยกไม่ได้ที่ระดับ exception (จะชนได้ทั้ง index ของ RequestId และของ one-open-per-tech)
    /// จึง re-read ด้วย RequestId เพื่อตัดสิน — ห้าม parse ชื่อ index จากข้อความ SQL
    /// </summary>
    private async Task<Result<bool>> CommitAsync(Guid requestId, CancellationToken ct)
    {
        try
        {
            await repo.SaveChangesAsync(ct);
            return Result<bool>.Ok(true);
        }
        catch (WorkIntervalConflictException)
        {
            var existing = await repo.GetByRequestIdAsync(requestId, ct);
            return existing is not null
                ? Result<bool>.Ok(true)
                : Result<bool>.Fail(
                    "WORK_ALREADY_OPEN",
                    "คุณมีงานที่กำลังจับเวลาอยู่แล้ว กรุณาโหลดสถานะล่าสุดแล้วลองใหม่");
        }
    }
}
