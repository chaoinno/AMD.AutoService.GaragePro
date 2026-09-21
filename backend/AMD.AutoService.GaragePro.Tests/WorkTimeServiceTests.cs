using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Jobs;
using AMD.AutoService.GaragePro.Application.Work;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// จับเวลาการทำงานของช่าง — docs/09-technician-time-tracking.md §5-§7
/// เทสต์ชุดนี้ครอบกฎที่ service บังคับ ส่วนกฎที่บังคับด้วย unique index จริงอยู่ที่ WorkIntervalSqlTests
/// </summary>
public sealed class WorkTimeServiceTests
{
    private static readonly Guid JobId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherJobId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTime Noon = new(2026, 9, 21, 5, 0, 0, DateTimeKind.Utc); // 12:00 ตามเวลาไทย
    private const long TechnicianStaffId = 4242;

    [Fact]
    public async Task StartAsync_opens_an_interval_and_records_the_shift_session()
    {
        var repo = new FakeWorkIntervalRepository();
        var service = CreateService(repo, JobAt(JobStatus.InProgress));

        var result = await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        result.Data!.Current.Kind.Should().Be("work");
        result.Data.Current.IsRework.Should().BeFalse();
        result.Data.ClosedPrevious.Should().BeNull();
        result.Data.ServerNow.Should().Be(Noon);
        repo.Intervals.Should().ContainSingle();
        repo.Intervals[0].IsOpen.Should().BeTrue();
    }

    [Fact]
    public async Task StartAsync_pushes_an_approved_job_into_repair_before_opening_the_interval()
    {
        var repo = new FakeWorkIntervalRepository();
        var jobService = new FakeJobService();
        var service = CreateService(repo, JobAt(JobStatus.Approved), jobService: jobService);

        var result = await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        jobService.Transitions.Should().ContainSingle().Which.Should().Be((JobId, "inprogress"));
    }

    [Fact]
    public async Task StartAsync_refuses_a_job_that_is_not_ready_for_repair()
    {
        var service = CreateService(new FakeWorkIntervalRepository(), JobAt(JobStatus.WaitQuote));

        var result = await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("WORK_JOB_NOT_REPAIRABLE");
    }

    /// <summary>
    /// docs/09 §5.5 — ชั่วโมงแก้งานหลังเข้า QC คือเมตริกคุณภาพหลัก และวัดได้เพราะช่างกดเริ่มงานตอน
    /// จ๊อบอยู่ qc ได้ · การกดต้องไม่ดันสถานะกลับ เพราะระบบไม่มี process ตีกลับ (คำขอผู้ใช้ 2026-09-09)
    /// </summary>
    [Fact]
    public async Task StartAsync_marks_rework_when_the_job_is_already_in_qc_without_moving_it_back()
    {
        var repo = new FakeWorkIntervalRepository();
        var jobService = new FakeJobService();
        var service = CreateService(repo, JobAt(JobStatus.Qc), jobService: jobService);

        var result = await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        result.Data!.Current.IsRework.Should().BeTrue();
        jobService.Transitions.Should().BeEmpty();
    }

    [Fact]
    public async Task StartAsync_on_another_job_closes_the_previous_interval_in_the_same_request()
    {
        var repo = new FakeWorkIntervalRepository();
        var jobs = new FakeJobRepository(JobAt(JobStatus.InProgress), JobAt(JobStatus.InProgress, OtherJobId));
        var service = CreateService(repo, jobs: jobs);

        await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        var second = await service.StartAsync(OtherJobId, new StartWorkRequest(Guid.NewGuid()));

        second.Success.Should().BeTrue(second.Error?.MessageTh);
        second.Data!.ClosedPrevious.Should().NotBeNull();
        second.Data.ClosedPrevious!.JobId.Should().Be(JobId);
        repo.Intervals.Count(x => x.IsOpen).Should().Be(1);
        repo.Intervals.Single(x => x.JobId == JobId).EndReason.Should().Be(WorkEndReason.SwitchedJob);
    }

    [Fact]
    public async Task StartAsync_replays_the_same_interval_when_the_request_id_is_reused()
    {
        var repo = new FakeWorkIntervalRepository();
        var service = CreateService(repo, JobAt(JobStatus.InProgress));
        var requestId = Guid.NewGuid();

        var first = await service.StartAsync(JobId, new StartWorkRequest(requestId));
        var replay = await service.StartAsync(JobId, new StartWorkRequest(requestId));

        replay.Success.Should().BeTrue(replay.Error?.MessageTh);
        replay.Data!.Current.Id.Should().Be(first.Data!.Current.Id);
        repo.Intervals.Should().ContainSingle();
    }

