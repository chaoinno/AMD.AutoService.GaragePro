using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Work;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;

namespace AMD.AutoService.GaragePro.Application.Jobs;

public interface IJobService
{
    Task<Result<JobDto>> GetAsync(Guid jobId, CancellationToken ct = default);

    Task<Result<IReadOnlyList<JobDto>>> SearchAsync(
        string? keyword, int take, DateTime? beforeCreatedAt, Guid? beforeJobId,
        int? jobTypeId, string? statusToken, CancellationToken ct = default);

    IReadOnlyList<JobStatusOptionDto> GetStatusOptions();

    /// <summary>จำนวนงานที่ยังไม่ปิดของสาขาปัจจุบัน — ใช้แสดงตัวเลขในเมนูจ๊อบ</summary>
    Task<Result<int>> CountOpenAsync(int? jobTypeId, CancellationToken ct = default);

    Task<Result<JobCountsDto>> CountsAsync(int? jobTypeId, CancellationToken ct = default);

    Task<Result<CreatedJobDto>> CreateAsync(CreateJobRequest request, CancellationToken ct = default);

    Task<Result<JobTransitionResultDto>> TransitionAsync(
        Guid jobId, TransitionJobRequest request, CancellationToken ct = default);

    /// <summary>เลื่อน/แก้วันเวลานัดหมาย — เฉพาะงานประเภทรถนัดหมายและยังไม่ถึงสถานะจบ</summary>
    Task<Result<JobDto>> UpdateAppointmentAsync(
        Guid jobId, UpdateJobAppointmentRequest request, CancellationToken ct = default);

    /// <summary>แปลงงานนัดหมาย (JobTypeId=10) เป็นรถในอู่ (JobTypeId=9) พร้อมบันทึกวันเวลาที่รถเข้าอู่จริง —
    /// ใช้เมื่อลูกค้านำรถเข้าจริงตามนัด (หรือมาก่อน/หลังนัดก็ได้ ไม่ผูกกับวันนัดหมายที่ตั้งไว้)</summary>
    Task<Result<JobDto>> ConvertToInShopAsync(
        Guid jobId, ConvertToInShopRequest request, CancellationToken ct = default);

    /// <summary>งานนัดหมายในช่วงเวลาที่กำหนด (มุมมองปฏิทิน) — กรองด้วย AppointmentAt ไม่ว่าง</summary>
    Task<Result<JobCalendarDto>> GetCalendarAsync(
        DateTimeOffset from, DateTimeOffset to, string? keyword, string? statusToken,
        CancellationToken ct = default);
}

