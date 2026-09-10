using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Jobs;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class JobServiceTests
{
    private static readonly Guid TestJobId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task CreateAsync_fails_when_customer_not_found()
    {
        var jobs = new FakeJobRepository();
        var service = CreateService(jobs, customer: null, vehicle: SampleVehicle());

        var result = await service.CreateAsync(new CreateJobRequest(1, 1, 9, null, null, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("CUSTOMER_NOT_FOUND");
    }

    [Fact]
    public async Task CreateAsync_fails_when_vehicle_not_found()
    {
        var jobs = new FakeJobRepository();
        var service = CreateService(jobs, customer: SampleCustomer(), vehicle: null);

        var result = await service.CreateAsync(new CreateJobRequest(1, 1, 9, null, null, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("VEHICLE_NOT_FOUND");
    }

    [Fact]
    public async Task CreateAsync_fails_when_vehicle_already_has_an_open_job()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(new Job
        {
            LegacyShardKey = "db2", BranchId = 105, VehicleId = 1, CustomerId = 1,
            JobNo = "JB2608310105001", Status = JobStatus.InProgress
        });
        var service = CreateService(jobs, customer: SampleCustomer(), vehicle: SampleVehicle());

        var result = await service.CreateAsync(new CreateJobRequest(1, 1, 9, null, null, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_DUPLICATE_OPEN");
    }

    [Fact]
    public async Task CreateAsync_creates_a_job_in_svc_Job_only_and_logs_an_activity_event()
    {
        var jobs = new FakeJobRepository();
        var service = CreateService(jobs, customer: SampleCustomer(), vehicle: SampleVehicle());

        var result = await service.CreateAsync(new CreateJobRequest(1, 1, 9, "สมชาย ใจดี", "0812345678", null));

        result.Success.Should().BeTrue();
        result.Data!.JobNo.Should().Be("JB2608310105001");
        jobs.Saved.Should().ContainSingle(j =>
            j.CustomerId == 1 && j.VehicleId == 1 && j.Status == JobStatus.WaitInspect);
        jobs.Events.Should().ContainSingle(e => e.EventType == "job.opened");
    }

    [Fact]
    public async Task CountOpenAsync_counts_only_open_jobs_of_the_current_branch_and_type()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 9, JobNo = "JB1", Status = JobStatus.WaitInspect });
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 9, JobNo = "JB2", Status = JobStatus.InProgress });
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 9, JobNo = "JB3", Status = JobStatus.Completed });
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 10, JobNo = "JB4", Status = JobStatus.WaitQuote });
        jobs.Seed(new Job { LegacyShardKey = "db1", BranchId = 105, JobTypeId = 9, JobNo = "JB5", Status = JobStatus.WaitQuote });
        var service = CreateService(jobs);

        var result = await service.CountOpenAsync(9);

        result.Success.Should().BeTrue();
        result.Data.Should().Be(2);
    }

    [Fact]
    public async Task TransitionAsync_fails_when_job_has_no_svc_Job_row()
    {
        var service = CreateService(new FakeJobRepository());

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("cancelled", "ลูกค้ายกเลิก"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_NOT_FOUND");
    }

    [Fact]
    public async Task TransitionAsync_rejects_unknown_status_token()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.WaitInspect));
        var service = CreateService(jobs);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("not-a-status", null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_STATUS_UNKNOWN");
    }

    [Fact]
    public async Task TransitionAsync_requires_a_reason_when_the_guard_cannot_be_computed_yet()
    {
        // waitinspect -> waitquote ต้องการ InspectionComplete ซึ่งยังไม่มีระบบ Inspection รองรับ
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.WaitInspect));
        var service = CreateService(jobs, role: UserRole.Technician, source: EventSource.Mobile);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("waitquote", null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_TRANSITION_NEEDS_REASON");
    }

    [Fact]
    public async Task TransitionAsync_accepts_manual_override_with_a_reason_and_logs_it()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.WaitInspect));
        var service = CreateService(jobs, role: UserRole.Technician, source: EventSource.Mobile);

        var result = await service.TransitionAsync(
            TestJobId, new TransitionJobRequest("waitquote", "ตรวจเช็คเสร็จแล้วนอกระบบ (โมดูล Inspection ยังไม่พร้อม)"));

        result.Success.Should().BeTrue();
        result.Data!.Status.Should().Be("waitquote");
        jobs.Saved.Single().Status.Should().Be(JobStatus.WaitQuote);
        jobs.Events.Should().ContainSingle(e =>
            e.EventType == "job.status.changed" && e.DescriptionTh.Contains("ยืนยันด้วยตนเอง"));
    }

    [Fact]
    public async Task TransitionAsync_writes_from_to_status_payload_for_reports_to_reconstruct_the_timeline()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.WaitInspect));
        var service = CreateService(jobs, role: UserRole.Technician, source: EventSource.Mobile);

        await service.TransitionAsync(
            TestJobId, new TransitionJobRequest("waitquote", "ตรวจเช็คเสร็จแล้วนอกระบบ (โมดูล Inspection ยังไม่พร้อม)"));

        var payload = jobs.Events.Single(e => e.EventType == "job.status.changed").PayloadJson;
        payload.Should().NotBeNullOrWhiteSpace();
        using var doc = System.Text.Json.JsonDocument.Parse(payload!);
        doc.RootElement.GetProperty("from").GetString().Should().Be("WaitInspect");
        doc.RootElement.GetProperty("to").GetString().Should().Be("WaitQuote");
    }

    [Fact]
    public async Task TransitionAsync_computes_QuotationValid_guard_and_rejects_when_a_labor_line_has_no_technician()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.WaitQuote));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "LBR-001", Name = "ค่าแรงตรวจเช็ค",
            Type = LineType.Labor, Quantity = 1, UnitPrice = 500, AssignedTechnicianId = null
        });

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            role: UserRole.Office, source: EventSource.Web);

        // ไม่ระบุ reason เพราะ guard นี้คำนวณได้จริง — ไม่อนุญาต manual override
        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("waitapprove", null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_GUARD_NOT_SATISFIED");
    }

    [Fact]
    public async Task TransitionAsync_advances_to_waitapprove_when_quotation_is_valid()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.WaitQuote));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 900
        });

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            role: UserRole.Office, source: EventSource.Web);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("waitapprove", null));

        result.Success.Should().BeTrue();
        jobs.Saved.Single().Status.Should().Be(JobStatus.WaitApprove);
    }

    [Fact]
    public async Task TransitionAsync_computes_HasApprovedLines_guard_for_approved_to_inprogress()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Approved));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 900, ApprovalStatus = LineApprovalStatus.Rejected,
            RejectReason = "ลูกค้าไม่เอา"
        });

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            role: UserRole.Technician, source: EventSource.Mobile);

        // ไม่มีบรรทัดที่ลูกค้าอนุมัติเลย — ไม่อนุญาต manual override เพราะ guard นี้คำนวณได้จริง
        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("inprogress", null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_GUARD_NOT_SATISFIED");
    }

    [Fact]
    public async Task TransitionAsync_rejects_qc_to_ready_without_reason_when_checklist_is_incomplete()
    {
        // ไม่มี process ตีกลับ (คำขอผู้ใช้ 2026-09-09) — Qc→Ready คำนวณได้จริงแล้ว ไม่อนุญาต manual override อีกต่อไป
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Qc));

        var checklist = new QcChecklist { JobId = TestJobId };
        checklist.Items.Add(new QcChecklistItem { QcChecklistId = checklist.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า", Result = QcItemResult.Pending });

        var service = CreateService(jobs, qcChecklists: new FakeQcChecklistRepository(checklist),
            role: UserRole.Office, source: EventSource.Web);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("ready", "ยืนยันเอง"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_GUARD_NOT_SATISFIED");
    }

    [Fact]
    public async Task TransitionAsync_rejects_qc_to_ready_when_all_items_pass_but_test_drive_is_missing()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Qc));

        var checklist = new QcChecklist { JobId = TestJobId };
        checklist.Items.Add(new QcChecklistItem { QcChecklistId = checklist.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า", Result = QcItemResult.Pass });

        var service = CreateService(jobs, qcChecklists: new FakeQcChecklistRepository(checklist),
            role: UserRole.Office, source: EventSource.Web);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("ready", null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_GUARD_NOT_SATISFIED");
    }

    [Fact]
    public async Task TransitionAsync_advances_qc_to_ready_without_a_reason_when_checklist_and_test_drive_are_complete()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Qc));

        var checklist = new QcChecklist
        {
            JobId = TestJobId, TestDriveKm = 5, TestDriveNote = "ขับปกติดี", TestDriveRecordedAt = DateTime.UtcNow
        };
        checklist.Items.Add(new QcChecklistItem { QcChecklistId = checklist.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า", Result = QcItemResult.Pass });

        var service = CreateService(jobs, qcChecklists: new FakeQcChecklistRepository(checklist),
            role: UserRole.Office, source: EventSource.Web);

        // ไม่ส่ง reason — guard คำนวณได้จริงแล้วจึงไม่ต้อง manual override
        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("ready", null));

        result.Success.Should().BeTrue();
        jobs.Saved.Single().Status.Should().Be(JobStatus.Ready);
    }

    [Fact]
    public async Task TransitionAsync_rejects_ready_to_completed_without_reason_bypass_when_balance_not_settled()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Ready));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 900, ApprovalStatus = LineApprovalStatus.Approved
        });

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            role: UserRole.Cashier, source: EventSource.Web);

        // ไม่มีการชำระเงินเลย — ไม่อนุญาต manual override เพราะ guard นี้คำนวณได้จริงแล้ว
        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("completed", "ยืนยันเอง"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_GUARD_NOT_SATISFIED");
    }

    [Fact]
    public async Task TransitionAsync_rejects_ready_to_completed_when_balance_settled_but_no_receipt_or_handover()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Ready));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 900, ApprovalStatus = LineApprovalStatus.Approved
        });

        var payments = new List<Payment> { new() { JobId = TestJobId, Amount = 963m } };
        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            posRepo: new FakePosRepository(payments), role: UserRole.Cashier, source: EventSource.Web);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("completed", null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_GUARD_NOT_SATISFIED");
    }

    [Fact]
    public async Task TransitionAsync_advances_ready_to_completed_without_a_reason_when_paid_receipted_and_handed_over()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Ready));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 900, ApprovalStatus = LineApprovalStatus.Approved
        });

        var payments = new List<Payment> { new() { JobId = TestJobId, Amount = 963m } };
        var receipt = new Receipt { JobId = TestJobId, DocumentNo = "RC-26-0001", TotalAmount = 963m };
        var handover = new HandoverRecord { JobId = TestJobId, SubmittedAt = DateTime.UtcNow };

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            posRepo: new FakePosRepository(payments, receipt), handoverRepo: new FakeHandoverRepository(handover),
            role: UserRole.Cashier, source: EventSource.Web);

        // ไม่ส่ง reason — guard คำนวณได้จริงแล้วจึงไม่ต้อง manual override
        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("completed", null));

        result.Success.Should().BeTrue();
        jobs.Saved.Single().Status.Should().Be(JobStatus.Completed);
    }

    [Fact]
    public async Task TransitionAsync_reclassifies_job_type_as_closed_once_it_reaches_a_terminal_status()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Ready));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 900, ApprovalStatus = LineApprovalStatus.Approved
        });

        var payments = new List<Payment> { new() { JobId = TestJobId, Amount = 963m } };
        var receipt = new Receipt { JobId = TestJobId, DocumentNo = "RC-26-0001", TotalAmount = 963m };
        var handover = new HandoverRecord { JobId = TestJobId, SubmittedAt = DateTime.UtcNow };

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            posRepo: new FakePosRepository(payments, receipt), handoverRepo: new FakeHandoverRepository(handover),
            role: UserRole.Cashier, source: EventSource.Web);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("completed", null));

        result.Success.Should().BeTrue();
        var saved = jobs.Saved.Single();
        saved.JobTypeId.Should().Be(11);
        saved.JobTypeName.Should().Be("ปิดจ๊อบ");
    }

    [Fact]
    public async Task TransitionAsync_reclassifies_job_type_as_closed_when_cancelled()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.WaitInspect));
        var service = CreateService(jobs, role: UserRole.Manager, source: EventSource.Web);

        var result = await service.TransitionAsync(
            TestJobId, new TransitionJobRequest("cancelled", "ลูกค้ายกเลิกงาน"));

        result.Success.Should().BeTrue();
        var saved = jobs.Saved.Single();
        saved.Status.Should().Be(JobStatus.Cancelled);
        saved.JobTypeId.Should().Be(11);
        saved.JobTypeName.Should().Be("ปิดจ๊อบ");
    }

    [Fact]
    public async Task TransitionAsync_computes_balance_settled_without_vat_when_job_excludes_vat()
    {
        var jobs = new FakeJobRepository();
        var job = SeedJob(JobStatus.Ready);
        job.VatIncluded = false;
        jobs.Seed(job);

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 900, ApprovalStatus = LineApprovalStatus.Approved
        });

        // จ่ายแค่ 900 (ไม่รวม VAT 63) — ถ้า guard ไม่สนใจ VatIncluded จะยังขาดยอดและปฏิเสธ
        var payments = new List<Payment> { new() { JobId = TestJobId, Amount = 900m } };
        var receipt = new Receipt { JobId = TestJobId, DocumentNo = "RC-26-0001", TotalAmount = 900m };
        var handover = new HandoverRecord { JobId = TestJobId, SubmittedAt = DateTime.UtcNow };

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            posRepo: new FakePosRepository(payments, receipt), handoverRepo: new FakeHandoverRepository(handover),
            role: UserRole.Cashier, source: EventSource.Web);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("completed", null));

        result.Success.Should().BeTrue();
        jobs.Saved.Single().Status.Should().Be(JobStatus.Completed);
    }

    // ---------- helpers ----------

    private static Job SeedJob(JobStatus status) => new()
    {
        Id = TestJobId,
        LegacyShardKey = "db2", BranchId = 105, CustomerId = 1, VehicleId = 1,
        JobNo = "JB2608310105001", Status = status,
        BranchName = "อู่ทดสอบ", CustomerName = "ลูกค้าทดสอบ", VehicleRegistration = "1กก-1234",
        JobTypeId = 9, JobTypeName = "รถในอู่"
    };

    private static CustomerDetailDto SampleCustomer() => new(
        Id: 1, Code: "CUS-000001", FirstName: "สมชาย", LastName: "ใจดี",
        IdCard: null, DriverLicense: null, GenderId: null, DateOfBirth: null,
        Address1: null, Address2: null, ProvinceId: null, ProvinceName: null,
        AmphureId: null, AmphureName: null, DistrictId: null, DistrictName: null, ZipCode: null,
        PhoneNumber1: "0812345678", PhoneNumber2: null, Email: null, LineId: null,
        IsBlacklist: false, BlacklistRemark: null, IsDeleted: false,
        CreatedDate: null, LastUpdated: null, Vehicles: []);

    private static VehicleDetailDto SampleVehicle() => new(
        Id: 1, Registration: "1กก-1234", ProvinceId: null, ProvinceName: null,
        BrandId: null, BrandName: "Toyota", ModelId: null, ModelName: "Yaris",
        NicknameId: null, Nickname: null, CarTypeId: null, CarTypeName: null,
        YearId: null, Year: null, PrimaryColorId: null, PrimaryColorName: null,
        ColorMixId: null, ColorMixName: null, GearId: null, GearName: null,
        MachineId: null, MachineName: null, DriveSystemId: null, DriveSystemName: null,
        Vin: "JT000000000000001", EngineNumber: null, InsuranceId: null, InsuranceName: null,
        InsuranceExpiredDate: null, ImageUrl: null, IsDeleted: false,
        CreatedDate: null, LastUpdated: null, Owners: []);

    private static JobService CreateService(
        FakeJobRepository jobs,
        FakeQuotationRepository? quotations = null,
        FakeQcChecklistRepository? qcChecklists = null,
        FakePosRepository? posRepo = null,
        FakeHandoverRepository? handoverRepo = null,
        CustomerDetailDto? customer = null,
        VehicleDetailDto? vehicle = null,
        UserRole role = UserRole.Manager,
        EventSource source = EventSource.Web) =>
        new(jobs, quotations ?? new FakeQuotationRepository(null), qcChecklists ?? new FakeQcChecklistRepository(null),
            posRepo ?? new FakePosRepository(), handoverRepo ?? new FakeHandoverRepository(null),
            new FakeJobNumberGenerator(), new FakeCustomerVehicleService(customer, vehicle),
            new FakeLegacyReader(), new StubCurrentUser(role, source), TimeProvider.System);

    private sealed class StubCurrentUser(UserRole role, EventSource source) : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "พนักงาน ทดสอบ";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => source;
        public Guid? SessionId => null;
        public bool IsAdministrator => false;
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        private readonly List<Job> _jobs = [];
        public List<ActivityEvent> Events { get; } = [];
        public IReadOnlyList<Job> Saved => _jobs;

        public void Seed(Job job) => _jobs.Add(job);

        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(_jobs.FirstOrDefault(j => j.Id == jobId));

        public Task<Job?> GetOpenByVehicleAsync(
            string shardKey, int branchId, long vehicleId, CancellationToken ct = default) =>
            Task.FromResult(_jobs.FirstOrDefault(j =>
                j.LegacyShardKey == shardKey && j.BranchId == branchId && j.VehicleId == vehicleId
                && j.Status is not (JobStatus.Completed or JobStatus.Cancelled)));

        public Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Job>>(_jobs
                .Where(j => j.LegacyShardKey == query.ShardKey && j.BranchId == query.BranchId)
                .ToList());

        public Task<int> CountOpenAsync(
            string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default) =>
            Task.FromResult(_jobs.Count(j =>
                j.LegacyShardKey == shardKey && j.BranchId == branchId
                && j.Status is not (JobStatus.Completed or JobStatus.Cancelled)
                && (jobTypeId is null || j.JobTypeId == jobTypeId)));

        public Task AddAsync(Job job, CancellationToken ct = default)
        {
            _jobs.Add(job);
            return Task.CompletedTask;
        }

        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeQuotationRepository(Quotation? quotation) : IQuotationRepository
    {
        public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(quotation);
        public Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default) => Task.FromResult(quotation);
        public Task<Quotation?> GetLatestForJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(quotation);
        public Task<IReadOnlyList<Quotation>> GetQueueAsync(
            string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>(quotation is null ? [] : [quotation]);
        public Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(1);
        public Task AddAsync(Quotation q, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeQcChecklistRepository(QcChecklist? checklist) : IQcChecklistRepository
    {
        public Task<QcChecklist?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(checklist);
        public Task AddAsync(QcChecklist c, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakePosRepository(
        IReadOnlyList<Payment>? payments = null, Receipt? receipt = null) : IPosRepository
    {
        public Task<IReadOnlyList<Payment>> GetPaymentsByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(payments ?? []);
        public Task<Payment?> GetPaymentByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(payments?.FirstOrDefault(p => p.RequestId == requestId));
        public Task<Payment?> GetPaymentAsync(Guid jobId, Guid paymentId, CancellationToken ct = default) =>
            Task.FromResult(payments?.FirstOrDefault(p => p.Id == paymentId));
        public Task AddPaymentAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemovePaymentAsync(Payment payment, CancellationToken ct = default) => Task.CompletedTask;
        public Task<Receipt?> GetReceiptByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(receipt);
        public Task AddReceiptAsync(Receipt receipt, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeHandoverRepository(HandoverRecord? record) : IHandoverRepository
    {
        public Task<HandoverRecord?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(record);
        public Task AddAsync(HandoverRecord r, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeJobNumberGenerator : IJobNumberGenerator
    {
        public Task<string> NextAsync(string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default) =>
            Task.FromResult("JB2608310105001");
    }

    private sealed class FakeLegacyReader : ILegacyReader
    {
        public Task<LegacyBranchDto?> GetBranchAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult<LegacyBranchDto?>(new LegacyBranchDto(branchId, "อู่ทดสอบ", null, null, null));

        public Task<IReadOnlyList<LegacyTechnicianDto>> GetTechniciansAsync(
            string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LegacyTechnicianDto>>([]);
    }

    private sealed class FakeCustomerVehicleService(
        CustomerDetailDto? customer, VehicleDetailDto? vehicle) : ICustomerVehicleService
    {
        public Task<Result<CustomerDetailDto>> GetCustomerAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(customer is null
                ? Result<CustomerDetailDto>.Fail("CUSTOMER_NOT_FOUND", "ไม่พบข้อมูลลูกค้า")
                : Result<CustomerDetailDto>.Ok(customer));

        public Task<Result<VehicleDetailDto>> GetVehicleAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(vehicle is null
                ? Result<VehicleDetailDto>.Fail("VEHICLE_NOT_FOUND", "ไม่พบข้อมูลรถ")
                : Result<VehicleDetailDto>.Ok(vehicle));

        public Task<Result<PagedResult<CustomerSummaryDto>>> SearchCustomersAsync(
            CustomerSearchQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<IReadOnlyList<CustomerSummaryDto>>> ExportCustomersAsync(
            CustomerSearchQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<CustomerDetailDto>> CreateCustomerAsync(
            CustomerUpsertRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<CustomerDetailDto>> UpdateCustomerAsync(
            long id, CustomerUpsertRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<bool>> DeleteCustomerAsync(long id, CancellationToken ct = default) =>
            throw new NotImplementedException();

        public Task<Result<PagedResult<VehicleSummaryDto>>> SearchVehiclesAsync(
            VehicleSearchQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<IReadOnlyList<VehicleSummaryDto>>> ExportVehiclesAsync(
            VehicleSearchQuery query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Result<VehicleDetailDto>> CreateVehicleAsync(
            VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Result<VehicleDetailDto>> UpdateVehicleAsync(
            long id, VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Result<bool>> DeleteVehicleAsync(long id, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Result<VehicleImageFile>> OpenVehicleImageAsync(long id, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Result<VehicleDetailDto>> UpdateVehicleImageAsync(
            long id, VehicleImageUpload image, CancellationToken ct = default) =>
            throw new NotImplementedException();
    }
}