    /// <summary>
    /// [BIZ] ยืนยันกับผู้ใช้ 2026-09-21: หัวหน้าช่างคุม/ตรวจ ไม่ลงมือ จึงจับเวลาไม่ได้เลย
    /// (ผลพลอยได้คือไม่ต้องเติม Lead เข้า Approved→InProgress ใน JobStateMachine)
    /// </summary>
    [Theory]
    [InlineData(UserRole.Lead)]
    [InlineData(UserRole.Manager)]
    [InlineData(UserRole.Office)]
    [InlineData(UserRole.FrontDesk)]
    [InlineData(UserRole.Cashier)]
    public async Task Only_technicians_may_track_time(UserRole role)
    {
        var service = CreateService(new FakeWorkIntervalRepository(), JobAt(JobStatus.InProgress), role: role);

        var result = await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("WORK_FORBIDDEN");
    }

    [Fact]
    public async Task Pause_then_resume_splits_the_work_from_the_break()
    {
        var repo = new FakeWorkIntervalRepository();
        var clock = new MovableClock(Noon);
        var service = CreateService(repo, JobAt(JobStatus.InProgress), clock: clock);

        await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromHours(1));
        await service.PauseAsync(JobId, new PauseWorkRequest(Guid.NewGuid(), "พักเที่ยง"));
        clock.Advance(TimeSpan.FromMinutes(30));
        await service.ResumeAsync(JobId, new ResumeWorkRequest(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromHours(2));
        await service.StopAsync(JobId, new StopWorkRequest(Guid.NewGuid(), null));

        var work = repo.Intervals.Where(x => x.Kind == WorkIntervalKind.Work).ToList();
        var pause = repo.Intervals.Single(x => x.Kind == WorkIntervalKind.Pause);

        work.Sum(x => x.Duration!.Value.TotalHours).Should().Be(3);
        pause.Duration!.Value.TotalMinutes.Should().Be(30);
        repo.Intervals.Should().OnlyContain(x => !x.IsOpen);
    }