/// <summary>
/// เจ้าของข้อมูลจ๊อบเพียงแหล่งเดียว (svc_Job) — ไม่เขียนกลับ legacy อีกต่อไป
/// [BIZ] ทุก state change เขียน ActivityEvent พร้อม Source เสมอ
/// อ้างอิง: docs/02-domain-model.md §Job · docs/05-legacy-db-mapping.md §5 (ข้อยกเว้น 2026-08-26 ถูกยกเลิก 2026-08-31)
/// </summary>
public sealed class JobService(
    IJobRepository jobs,
    IQuotationRepository quotations,
    IQcChecklistRepository qcChecklists,
    IPosRepository posRepo,
    IHandoverRepository handoverRepo,
    IJobNumberGenerator jobNumbers,
    ICustomerVehicleService customerVehicles,
    ILegacyReader legacy,
    IWorkIntervalHook workHook,
    ICurrentUser user,
    TimeProvider clock) : IJobService
{
    private const int InShopTypeId = 9;      // รถในอู่
    private const int AppointmentTypeId = 10; // รถนัดหมาย
    private const int ClosedTypeId = 11;      // ปิดจ๊อบ — ระบบตั้งเองเมื่อถึงสถานะจบ (ไม่ใช่ค่าที่เลือกตอนเปิดจ๊อบได้)
    private const string ClosedTypeName = "ปิดจ๊อบ";

    // [BIZ] วันนัดหมายห้ามน้อยกว่าวันเวลาปัจจุบัน (ยืนยันกับผู้ใช้ 2026-09-17) — เผื่อ tolerance สั้นๆ
    // กันเวลา client/server คลาดกันไม่กี่นาทีตอนเลือก "ตอนนี้เลย" พอดี ไม่ใช่ grace period ให้เลือกวันในอดีตจริงๆ
    private const int AppointmentPastToleranceMinutes = 5;
    // [ASSUME] เพดานอนาคตที่ยอมรับ — ยังไม่ได้ยืนยันกับเจ้าของระบบ
    private const int AppointmentMaxYearsAhead = 2;
    // [ASSUME] วันเข้าอู่จริงห้ามเป็นอนาคต (จะ "บันทึกว่ารถเข้าแล้ว" ล่วงหน้าไม่ได้) เผื่อ tolerance เดียวกัน
    private const int ArrivalFutureToleranceMinutes = 5;
    // [ASSUME] เพดานมุมมองปฏิทิน — กันดึงข้อมูลเกินจำเป็นเวลากรองช่วงกว้าง
    private const int CalendarMaxRangeDays = 92;
    private const int CalendarRowLimit = 500;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>[BIZ] AppointmentAt ไม่ว่าง ⟺ งานนี้เป็นรถนัดหมาย — บังคับให้ตรงกันเสมอที่ API
    /// (ห้ามเพิกเฉยเงียบๆ เมื่อ type 9 ส่งค่ามา เพราะจะทำให้มีข้อมูลที่ไม่มีหน้าจอไหนแสดง)</summary>
    private Result<DateTime?> ValidateAppointment(int jobTypeId, DateTimeOffset? appointmentAt)
    {
        if (jobTypeId == AppointmentTypeId && appointmentAt is null)
            return Result<DateTime?>.Fail(
                "JOB_VALIDATION", "กรุณาระบุวันเวลาที่ลูกค้าจะนำรถเข้า สำหรับงานประเภทรถนัดหมาย", "appointmentAt");

        if (jobTypeId != AppointmentTypeId && appointmentAt is not null)
            return Result<DateTime?>.Fail(
                "JOB_VALIDATION", "ระบุวันเวลานัดหมายได้เฉพาะงานประเภทรถนัดหมาย", "appointmentAt");

        if (appointmentAt is null)
            return Result<DateTime?>.Ok(null);

        var utc = appointmentAt.Value.UtcDateTime;
        if (utc < Now.AddMinutes(-AppointmentPastToleranceMinutes))
            return Result<DateTime?>.Fail(
                "JOB_VALIDATION", "วันเวลานัดหมายต้องไม่น้อยกว่าวันเวลาปัจจุบัน", "appointmentAt");
        if (utc > Now.AddYears(AppointmentMaxYearsAhead))
            return Result<DateTime?>.Fail(
                "JOB_VALIDATION", "วันเวลานัดหมายต้องไม่เกิน 2 ปีข้างหน้า", "appointmentAt");

        return Result<DateTime?>.Ok(utc);
    }

    public async Task<Result<JobDto>> GetAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return Result<JobDto>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        return Result<JobDto>.Ok(JobMapper.ToDto(job, Now));
    }

    public async Task<Result<IReadOnlyList<JobDto>>> SearchAsync(
        string? keyword, int take, DateTime? beforeCreatedAt, Guid? beforeJobId,
        int? jobTypeId, string? statusToken, CancellationToken ct = default)
    {
        JobStatus? status = null;
        if (!string.IsNullOrWhiteSpace(statusToken))
        {
            status = JobStateMachine.ParseToken(statusToken);
            if (status is null)
                return Result<IReadOnlyList<JobDto>>.Fail(
                    "JOB_STATUS_UNKNOWN", $"ไม่รู้จักสถานะ '{statusToken}'", nameof(statusToken));
        }

        var query = new JobSearchQuery(
            user.ShardKey, user.BranchId, keyword, take, beforeCreatedAt, beforeJobId, jobTypeId, status);
        var results = await jobs.SearchAsync(query, ct);

        return Result<IReadOnlyList<JobDto>>.Ok(results.Select(j => JobMapper.ToDto(j, Now)).ToList());
    }

    /// <summary>มุมมองปฏิทินนัดหมาย — คืนงานทุกงานที่มี AppointmentAt อยู่ในช่วง [from, to) เรียงจากนัดใกล้ที่สุด
    /// (ไม่ใช่ keyset cursor แบบ SearchAsync เพราะปฏิทินต้องการทุกแถวในเดือนนั้น ไม่ใช่หน้าแบ่งหน้า)</summary>
    public async Task<Result<JobCalendarDto>> GetCalendarAsync(
        DateTimeOffset from, DateTimeOffset to, string? keyword, string? statusToken, CancellationToken ct = default)
    {
        JobStatus? status = null;
        if (!string.IsNullOrWhiteSpace(statusToken))
        {
            status = JobStateMachine.ParseToken(statusToken);
            if (status is null)
                return Result<JobCalendarDto>.Fail(
                    "JOB_STATUS_UNKNOWN", $"ไม่รู้จักสถานะ '{statusToken}'", nameof(statusToken));
        }

        var fromUtc = from.UtcDateTime;
        var toUtc = to.UtcDateTime;
        if (toUtc <= fromUtc || (toUtc - fromUtc).TotalDays > CalendarMaxRangeDays)
            return Result<JobCalendarDto>.Fail(
                "JOB_CALENDAR_RANGE",
                "ช่วงวันที่ของปฏิทินต้องไม่เกิน 3 เดือนและวันสิ้นสุดต้องอยู่หลังวันเริ่มต้น");

        var appointmentQuery = new JobAppointmentQuery(
            user.ShardKey, user.BranchId, fromUtc, toUtc, keyword, status, CalendarRowLimit + 1);
        var rows = await jobs.GetAppointmentsAsync(appointmentQuery, ct);
        var truncated = rows.Count > CalendarRowLimit;
        var items = (truncated ? rows.Take(CalendarRowLimit) : rows).Select(j => JobMapper.ToDto(j, Now)).ToList();

        return Result<JobCalendarDto>.Ok(new JobCalendarDto(items, truncated, CalendarRowLimit));
    }

    public async Task<Result<int>> CountOpenAsync(int? jobTypeId, CancellationToken ct = default) =>
        Result<int>.Ok(await jobs.CountOpenAsync(user.ShardKey, user.BranchId, jobTypeId, ct));

    /// <summary>
    /// คืนสถานะที่ยังเดินต่อได้ **ครบทุกตัวเสมอ รวมที่เป็นศูนย์** เพื่อให้หน้าจอมีรายการคงที่
    /// ไม่กระโดดสลับตำแหน่งเวลาจำนวนเปลี่ยน · เรียงตามลำดับ lifecycle ไม่ใช่ตามจำนวน
    /// </summary>
    private static readonly JobStatus[] OpenStatusOrder =
    [
        JobStatus.WaitInspect, JobStatus.WaitQuote, JobStatus.WaitApprove, JobStatus.Approved,
        JobStatus.InProgress, JobStatus.WaitParts, JobStatus.Qc, JobStatus.Ready
    ];

    public async Task<Result<JobCountsDto>> CountsAsync(
        int? jobTypeId, CancellationToken ct = default)
    {
        var tallies = await jobs.CountOpenByStatusAsync(user.ShardKey, user.BranchId, jobTypeId, Now, ct);
        var byStatus = tallies.ToDictionary(t => t.Status);

        var items = OpenStatusOrder
            .Select(status => new JobStatusCountDto(
                JobStateMachine.ToToken(status),
                JobStateMachine.Describe(status),
                byStatus.TryGetValue(status, out var tally) ? tally.Count : 0))
            .ToList();

        return Result<JobCountsDto>.Ok(new JobCountsDto(
            TotalOpen: tallies.Sum(t => t.Count),
            Overdue: tallies.Sum(t => t.Overdue),
            ByStatus: items));
    }

    public IReadOnlyList<JobStatusOptionDto> GetStatusOptions() =>
        Enum.GetValues<JobStatus>()
            .Select(s => new JobStatusOptionDto(JobStateMachine.ToToken(s), JobStateMachine.Describe(s)))
            .ToList();

    public async Task<Result<CreatedJobDto>> CreateAsync(
        CreateJobRequest request, CancellationToken ct = default)
    {
        if (request.JobTypeId is not (InShopTypeId or AppointmentTypeId))
            return Result<CreatedJobDto>.Fail("JOB_VALIDATION", "กรุณาเลือกประเภทงาน", nameof(request.JobTypeId));

        var appointmentResult = ValidateAppointment(request.JobTypeId, request.AppointmentAt);
        if (!appointmentResult.Success)
            return Result<CreatedJobDto>.Fail(appointmentResult.Error!);
        var appointmentAt = appointmentResult.Data;

        var customerResult = await customerVehicles.GetCustomerAsync(request.CustomerId, ct);
        if (!customerResult.Success)
            return Result<CreatedJobDto>.Fail("CUSTOMER_NOT_FOUND", "ไม่พบข้อมูลลูกค้าที่เลือก", nameof(request.CustomerId));
        var customer = customerResult.Data!;

        var vehicleResult = await customerVehicles.GetVehicleAsync(request.VehicleId, ct);
        if (!vehicleResult.Success)
            return Result<CreatedJobDto>.Fail("VEHICLE_NOT_FOUND", "ไม่พบข้อมูลรถที่เลือก", nameof(request.VehicleId));
        var vehicle = vehicleResult.Data!;

        var openJob = await jobs.GetOpenByVehicleAsync(user.ShardKey, user.BranchId, request.VehicleId, ct);
        if (openJob is not null)
            return Result<CreatedJobDto>.Fail(
                "JOB_DUPLICATE_OPEN", "รถยนต์คันนี้มีงานที่ยังไม่เสร็จอยู่แล้ว กรุณาตรวจสอบอีกครั้ง");

        var jobNo = await jobNumbers.NextAsync(user.ShardKey, user.BranchId, Now.AddHours(7), ct);
        var branch = await legacy.GetBranchAsync(user.ShardKey, user.BranchId, ct);

        var job = new Job
        {
            LegacyShardKey = user.ShardKey,
            BranchId = user.BranchId,
            CustomerId = request.CustomerId,
            VehicleId = request.VehicleId,
            JobNo = jobNo,
            Status = JobStatus.WaitInspect,
            BranchName = branch?.Name ?? string.Empty,
            CustomerName = $"{customer.FirstName} {customer.LastName}".Trim(),
            CustomerPhone = customer.PhoneNumber1,
            VehicleRegistration = vehicle.Registration,
            VehicleModel = vehicle.ModelName,
            VehicleVin = vehicle.Vin,
            VehicleImagePath = vehicle.ImageUrl,
            JobTypeId = request.JobTypeId,
            JobTypeName = request.JobTypeId == InShopTypeId ? "รถในอู่" : "รถนัดหมาย",
            SenderName = string.IsNullOrWhiteSpace(request.SenderName) ? null : request.SenderName.Trim(),
            SenderPhoneNumber = string.IsNullOrWhiteSpace(request.SenderPhoneNumber) ? null : request.SenderPhoneNumber.Trim(),
            Detail = string.IsNullOrWhiteSpace(request.Detail) ? null : request.Detail.Trim(),
            AppointmentAt = appointmentAt,
            CreatedByUserId = user.UserId,
            CreatedByUserName = user.UserName,
            CreatedAt = Now,
            Source = user.Source
        };

        await jobs.AddAsync(job, ct);
        await jobs.AddEventAsync(new ActivityEvent
        {
            JobId = job.Id,
            EntityId = job.Id,
            EntityType = nameof(Job),
            EventType = "job.opened",
            DescriptionTh = appointmentAt is null
                ? $"เปิดจ๊อบ {jobNo}"
                : $"เปิดจ๊อบ {jobNo} · นัดหมาย {appointmentAt.Value.AddHours(7):dd/MM/yyyy HH:mm} น.",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await jobs.SaveChangesAsync(ct);

        return Result<CreatedJobDto>.Ok(new CreatedJobDto(job.Id, job.JobNo));
    }

    public async Task<Result<JobTransitionResultDto>> TransitionAsync(
        Guid jobId, TransitionJobRequest request, CancellationToken ct = default)
    {
        var to = JobStateMachine.ParseToken(request.ToStatus);
        if (to is null)
            return Result<JobTransitionResultDto>.Fail(
                "JOB_STATUS_UNKNOWN", $"ไม่รู้จักสถานะ '{request.ToStatus}'", nameof(request.ToStatus));

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null || job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<JobTransitionResultDto>.Fail(
                "JOB_NOT_FOUND", "ไม่พบข้อมูลสถานะของจ๊อบนี้ — เปิดหน้าจ๊อบอีกครั้งก่อนเปลี่ยนสถานะ");

        // ตรวจโครงสร้างก่อน (เส้นทางนี้มีจริงไหม · role/เครื่องทำได้ไหม) โดยสมมติว่า guard ผ่านหมด
        // ต้องมาก่อนการบังคับ reason ด้านล่าง ไม่งั้น transition ที่เป็นไปไม่ได้เลย เช่น กำลังซ่อม → พร้อมส่งมอบ
        // จะได้ข้อความ "ยังไม่มีระบบตรวจสอบอัตโนมัติ กรุณาระบุเหตุผล" ซึ่งชี้ทางผิดสนิท
        // (ผู้ใช้พิมพ์เหตุผลแล้วก็ยังไปต่อไม่ได้ เพราะปัญหาคือเส้นทางไม่มีอยู่ ไม่ใช่ขาดเหตุผล)
        var structural = JobStateMachine.CanTransition(
            job.Status, to.Value, user.Role, user.Source, ~JobGuard.None);
        if (!structural.Allowed)
            return Result<JobTransitionResultDto>.Fail(structural.ErrorCode!, structural.MessageTh!);

        var (computedGuard, isFullyComputed) = await ComputeGuardAsync(job, to.Value, ct);

        var satisfied = computedGuard;
        if (!isFullyComputed)
        {
            // ขั้นตอนนี้ยังไม่มีระบบหลังบ้านรองรับ (Inspection/เบิกอะไหล่/QC/POS) — ยอมให้ role ที่ transition
            // table อนุญาตยืนยันด้วยตนเอง โดยบังคับต้องมีเหตุผลเสมอ (docs/02-domain-model.md invariant #12)
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Result<JobTransitionResultDto>.Fail(
                    "JOB_TRANSITION_NEEDS_REASON",
                    "ขั้นตอนนี้ยังไม่มีระบบตรวจสอบอัตโนมัติ (อยู่ระหว่างพัฒนา) — กรุณาระบุเหตุผลเพื่อยืนยันด้วยตนเอง",
                    nameof(request.Reason));

            var requiredGuard = to == JobStatus.Cancelled
                ? JobStateMachine.CancelTransition.Guard
                : JobStateMachine.Transitions
                    .FirstOrDefault(t => t.From == job.Status && t.To == to.Value)?.Guard ?? JobGuard.None;
            satisfied |= requiredGuard;
        }

        var evaluation = JobStateMachine.CanTransition(job.Status, to.Value, user.Role, user.Source, satisfied);
        if (!evaluation.Allowed)
            return Result<JobTransitionResultDto>.Fail(evaluation.ErrorCode!, evaluation.MessageTh!);

        var from = job.Status;
        job.Status = to.Value;
        if (to == JobStatus.Cancelled)
        {
            job.CancelReason = request.Reason;
            job.CancelledByUserId = user.UserId;
            job.CancelledAt = Now;
        }

        // ปิดงานแล้ว (เสร็จสมบูรณ์/ยกเลิก) ไม่ใช่ "รถในอู่"/"รถนัดหมาย" อีกต่อไป —
        // เปลี่ยนประเภทเป็น "ปิดจ๊อบ" ให้ตกออกจากรายการ/ตัวเลขในเมนูที่กรองตามประเภทเดิมโดยอัตโนมัติ
        if (JobStateMachine.IsTerminal(to.Value))
        {
            job.JobTypeId = ClosedTypeId;
            job.JobTypeName = ClosedTypeName;
        }

        // [BIZ] จ๊อบเปลี่ยนสถานะแล้วคาบเวลาที่ช่างเปิดค้างไว้ต้องปิดตามทันที (docs/09 §6) —
        // ไม่ SaveChanges ที่นี่ เพราะ hook แชร์ DbContext เดียวกันและจะถูก commit พร้อมบรรทัดล่างสุด
        // เป็น transaction เดียวกันจริง (เปลี่ยนสถานะสำเร็จแต่คาบไม่ปิด จะเกิดขึ้นไม่ได้)
        //
        // Qc→Ready อยู่ในรายการนี้ด้วยแม้ docs/09 §6 จะไม่ได้ระบุไว้ — ไม่งั้นคาบที่ช่างเปิดตอนกลับมา
        // แก้งานในสถานะ qc จะค้างเปิดตลอดไปเมื่อ QC ผ่าน แล้วกลายเป็น autoCapped ทุกครั้ง
        // = ชั่วโมงแก้งาน (เมตริกคุณภาพหลัก) ถูกตัดทิ้งอย่างเป็นระบบ
        var closeReason = to.Value switch
        {
            JobStatus.WaitParts => WorkEndReason.WaitParts,
            JobStatus.Qc => WorkEndReason.SentToQc,
            JobStatus.Ready => WorkEndReason.SentToQc,
            _ when JobStateMachine.IsTerminal(to.Value) => WorkEndReason.JobClosed,
            _ => (WorkEndReason?)null
        };
        if (closeReason is not null)
            await workHook.CloseOpenForJobAsync(job.Id, closeReason.Value, ct);

        var description = isFullyComputed
            ? $"เปลี่ยนสถานะจาก {JobStateMachine.Describe(from)} เป็น {JobStateMachine.Describe(to.Value)}"
            : $"เปลี่ยนสถานะจาก {JobStateMachine.Describe(from)} เป็น {JobStateMachine.Describe(to.Value)} " +
              $"· ยืนยันด้วยตนเอง (ยังไม่มีระบบตรวจสอบอัตโนมัติ): {request.Reason}";

        await jobs.AddEventAsync(new ActivityEvent
        {
            JobId = job.Id,
            EntityId = job.Id,
            EntityType = nameof(Job),
            EventType = "job.status.changed",
            DescriptionTh = description,
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now,
            // ให้รายงานรอบเวลา (ReportsService) แยกสถานะแต่ละช่วงได้แน่นอน โดยไม่ต้อง parse ข้อความไทยที่เปราะบาง
            PayloadJson = JsonSerializer.Serialize(new { from = from.ToString(), to = to.Value.ToString() })
        }, ct);

        await jobs.SaveChangesAsync(ct);

        return Result<JobTransitionResultDto>.Ok(
            new JobTransitionResultDto(JobStateMachine.ToToken(job.Status), JobStateMachine.Describe(job.Status)));
    }

    /// <summary>เลื่อน/แก้วันเวลานัดหมาย — ไม่ใช่ state transition จึงไม่ผ่าน JobStateMachine แต่ล็อกแก้ไม่ได้
    /// หลังจ๊อบถึงสถานะจบแล้ว (ไม่ใช่แค่หลัง waitinspect เพราะยังไม่มี transition "รถมาถึงแล้ว" ในระบบ —
    /// จ๊อบค้างที่ waitinspect/waitquote ได้จริงขณะที่ลูกค้าขอเลื่อนนัด)
    /// สิทธิ์: [Authorize] + [RequireShiftSession] เท่านั้น เหมือน endpoint อื่นของ JobsController —
    /// [RISK] สืบทอดช่องโหว่ RBAC เดิมที่ CLAUDE.md บันทึกไว้แล้ว ไม่ได้เพิ่ม/ลดสิทธิ์ใหม่ในงานนี้</summary>
    public async Task<Result<JobDto>> UpdateAppointmentAsync(
        Guid jobId, UpdateJobAppointmentRequest request, CancellationToken ct = default)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return Result<JobDto>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<JobDto>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (JobStateMachine.IsTerminal(job.Status))
            return Result<JobDto>.Fail("JOB_APPOINTMENT_LOCKED", "จ๊อบนี้ปิดแล้ว — แก้ไขวันเวลานัดหมายไม่ได้");

        if (job.JobTypeId != AppointmentTypeId)
            return Result<JobDto>.Fail(
                "JOB_VALIDATION", "แก้ไขวันเวลานัดหมายได้เฉพาะงานประเภทรถนัดหมาย", "appointmentAt");

        var appointmentResult = ValidateAppointment(AppointmentTypeId, request.AppointmentAt);
        if (!appointmentResult.Success)
            return Result<JobDto>.Fail(appointmentResult.Error!);

        var oldAppointment = job.AppointmentAt;
        job.AppointmentAt = appointmentResult.Data;

        var descriptionTh = oldAppointment is null
            ? $"ตั้งวันเวลานัดหมายเป็น {job.AppointmentAt!.Value.AddHours(7):dd/MM/yyyy HH:mm} น."
            : $"เปลี่ยนวันเวลานัดหมายจาก {oldAppointment.Value.AddHours(7):dd/MM/yyyy HH:mm} " +
              $"เป็น {job.AppointmentAt!.Value.AddHours(7):dd/MM/yyyy HH:mm} น.";

        await jobs.AddEventAsync(new ActivityEvent
        {
            JobId = job.Id,
            EntityId = job.Id,
            EntityType = nameof(Job),
            EventType = "job.appointment.changed",
            DescriptionTh = descriptionTh,
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now,
            PayloadJson = JsonSerializer.Serialize(new
            {
                from = oldAppointment?.ToString("O"),
                to = job.AppointmentAt!.Value.ToString("O")
            })
        }, ct);

        await jobs.SaveChangesAsync(ct);

        return Result<JobDto>.Ok(JobMapper.ToDto(job, Now));
    }

    /// <summary>[BIZ] เพิ่ม 2026-09-17 ตามคำขอผู้ใช้ — แปลงงานนัดหมายเป็นรถในอู่เมื่อรถมาถึงจริง (ไม่ผูกกับ
    /// วันนัดหมายที่ตั้งไว้ มาก่อน/หลังนัดก็แปลงได้) ไม่แตะ JobStateMachine เลย (ไม่ใช่ state transition แค่
    /// เปลี่ยนหมวดหมู่ + บันทึกเวลา) AppointmentAt เดิมไม่ถูกล้าง — เก็บไว้เป็นประวัติว่าเดิมนัดวันไหน</summary>
    public async Task<Result<JobDto>> ConvertToInShopAsync(
        Guid jobId, ConvertToInShopRequest request, CancellationToken ct = default)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return Result<JobDto>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<JobDto>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (job.JobTypeId != AppointmentTypeId)
            return Result<JobDto>.Fail(
                "JOB_TYPE_CONVERSION_NOT_ALLOWED", "แปลงเป็นรถในอู่ได้เฉพาะงานประเภทรถนัดหมายเท่านั้น");

        var arrivalUtc = request.ActualArrivalAt.UtcDateTime;
        if (arrivalUtc > Now.AddMinutes(ArrivalFutureToleranceMinutes))
            return Result<JobDto>.Fail(
                "JOB_VALIDATION", "วันเวลาที่รถเข้าอู่จริงต้องไม่เกินวันเวลาปัจจุบัน", "actualArrivalAt");

        job.JobTypeId = InShopTypeId;
        job.JobTypeName = "รถในอู่";
        job.ActualArrivalAt = arrivalUtc;

        await jobs.AddEventAsync(new ActivityEvent
        {
            JobId = job.Id,
            EntityId = job.Id,
            EntityType = nameof(Job),
            EventType = "job.converted_to_in_shop",
            DescriptionTh = $"แปลงประเภทงานจากรถนัดหมายเป็นรถในอู่ · เข้าอู่จริงเมื่อ {arrivalUtc.AddHours(7):dd/MM/yyyy HH:mm} น.",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now,
            PayloadJson = JsonSerializer.Serialize(new
            {
                fromJobTypeId = AppointmentTypeId,
                toJobTypeId = InShopTypeId,
                actualArrivalAt = arrivalUtc.ToString("O")
            })
        }, ct);

        await jobs.SaveChangesAsync(ct);

        return Result<JobDto>.Ok(JobMapper.ToDto(job, Now));
    }

    /// <summary>
    /// คำนวณ guard เฉพาะ bit ที่มีข้อมูลจริงรองรับวันนี้ (จากใบเสนอราคา) — คืน IsFullyComputed = true
    /// เมื่อ transition นี้ตรวจสอบได้จริงทั้งหมด (ไม่อนุญาต manual override สำหรับ transition เหล่านี้)
    /// </summary>
    private async Task<(JobGuard Guard, bool IsFullyComputed)> ComputeGuardAsync(
        Job job, JobStatus to, CancellationToken ct)
    {
        var from = job.Status;
        var isComputable =
            (from == JobStatus.WaitQuote && to == JobStatus.WaitApprove) ||
            (from == JobStatus.WaitApprove && to == JobStatus.Approved) ||
            (from == JobStatus.Approved && to == JobStatus.InProgress) ||
            (from == JobStatus.Qc && to == JobStatus.Ready) ||
            (from == JobStatus.Ready && to == JobStatus.Completed);

        if (!isComputable)
            return (JobGuard.None, false);

        if (to == JobStatus.Completed)
        {
            // [BIZ] ยอดคงเหลือ/ใบเสร็จ/ส่งมอบ คำนวณจากข้อมูลจริงของ PosService/HandoverService
            // (docs/01-workflow.md §3.9/§11 — ตัดขอบเขต split payment/reconciliation/มือถือออกแล้ว)
            var completionGuard = JobGuard.None;

            var quotationForBalance = await quotations.GetLatestForJobAsync(job.Id, ct);
            if (quotationForBalance is not null)
            {
                QuotationCalculator.ApplyQuotationTotals(quotationForBalance);
                var approved = QuotationCalculator.CalculateApprovedTotals(quotationForBalance, job.VatIncluded);
                var paid = (await posRepo.GetPaymentsByJobAsync(job.Id, ct)).Sum(p => p.Amount);
                if (Math.Round(approved.GrandTotal - paid, 2, MidpointRounding.AwayFromZero) <= 0m)
                    completionGuard |= JobGuard.BalanceSettled;
            }

            if (await posRepo.GetReceiptByJobAsync(job.Id, ct) is not null)
                completionGuard |= JobGuard.DocumentIssued;

            var handover = await handoverRepo.GetByJobAsync(job.Id, ct);
            if (handover?.IsLocked == true)
                completionGuard |= JobGuard.VehicleHandedOver;

            return (completionGuard, true);
        }

        if (to == JobStatus.Ready)
        {
            // [BIZ] QcPassed คำนวณจากเช็คลิสต์ QC จริง (ไม่มี process ตีกลับ — ผ่านอย่างเดียว, คำขอผู้ใช้ 2026-09-09)
            // ดู QcChecklistService — checklist สร้างจากบรรทัดที่อนุมัติในใบเสนอราคา ณ ตอนเปิดหน้า QC
            var checklist = await qcChecklists.GetByJobAsync(job.Id, ct);
            if (checklist is null || checklist.Items.Count == 0)
                return (JobGuard.None, true);

            var allPassed = checklist.Items.All(i => i.Result == QcItemResult.Pass);
            var testDriveRecorded = checklist.TestDriveRecordedAt.HasValue
                && checklist.TestDriveKm.HasValue
                && !string.IsNullOrWhiteSpace(checklist.TestDriveNote);

            return (allPassed && testDriveRecorded ? JobGuard.QcPassed : JobGuard.None, true);
        }

        var quotation = await quotations.GetLatestForJobAsync(job.Id, ct);
        if (quotation is null)
            return (JobGuard.None, true);

        if (to == JobStatus.WaitApprove)
        {
            QuotationCalculator.ApplyQuotationTotals(quotation);
            var validation = QuotationValidator.ValidateForSend(quotation, user.Role);
            return (validation.IsValid ? JobGuard.QuotationValid : JobGuard.None, true);
        }

        var hasApprovedLine = quotation.Lines.Any(l => l.ApprovalStatus == LineApprovalStatus.Approved);

        if (to == JobStatus.InProgress)
            return (hasApprovedLine ? JobGuard.HasApprovedLines : JobGuard.None, true);

        // to == JobStatus.Approved
        var guard = JobGuard.None;
        var hasPendingLine = quotation.Lines.Any(l => l.ApprovalStatus == LineApprovalStatus.Pending);
        if (!hasPendingLine && quotation.Lines.Count > 0 && quotation.Approval is not null)
            guard |= JobGuard.AllLinesDecidedAndSigned;
        if (hasApprovedLine)
            guard |= JobGuard.HasApprovedLines;

        return (guard, true);
    }
}
