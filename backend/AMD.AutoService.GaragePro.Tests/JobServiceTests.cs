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
    public async Task CreateAsync_fails_when_appointment_missing_for_appointment_type()
    {
        var jobs = new FakeJobRepository();
        var service = CreateService(jobs, customer: SampleCustomer(), vehicle: SampleVehicle());

        var result = await service.CreateAsync(new CreateJobRequest(1, 1, 10, null, null, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_VALIDATION");
        result.Error!.Field.Should().Be("appointmentAt");
    }

    [Fact]
    public async Task CreateAsync_fails_when_appointment_sent_for_in_shop_type()
    {
        var jobs = new FakeJobRepository();
        var service = CreateService(jobs, customer: SampleCustomer(), vehicle: SampleVehicle());

        var result = await service.CreateAsync(new CreateJobRequest(
            1, 1, 9, null, null, null, DateTimeOffset.UtcNow.AddDays(1)));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_VALIDATION");
        result.Error!.Field.Should().Be("appointmentAt");
    }

    [Fact]
    public async Task CreateAsync_fails_when_appointment_is_out_of_allowed_range()
    {
        var jobs = new FakeJobRepository();
        var service = CreateService(jobs, customer: SampleCustomer(), vehicle: SampleVehicle());

        var tooFarAhead = await service.CreateAsync(new CreateJobRequest(
            1, 1, 10, null, null, null, DateTimeOffset.UtcNow.AddYears(3)));
        var tooFarBehind = await service.CreateAsync(new CreateJobRequest(
            1, 1, 10, null, null, null, DateTimeOffset.UtcNow.AddDays(-5)));

        tooFarAhead.Success.Should().BeFalse();
        tooFarAhead.Error!.Code.Should().Be("JOB_VALIDATION");
        tooFarBehind.Success.Should().BeFalse();
        tooFarBehind.Error!.Code.Should().Be("JOB_VALIDATION");
    }

    [Fact]
    public async Task CreateAsync_fails_when_appointment_is_even_slightly_in_the_past()
    {
        // [BIZ] ยืนยันกับผู้ใช้ 2026-09-17 — วันนัดหมายห้ามน้อยกว่าวันเวลาปัจจุบัน (เกิน tolerance กันเวลาคลาดสั้นๆ)
        var jobs = new FakeJobRepository();
        var service = CreateService(jobs, customer: SampleCustomer(), vehicle: SampleVehicle());

        var result = await service.CreateAsync(new CreateJobRequest(
            1, 1, 10, null, null, null, DateTimeOffset.UtcNow.AddHours(-1)));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_VALIDATION");
        result.Error!.Field.Should().Be("appointmentAt");
    }

    [Fact]
    public async Task CreateAsync_accepts_an_appointment_at_the_current_moment()
    {
        var jobs = new FakeJobRepository();
        var service = CreateService(jobs, customer: SampleCustomer(), vehicle: SampleVehicle());

        var result = await service.CreateAsync(new CreateJobRequest(
            1, 1, 10, null, null, null, DateTimeOffset.UtcNow));

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task CreateAsync_stores_appointment_and_mentions_it_in_the_job_opened_event()
    {
        var jobs = new FakeJobRepository();
        var service = CreateService(jobs, customer: SampleCustomer(), vehicle: SampleVehicle());
        var appointment = DateTimeOffset.UtcNow.AddDays(2);

        var result = await service.CreateAsync(new CreateJobRequest(1, 1, 10, null, null, null, appointment));

        result.Success.Should().BeTrue();
        var job = jobs.Saved.Single();
        job.AppointmentAt.Should().BeCloseTo(appointment.UtcDateTime, TimeSpan.FromSeconds(1));
        jobs.Events.Should().ContainSingle(e => e.EventType == "job.opened" && e.DescriptionTh.Contains("นัดหมาย"));
    }

    [Fact]
    public async Task UpdateAppointmentAsync_writes_an_event_with_from_and_to_payload()
    {
        var jobs = new FakeJobRepository();
        var oldAppointment = DateTime.UtcNow.AddDays(1);
        jobs.Seed(new Job
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 105, JobTypeId = 10,
            JobNo = "JB1", Status = JobStatus.WaitInspect, AppointmentAt = oldAppointment
        });
        var service = CreateService(jobs);
        var newAppointment = DateTimeOffset.UtcNow.AddDays(5);

        var result = await service.UpdateAppointmentAsync(TestJobId, new UpdateJobAppointmentRequest(newAppointment));

        result.Success.Should().BeTrue();
        result.Data!.AppointmentAt.Should().BeCloseTo(newAppointment.UtcDateTime, TimeSpan.FromSeconds(1));
        jobs.Events.Should().ContainSingle(e =>
            e.EventType == "job.appointment.changed" && e.PayloadJson != null && e.PayloadJson.Contains("from"));
    }

    [Fact]
    public async Task UpdateAppointmentAsync_fails_for_a_job_that_already_reached_a_terminal_status()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(new Job
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 105, JobTypeId = 11,
            JobNo = "JB1", Status = JobStatus.Completed, AppointmentAt = DateTime.UtcNow.AddDays(1)
        });
        var service = CreateService(jobs);

        var result = await service.UpdateAppointmentAsync(
            TestJobId, new UpdateJobAppointmentRequest(DateTimeOffset.UtcNow.AddDays(2)));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_APPOINTMENT_LOCKED");
    }

    [Fact]
    public async Task UpdateAppointmentAsync_fails_for_a_job_of_another_branch()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(new Job
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 999, JobTypeId = 10,
            JobNo = "JB1", Status = JobStatus.WaitInspect, AppointmentAt = DateTime.UtcNow.AddDays(1)
        });
        var service = CreateService(jobs);

        var result = await service.UpdateAppointmentAsync(
            TestJobId, new UpdateJobAppointmentRequest(DateTimeOffset.UtcNow.AddDays(2)));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_NOT_FOUND");
    }

    [Fact]
    public async Task ConvertToInShopAsync_changes_job_type_and_records_arrival_and_writes_event()
    {
        var jobs = new FakeJobRepository();
        var appointment = DateTime.UtcNow.AddDays(-1); // นัดไว้เมื่อวาน แต่รถเพิ่งมาวันนี้ — แปลงได้ไม่ผูกกับวันนัด
        jobs.Seed(new Job
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 105, JobTypeId = 10, JobTypeName = "รถนัดหมาย",
            JobNo = "JB1", Status = JobStatus.WaitInspect, AppointmentAt = appointment
        });
        var service = CreateService(jobs);
        var arrival = DateTimeOffset.UtcNow;

        var result = await service.ConvertToInShopAsync(TestJobId, new ConvertToInShopRequest(arrival));

        result.Success.Should().BeTrue();
        result.Data!.JobTypeId.Should().Be(9);
        result.Data!.JobTypeName.Should().Be("รถในอู่");
        result.Data!.ActualArrivalAt.Should().BeCloseTo(arrival.UtcDateTime, TimeSpan.FromSeconds(1));
        // AppointmentAt เดิมไม่ถูกล้าง — เก็บไว้เป็นประวัติว่าเดิมนัดวันไหน
        result.Data!.AppointmentAt.Should().BeCloseTo(appointment, TimeSpan.FromSeconds(1));
        jobs.Events.Should().ContainSingle(e => e.EventType == "job.converted_to_in_shop");
    }

    [Fact]
    public async Task ConvertToInShopAsync_fails_when_job_is_not_an_appointment_type()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(new Job
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 105, JobTypeId = 9,
            JobNo = "JB1", Status = JobStatus.WaitInspect
        });
        var service = CreateService(jobs);

        var result = await service.ConvertToInShopAsync(TestJobId, new ConvertToInShopRequest(DateTimeOffset.UtcNow));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_TYPE_CONVERSION_NOT_ALLOWED");
    }

    [Fact]
    public async Task ConvertToInShopAsync_fails_when_actual_arrival_is_in_the_future()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(new Job
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 105, JobTypeId = 10,
            JobNo = "JB1", Status = JobStatus.WaitInspect, AppointmentAt = DateTime.UtcNow.AddDays(1)
        });
        var service = CreateService(jobs);

        var result = await service.ConvertToInShopAsync(
            TestJobId, new ConvertToInShopRequest(DateTimeOffset.UtcNow.AddHours(1)));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_VALIDATION");
        result.Error!.Field.Should().Be("actualArrivalAt");
    }

    [Fact]
    public async Task ConvertToInShopAsync_fails_for_a_job_of_another_branch()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(new Job
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 999, JobTypeId = 10,
            JobNo = "JB1", Status = JobStatus.WaitInspect, AppointmentAt = DateTime.UtcNow.AddDays(1)
        });
        var service = CreateService(jobs);

        var result = await service.ConvertToInShopAsync(TestJobId, new ConvertToInShopRequest(DateTimeOffset.UtcNow));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_NOT_FOUND");
    }

    [Fact]
    public async Task GetCalendarAsync_rejects_a_window_longer_than_the_allowed_range()
    {
        var service = CreateService(new FakeJobRepository());
        var from = DateTimeOffset.UtcNow;

        var result = await service.GetCalendarAsync(from, from.AddDays(200), null, null);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_CALENDAR_RANGE");
    }

    [Fact]
    public async Task GetCalendarAsync_returns_only_jobs_with_an_appointment_in_range_sorted_ascending()
    {
        var jobs = new FakeJobRepository();
        var from = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var to = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        jobs.Seed(new Job
        {
            LegacyShardKey = "db2", BranchId = 105, JobNo = "JB-LATE", Status = JobStatus.WaitInspect,
            AppointmentAt = from.AddDays(20)
        });
        jobs.Seed(new Job
        {
            LegacyShardKey = "db2", BranchId = 105, JobNo = "JB-EARLY", Status = JobStatus.WaitInspect,
            AppointmentAt = from.AddDays(5)
        });
        jobs.Seed(new Job // ไม่มีวันนัด — ต้องไม่ติดมาด้วย
        {
            LegacyShardKey = "db2", BranchId = 105, JobNo = "JB-NONE", Status = JobStatus.WaitInspect,
            AppointmentAt = null
        });
        jobs.Seed(new Job // อยู่นอกช่วง — ต้องไม่ติดมาด้วย
        {
            LegacyShardKey = "db2", BranchId = 105, JobNo = "JB-OUTSIDE", Status = JobStatus.WaitInspect,
            AppointmentAt = to.AddDays(5)
        });
        var service = CreateService(jobs);

        var result = await service.GetCalendarAsync(from, to, null, null);

        result.Success.Should().BeTrue();
        result.Data!.Items.Select(i => i.JobNo).Should().Equal("JB-EARLY", "JB-LATE");
        result.Data!.Truncated.Should().BeFalse();
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

    [Fact]
    public async Task TransitionAsync_closes_the_job_from_mobile_once_payment_receipt_and_handover_are_done()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Ready));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 1000, ApprovalStatus = LineApprovalStatus.Approved
        });

        var payments = new List<Payment> { new() { JobId = TestJobId, Amount = 1070m } };
        var receipt = new Receipt { JobId = TestJobId, DocumentNo = "RC-26-0002", TotalAmount = 1070m };
        var handover = new HandoverRecord { JobId = TestJobId, SubmittedAt = DateTime.UtcNow };

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            posRepo: new FakePosRepository(payments, receipt), handoverRepo: new FakeHandoverRepository(handover),
            role: UserRole.Cashier, source: EventSource.Mobile);

        // guard ทั้งสามตัวคำนวณจากข้อมูลจริง จึงปิดงานได้โดยไม่ต้องส่ง reason
        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("completed", null));

        result.Success.Should().BeTrue();
        jobs.Saved.Single().Status.Should().Be(JobStatus.Completed);
    }

    /// <summary>
    /// [BIZ] 2026-09-17 — คนที่เพิ่งให้ลูกค้าเซ็นรับรถตรงหน้ารถคือคนที่ควรกดปิดงานต่อได้เลย
    /// ไม่ใช่ต้องเดินกลับไปให้แคชเชียร์กดแทน · เงินถูกตรวจครบแล้วผ่าน guard ก่อนถึงบรรทัดนี้
    /// </summary>
    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Lead)]
    [InlineData(UserRole.FrontDesk)]
    public async Task TransitionAsync_lets_whoever_handed_the_car_back_close_the_job(UserRole role)
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Ready));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 1000, ApprovalStatus = LineApprovalStatus.Approved
        });

        var payments = new List<Payment> { new() { JobId = TestJobId, Amount = 1070m } };
        var receipt = new Receipt { JobId = TestJobId, DocumentNo = "RC-26-0003", TotalAmount = 1070m };
        var handover = new HandoverRecord { JobId = TestJobId, SubmittedAt = DateTime.UtcNow };

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            posRepo: new FakePosRepository(payments, receipt), handoverRepo: new FakeHandoverRepository(handover),
            role: role, source: EventSource.Mobile);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("completed", null));

        result.Success.Should().BeTrue();
        jobs.Saved.Single().Status.Should().Be(JobStatus.Completed);
    }

    /// <summary>บทบาทใหม่ที่เพิ่งเปิดสิทธิ์ยังข้าม guard ไม่ได้ — ยังไม่เซ็นรับรถก็ปิดงานไม่ได้เหมือนเดิม</summary>
    [Fact]
    public async Task TransitionAsync_still_rejects_a_technician_closing_before_the_car_is_handed_over()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Ready));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 1000, ApprovalStatus = LineApprovalStatus.Approved
        });

        var payments = new List<Payment> { new() { JobId = TestJobId, Amount = 1070m } };
        var receipt = new Receipt { JobId = TestJobId, DocumentNo = "RC-26-0004", TotalAmount = 1070m };

        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            posRepo: new FakePosRepository(payments, receipt), handoverRepo: new FakeHandoverRepository(null),
            role: UserRole.Technician, source: EventSource.Mobile);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("completed", "ลูกค้ารีบ"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_GUARD_NOT_SATISFIED");
    }

    [Fact]
    public async Task TransitionAsync_still_rejects_closing_from_mobile_when_the_car_has_not_been_handed_over()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Ready));

        var quotation = new Quotation { JobId = TestJobId, Code = "QT-7042-01" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = 1000, ApprovalStatus = LineApprovalStatus.Approved
        });

        var payments = new List<Payment> { new() { JobId = TestJobId, Amount = 1070m } };
        var receipt = new Receipt { JobId = TestJobId, DocumentNo = "RC-26-0003", TotalAmount = 1070m };

        // ชำระครบและออกใบเสร็จแล้ว แต่ยังไม่ได้เซ็นรับรถ — การเปิดสิทธิ์มือถือต้องไม่ข้าม guard นี้
        var service = CreateService(jobs, quotations: new FakeQuotationRepository(quotation),
            posRepo: new FakePosRepository(payments, receipt), handoverRepo: new FakeHandoverRepository(null),
            role: UserRole.Cashier, source: EventSource.Mobile);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("completed", null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_GUARD_NOT_SATISFIED");
        jobs.Saved.Single().Status.Should().Be(JobStatus.Ready);
    }

    /// <summary>
    /// docs/09 §6 — จ๊อบเปลี่ยนสถานะแล้วคาบเวลาที่ช่างเปิดค้างต้องปิดตาม ในคำขอเดียวกัน
    /// Qc→Ready อยู่ในรายการด้วยแม้เอกสารไม่ได้ระบุ ไม่งั้นคาบที่เปิดตอนกลับมาแก้งานจะค้างตลอดไป
    /// </summary>
    [Theory]
    [InlineData(JobStatus.InProgress, "waitparts", WorkEndReason.WaitParts)]
    [InlineData(JobStatus.InProgress, "qc", WorkEndReason.SentToQc)]
    public async Task TransitionAsync_closes_open_work_intervals_of_the_job(
        JobStatus from, string toStatus, WorkEndReason expected)
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(from));
        var workHook = new FakeWorkIntervalHook();
        var service = CreateService(jobs, role: UserRole.Technician, source: EventSource.Mobile, workHook: workHook);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest(toStatus, "สรุปงานที่ทำเสร็จแล้ว"));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        workHook.ClosedJobs.Should().ContainSingle().Which.Should().Be((TestJobId, expected));
    }

    /// <summary>
    /// Qc→Ready แยกออกมาเพราะ guard QcPassed คำนวณได้จริง จึงต้องมีเช็คลิสต์ที่ผ่านครบ ส่ง reason แทนไม่ได้
    /// เส้นทางนี้ไม่อยู่ในตาราง §6 ของเอกสาร แต่ต้องปิดคาบ ไม่งั้นคาบที่เปิดตอนกลับมาแก้งานค้างตลอดไป
    /// </summary>
    [Fact]
    public async Task TransitionAsync_closes_work_intervals_when_qc_finally_passes()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.Qc));

        var checklist = new QcChecklist
        {
            JobId = TestJobId, TestDriveKm = 5, TestDriveNote = "ขับปกติดี", TestDriveRecordedAt = DateTime.UtcNow
        };
        checklist.Items.Add(new QcChecklistItem
        {
            QcChecklistId = checklist.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า", Result = QcItemResult.Pass
        });

        var workHook = new FakeWorkIntervalHook();
        var service = CreateService(jobs, qcChecklists: new FakeQcChecklistRepository(checklist),
            role: UserRole.Office, source: EventSource.Web, workHook: workHook);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("ready", null));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        workHook.ClosedJobs.Should().ContainSingle().Which.Should().Be((TestJobId, WorkEndReason.SentToQc));
    }

    [Fact]
    public async Task TransitionAsync_leaves_work_intervals_open_when_the_job_is_still_being_repaired()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.WaitParts));
        var workHook = new FakeWorkIntervalHook();
        var service = CreateService(jobs, role: UserRole.Office, source: EventSource.Web, workHook: workHook);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("inprogress", "อะไหล่มาแล้ว"));

        result.Success.Should().BeTrue(result.Error?.MessageTh);
        workHook.ClosedJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task TransitionAsync_reports_the_real_reason_when_the_path_does_not_exist_at_all()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.InProgress));
        var service = CreateService(jobs, role: UserRole.Technician, source: EventSource.Mobile);

        // กำลังซ่อม → พร้อมส่งมอบ ไม่มีเส้นทางนี้ในตาราง ต้องบอกตรงๆ ว่าเปลี่ยนไม่ได้
        // ห้ามตอบว่า "กรุณาระบุเหตุผล" เพราะผู้ใช้พิมพ์เหตุผลแล้วก็ยังไปต่อไม่ได้อยู่ดี
        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("ready", null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_TRANSITION_NOT_ALLOWED");
    }

    [Fact]
    public async Task TransitionAsync_reports_forbidden_source_before_asking_for_a_reason()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(SeedJob(JobStatus.WaitParts));
        // WaitParts→InProgress ทำได้จากเว็บเท่านั้น — มือถือต้องรู้ว่าเป็นเรื่องเครื่อง ไม่ใช่เรื่องเหตุผล
        var service = CreateService(jobs, role: UserRole.Office, source: EventSource.Mobile);

        var result = await service.TransitionAsync(TestJobId, new TransitionJobRequest("inprogress", null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("JOB_TRANSITION_FORBIDDEN_SOURCE");
    }

    [Fact]
    public async Task CountsAsync_groups_open_jobs_by_status_and_counts_overdue_ones()
    {
        var now = DateTime.UtcNow;
        var jobs = new FakeJobRepository();
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 9, JobNo = "JB1", Status = JobStatus.WaitInspect });
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 9, JobNo = "JB2", Status = JobStatus.WaitInspect, PromiseAt = now.AddHours(-3) });
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 9, JobNo = "JB3", Status = JobStatus.Qc, PromiseAt = now.AddHours(+3) });
        // ปิดแล้ว · คนละประเภท · คนละ shard — ต้องไม่ถูกนับทั้งสามกรณี
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 11, JobNo = "JB4", Status = JobStatus.Completed });
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 10, JobNo = "JB5", Status = JobStatus.Ready });
        jobs.Seed(new Job { LegacyShardKey = "db1", BranchId = 105, JobTypeId = 9, JobNo = "JB6", Status = JobStatus.Ready });

        var result = await CreateService(jobs).CountsAsync(9);

        result.Success.Should().BeTrue();
        var data = result.Data!;
        data.TotalOpen.Should().Be(3);
        data.Overdue.Should().Be(1);

        // สถานะที่ยังเดินต่อได้ต้องมาครบทุกตัวรวมที่เป็นศูนย์ เพื่อให้หน้าจอมีรายการคงที่
        data.ByStatus.Should().HaveCount(8);
        data.ByStatus.Select(s => s.Status).Should().ContainInOrder(
            "waitinspect", "waitquote", "waitapprove", "approved",
            "inprogress", "waitparts", "qc", "ready");
        data.ByStatus.Single(s => s.Status == "waitinspect").Count.Should().Be(2);
        data.ByStatus.Single(s => s.Status == "qc").Count.Should().Be(1);
        data.ByStatus.Single(s => s.Status == "waitquote").Count.Should().Be(0);
        // ข้อความไทยต้องมาจาก JobStateMachine ไม่ใช่แมปแยกของ service
        data.ByStatus.Single(s => s.Status == "ready").StatusLabelTh.Should().Be("พร้อมส่งมอบ");
    }

    [Fact]
    public async Task CountsAsync_without_a_type_filter_counts_every_open_job_type()
    {
        var jobs = new FakeJobRepository();
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 9, JobNo = "JB1", Status = JobStatus.WaitInspect });
        jobs.Seed(new Job { LegacyShardKey = "db2", BranchId = 105, JobTypeId = 10, JobNo = "JB2", Status = JobStatus.Ready });

        var result = await CreateService(jobs).CountsAsync(null);

        result.Data!.TotalOpen.Should().Be(2);
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
        EventSource source = EventSource.Web,
        FakeWorkIntervalHook? workHook = null) =>
        new(jobs, quotations ?? new FakeQuotationRepository(null), qcChecklists ?? new FakeQcChecklistRepository(null),
            posRepo ?? new FakePosRepository(), handoverRepo ?? new FakeHandoverRepository(null),
            new FakeJobNumberGenerator(), new FakeCustomerVehicleService(customer, vehicle),
            new FakeLegacyReader(), workHook ?? new FakeWorkIntervalHook(),
            new StubCurrentUser(role, source), TimeProvider.System);

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

        public Task<IReadOnlyList<Job>> GetAppointmentsAsync(
            JobAppointmentQuery query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Job>>(_jobs
                .Where(j => j.LegacyShardKey == query.ShardKey && j.BranchId == query.BranchId
                    && j.AppointmentAt is not null
                    && j.AppointmentAt >= query.FromUtc && j.AppointmentAt < query.ToUtc
                    && (query.Status is null || j.Status == query.Status)
                    && (string.IsNullOrWhiteSpace(query.Keyword)
                        || j.JobNo.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase)
                        || j.VehicleRegistration.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase)
                        || j.CustomerName.Contains(query.Keyword, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(j => j.AppointmentAt)
                .ThenBy(j => j.Id)
                .Take(query.Take)
                .ToList());

        public Task<int> CountOpenAsync(
            string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default) =>
            Task.FromResult(_jobs.Count(j =>
                j.LegacyShardKey == shardKey && j.BranchId == branchId
                && j.Status is not (JobStatus.Completed or JobStatus.Cancelled)
                && (jobTypeId is null || j.JobTypeId == jobTypeId)));

        public Task<IReadOnlyList<JobStatusTally>> CountOpenByStatusAsync(
            string shardKey, int branchId, int? jobTypeId, DateTime nowUtc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<JobStatusTally>>(_jobs
                .Where(j => j.LegacyShardKey == shardKey && j.BranchId == branchId
                    && j.Status is not (JobStatus.Completed or JobStatus.Cancelled)
                    && (jobTypeId == null || j.JobTypeId == jobTypeId))
                .GroupBy(j => j.Status)
                .Select(g => new JobStatusTally(
                    g.Key,
                    g.Count(),
                    g.Count(j => j.PromiseAt != null && j.PromiseAt < nowUtc)))
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