    [Fact]
    public async Task PauseAsync_refuses_when_nothing_is_being_tracked_on_that_job()
    {
        var service = CreateService(new FakeWorkIntervalRepository(), JobAt(JobStatus.InProgress));

        var result = await service.PauseAsync(JobId, new PauseWorkRequest(Guid.NewGuid(), null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("WORK_NOT_TRACKING");
    }

    /// <summary>
    /// ลืมกดหยุดข้ามกะ — ระบบตัดให้ที่เวลาสิ้นกะและติดธงไว้ ไม่ใช่นับเวลาข้ามคืนเป็นเวลาทำงานจริง
    /// (docs/09 §5.6) · จุดนี้คือเหตุผลที่ GET /work/current เขียนข้อมูลได้
    /// </summary>
    [Fact]
    public async Task GetCurrentAsync_caps_an_interval_that_ran_past_the_end_of_the_shift()
    {
        var repo = new FakeWorkIntervalRepository();
        var clock = new MovableClock(Noon);
        var service = CreateService(repo, JobAt(JobStatus.InProgress), clock: clock);

        await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromDays(1));

        var current = await service.GetCurrentAsync();

        current.Success.Should().BeTrue(current.Error?.MessageTh);
        current.Data!.Current.Should().BeNull();
        current.Data.AutoCappedPrevious.Should().NotBeNull();

        var capped = repo.Intervals.Single();
        capped.IsAutoCapped.Should().BeTrue();
        capped.EndReason.Should().Be(WorkEndReason.AutoCapped);
        // กะทดสอบเลิก 17:00 ตามเวลาไทย = 10:00 UTC ของวันเดียวกับที่เริ่ม
        capped.EndedAt.Should().Be(new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetCurrentAsync_returns_nothing_for_a_role_that_never_tracks_time()
    {
        var service = CreateService(new FakeWorkIntervalRepository(), role: UserRole.Cashier);

        var result = await service.GetCurrentAsync();

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        result.Data!.Current.Should().BeNull();
    }

    [Fact]
    public async Task EditAsync_rewrites_the_times_clears_the_auto_cap_flag_and_keeps_an_audit_trail()
    {
        var repo = new FakeWorkIntervalRepository();
        var clock = new MovableClock(Noon);
        var service = CreateService(repo, JobAt(JobStatus.InProgress), clock: clock);
        await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromDays(1));
        await service.GetCurrentAsync();

        var lead = CreateService(repo, JobAt(JobStatus.InProgress), role: UserRole.Lead, clock: clock);
        var result = await lead.EditAsync(
            repo.Intervals[0].Id,
            new EditWorkIntervalRequest(Noon, Noon.AddHours(3), "ช่างลืมกดหยุด ยืนยันเวลาจริงกับหัวหน้าแล้ว"));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        var edited = repo.Intervals[0];
        edited.EndedAt.Should().Be(Noon.AddHours(3));
        edited.IsAutoCapped.Should().BeFalse();
        edited.EditedByUserId.Should().Be(7);
        edited.EditReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task EditAsync_is_closed_to_the_technician_who_owns_the_interval()
    {
        var repo = new FakeWorkIntervalRepository();
        var service = CreateService(repo, JobAt(JobStatus.InProgress));
        await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        await service.StopAsync(JobId, new StopWorkRequest(Guid.NewGuid(), null));

        var result = await service.EditAsync(
            repo.Intervals[0].Id, new EditWorkIntervalRequest(Noon, Noon.AddHours(1), "อยากแก้เอง"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("WORK_FORBIDDEN");
    }

    [Fact]
    public async Task EditAsync_refuses_once_the_interval_is_older_than_the_edit_window()
    {
        var repo = new FakeWorkIntervalRepository();
        var clock = new MovableClock(Noon);
        var service = CreateService(repo, JobAt(JobStatus.InProgress), clock: clock);
        await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        await service.StopAsync(JobId, new StopWorkRequest(Guid.NewGuid(), null));

        clock.Advance(TimeSpan.FromDays(8));
        var lead = CreateService(repo, JobAt(JobStatus.InProgress), role: UserRole.Lead, clock: clock);
        var result = await lead.EditAsync(
            repo.Intervals[0].Id, new EditWorkIntervalRequest(Noon, Noon.AddHours(1), "ย้อนหลังนานเกินไป"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("WORK_EDIT_WINDOW_EXPIRED");
    }

    /// <summary>ยกเลิกแล้วต้องยังอยู่ในตาราง ไม่งั้นแยกไม่ออกว่าคาบที่หายไปคือ "ไม่เคยมี" หรือ "ถูกลบ"</summary>
    [Fact]
    public async Task VoidAsync_soft_deletes_and_is_reserved_for_the_manager()
    {
        var repo = new FakeWorkIntervalRepository();
        var service = CreateService(repo, JobAt(JobStatus.InProgress));
        await service.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        await service.StopAsync(JobId, new StopWorkRequest(Guid.NewGuid(), null));
        var id = repo.Intervals[0].Id;

        var lead = CreateService(repo, JobAt(JobStatus.InProgress), role: UserRole.Lead);
        (await lead.VoidAsync(id, new VoidWorkIntervalRequest("กดผิดคัน"))).Error!.Code.Should().Be("WORK_FORBIDDEN");

        var manager = CreateService(repo, JobAt(JobStatus.InProgress), role: UserRole.Manager);
        var result = await manager.VoidAsync(id, new VoidWorkIntervalRequest("กดผิดคัน"));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        repo.Intervals.Should().ContainSingle();
        repo.Intervals[0].VoidedAt.Should().NotBeNull();
    }

    /// <summary>
    /// docs/09 §5.3 — คนแรกที่กดไม่ใช่ "ช่างที่รับผิดชอบ" เสมอไป เพราะช่างหลายคนรุมคันเดียวได้
    /// เขียน Job.AssignedTechnicianId ต่อเมื่อมีบรรทัดค่าแรงที่อนุมัติแล้วเป็นของช่างคนนั้นจริง
    /// </summary>
    [Fact]
    public async Task StartAsync_claims_the_job_only_for_a_technician_with_an_approved_labour_line()
    {
        var helper = JobAt(JobStatus.InProgress);
        var noLines = CreateService(new FakeWorkIntervalRepository(), helper, quotation: QuotationFor(staffId: 999));
        await noLines.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        helper.AssignedTechnicianId.Should().BeNull();

        var owner = JobAt(JobStatus.InProgress);
        var assigned = CreateService(
            new FakeWorkIntervalRepository(), owner, quotation: QuotationFor(TechnicianStaffId));
        await assigned.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        owner.AssignedTechnicianId.Should().Be(TechnicianStaffId);
    }

    [Fact]
    public async Task SearchAsync_is_limited_to_the_people_who_can_fix_the_data()
    {
        var repo = new FakeWorkIntervalRepository();

        var technician = CreateService(repo, JobAt(JobStatus.InProgress));
        (await technician.SearchAsync(null, null, false)).Error!.Code.Should().Be("WORK_FORBIDDEN");

        var office = CreateService(repo, JobAt(JobStatus.InProgress), role: UserRole.Office);
        (await office.SearchAsync(null, null, false)).Error!.Code.Should().Be("WORK_FORBIDDEN");

        var lead = CreateService(repo, JobAt(JobStatus.InProgress), role: UserRole.Lead);
        (await lead.SearchAsync(null, null, false)).Success.Should().BeTrue();
    }

    [Fact]
    public async Task SearchAsync_can_narrow_to_the_intervals_that_actually_need_a_human()
    {
        var repo = new FakeWorkIntervalRepository();
        var clock = new MovableClock(Noon);
        var technician = CreateService(repo, JobAt(JobStatus.InProgress), clock: clock);

        // คาบปกติที่ปิดเรียบร้อย — ไม่ต้องตรวจ
        await technician.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromHours(1));
        await technician.StopAsync(JobId, new StopWorkRequest(Guid.NewGuid(), null));

        // คาบที่ลืมกดหยุดข้ามกะ — ต้องตรวจ
        await technician.StartAsync(JobId, new StartWorkRequest(Guid.NewGuid()));
        clock.Advance(TimeSpan.FromDays(1));
        await technician.GetCurrentAsync();

        var lead = CreateService(repo, JobAt(JobStatus.InProgress), role: UserRole.Lead, clock: clock);

        var all = await lead.SearchAsync(Noon.AddHours(-1), clock.GetUtcNow().UtcDateTime, false);
        all.Data.Should().HaveCount(2);

        var needsReview = await lead.SearchAsync(Noon.AddHours(-1), clock.GetUtcNow().UtcDateTime, true);
        needsReview.Data.Should().ContainSingle().Which.IsAutoCapped.Should().BeTrue();
    }

    // ---------- helpers ----------

    private static Job JobAt(JobStatus status, Guid? id = null) => new()
    {
        Id = id ?? JobId,
        JobNo = "JB2609210105001",
        Status = status,
        LegacyShardKey = "db2",
        BranchId = 105,
        VehicleRegistration = "4ขร-2468"
    };

    private static Quotation QuotationFor(long staffId) => new()
    {
        Id = Guid.NewGuid(),
        JobId = JobId,
        Lines =
        [
            new QuotationLine
            {
                Type = LineType.Labor,
                ApprovalStatus = LineApprovalStatus.Approved,
                AssignedTechnicianId = staffId,
                StandardHours = 2m,
                Quantity = 1m
            }
        ]
    };

    private static WorkTimeService CreateService(
        FakeWorkIntervalRepository repo,
        Job? job = null,
        FakeJobRepository? jobs = null,
        FakeJobService? jobService = null,
        Quotation? quotation = null,
        UserRole role = UserRole.Technician,
        MovableClock? clock = null) =>
        new(repo,
            jobs ?? new FakeJobRepository(job ?? JobAt(JobStatus.InProgress)),
            new FakeQuotationRepository(quotation),
            jobService ?? new FakeJobService(),
            new FakeLegacyUserReader(),
            new StubCurrentUser(role),
            clock ?? new MovableClock(Noon),
            new WorkTimeOptions());

    /// <summary>โปรเจกต์นี้ไม่มี FakeTimeProvider (ไม่ได้อ้างอิง Microsoft.Extensions.TimeProvider.Testing)</summary>
    private sealed class MovableClock(DateTime start) : TimeProvider
    {
        private DateTime _now = start;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(_now, DateTimeKind.Utc), TimeSpan.Zero);
    }

    private sealed class StubCurrentUser(UserRole role) : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "ช่าง ทดสอบ";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Mobile;
        public bool IsAdministrator => false;
        public Guid? SessionId => Guid.Parse("33333333-3333-3333-3333-333333333333");
        public long? StaffId => TechnicianStaffId;
    }

    private sealed class FakeWorkIntervalRepository : IWorkIntervalRepository
    {
        public List<WorkInterval> Intervals { get; } = [];
        public List<ActivityEvent> Events { get; } = [];

        public Task<WorkInterval?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Intervals.FirstOrDefault(x => x.Id == id));

        public Task<WorkInterval?> GetOpenByTechnicianAsync(long staffId, CancellationToken ct = default) =>
            Task.FromResult(Intervals.FirstOrDefault(x => x.TechnicianStaffId == staffId && x.EndedAt is null));

        public Task<WorkInterval?> GetOpenByTechnicianAsync(
            string shardKey, int branchId, long staffId, CancellationToken ct = default) =>
            GetOpenByTechnicianAsync(staffId, ct);

        public Task<IReadOnlyList<WorkInterval>> GetOpenByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkInterval>>(
                Intervals.Where(x => x.JobId == jobId && x.EndedAt is null).ToList());

        public Task<WorkInterval?> GetByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(Intervals.FirstOrDefault(x => x.RequestId == requestId));

        public Task<IReadOnlyList<WorkInterval>> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkInterval>>(Intervals.Where(x => x.JobId == jobId).ToList());

        public Task<IReadOnlyList<WorkInterval>> SearchAsync(
            DateTime fromUtc, DateTime toUtc, bool onlyNeedsReview, int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<WorkInterval>>(Intervals
                .Where(x => x.StartedAt >= fromUtc && x.StartedAt < toUtc)
                .Where(x => !onlyNeedsReview || x.IsAutoCapped || x.EndedAt is null)
                .OrderByDescending(x => x.StartedAt)
                .Take(take)
                .ToList());

        public Task AddAsync(WorkInterval interval, CancellationToken ct = default)
        {
            Intervals.Add(interval);
            return Task.CompletedTask;
        }

        /// <summary>กะทดสอบ 08:00–17:00 ตามเวลาไทย</summary>
        public Task<Shift?> GetShiftForSessionAsync(Guid sessionId, CancellationToken ct = default) =>
            Task.FromResult<Shift?>(new Shift
            {
                Name = "กะเช้า",
                StartTime = new TimeOnly(8, 0),
                EndTime = new TimeOnly(17, 0)
            });

        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeJobRepository(params Job[] jobs) : IJobRepository
    {
        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(jobs.FirstOrDefault(j => j.Id == jobId));

        public Task<Job?> GetOpenByVehicleAsync(string shardKey, int branchId, long vehicleId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<Job>> GetAppointmentsAsync(JobAppointmentQuery query, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<int> CountOpenAsync(string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<IReadOnlyList<JobStatusTally>> CountOpenByStatusAsync(
            string shardKey, int branchId, int? jobTypeId, DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddAsync(Job job, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeQuotationRepository(Quotation? quotation) : IQuotationRepository
    {
        public Task<Quotation?> GetLatestForJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(quotation);

        public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Quotation>> GetQueueAsync(
            string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddAsync(Quotation quotation, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeJobService : IJobService
    {
        public List<(Guid JobId, string ToStatus)> Transitions { get; } = [];

        public Task<Result<JobTransitionResultDto>> TransitionAsync(
            Guid jobId, TransitionJobRequest request, CancellationToken ct = default)
        {
            Transitions.Add((jobId, request.ToStatus));
            return Task.FromResult(Result<JobTransitionResultDto>.Ok(
                new JobTransitionResultDto(request.ToStatus, request.ToStatus)));
        }

        public Task<Result<JobDto>> GetAsync(Guid jobId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<IReadOnlyList<JobDto>>> SearchAsync(
            string? keyword, int take, DateTime? beforeCreatedAt, Guid? beforeJobId,
            int? jobTypeId, string? statusToken, CancellationToken ct = default) => throw new NotImplementedException();
        public IReadOnlyList<JobStatusOptionDto> GetStatusOptions() => throw new NotImplementedException();
        public Task<Result<int>> CountOpenAsync(int? jobTypeId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<JobCountsDto>> CountsAsync(int? jobTypeId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<CreatedJobDto>> CreateAsync(CreateJobRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<JobDto>> UpdateAppointmentAsync(
            Guid jobId, UpdateJobAppointmentRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<JobDto>> ConvertToInShopAsync(
            Guid jobId, ConvertToInShopRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<JobCalendarDto>> GetCalendarAsync(
            DateTimeOffset from, DateTimeOffset to, string? keyword, string? statusToken,
            CancellationToken ct = default) => throw new NotImplementedException();
    }

    private sealed class FakeLegacyUserReader : ILegacyUserReader
    {
        public Task<LegacyUserDto?> FindByUserNameAsync(string shardKey, string userName, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<LegacyUserDto?> FindByIdAsync(string shardKey, long userId, CancellationToken ct = default) =>
            Task.FromResult<LegacyUserDto?>(null);
        public Task<IReadOnlyList<LegacyBranchSummaryDto>> GetAccessibleBranchesAsync(
            string shardKey, LegacyUserDto user, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
