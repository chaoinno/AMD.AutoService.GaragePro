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
        CustomerDetailDto? customer = null,
        VehicleDetailDto? vehicle = null,
        UserRole role = UserRole.Manager,
        EventSource source = EventSource.Web) =>
        new(jobs, quotations ?? new FakeQuotationRepository(null),
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
    }
}
