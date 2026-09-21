using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Handover;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class HandoverServiceTests
{
    private static readonly Guid TestJobId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task GetOrCreateAsync_creates_a_fixed_checklist_on_first_open()
    {
        var repo = new FakeHandoverRepository();
        var service = CreateService(repo);

        var result = await service.GetOrCreateAsync(TestJobId);

        result.Success.Should().BeTrue();
        result.Data!.Items.Should().NotBeEmpty();
        result.Data.IsLocked.Should().BeFalse();
    }

    [Fact]
    public async Task SaveItemAsync_requires_a_note_when_item_is_not_returned()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        var service = CreateService(repo);

        var result = await service.SaveItemAsync(TestJobId, record.Items[0].Id, new SaveHandoverItemRequest(false, null));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("HANDOVER_NOTE_REQUIRED");
    }

    [Fact]
    public async Task SaveItemAsync_accepts_returned_items_without_a_note()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        var service = CreateService(repo);

        var result = await service.SaveItemAsync(TestJobId, record.Items[0].Id, new SaveHandoverItemRequest(true, null));

        result.Success.Should().BeTrue();
        record.Items[0].IsReturned.Should().BeTrue();
    }

    [Fact]
    public async Task SubmitAsync_rejects_when_items_are_undecided_or_signature_missing()
    {
        var repo = new FakeHandoverRepository();
        SeedRecord(repo);
        var service = CreateService(repo);

        var noSignature = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest(""));
        noSignature.Success.Should().BeFalse();
        noSignature.Error!.Code.Should().Be("HANDOVER_SIGNATURE_REQUIRED");

        var incomplete = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));
        incomplete.Success.Should().BeFalse();
        incomplete.Error!.Code.Should().Be("HANDOVER_INCOMPLETE");
    }

    [Fact]
    public async Task SubmitAsync_locks_the_record_once_every_item_is_decided_and_signed()
    {
        var repo = new FakeHandoverRepository();
        var record = SeedRecord(repo);
        foreach (var item in record.Items)
        {
            item.IsReturned = true;
            item.UpdatedAt = DateTime.UtcNow;
        }
        var service = CreateService(repo);

        var result = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));

        result.Success.Should().BeTrue();
        result.Data!.IsLocked.Should().BeTrue();

        var afterLock = await service.SaveItemAsync(TestJobId, record.Items[0].Id, new SaveHandoverItemRequest(false, "เหตุผล"));
        afterLock.Success.Should().BeFalse();
        afterLock.Error!.Code.Should().Be("HANDOVER_LOCKED");
    }

    // ---------- helpers ----------

    private static HandoverRecord SeedRecord(FakeHandoverRepository repo)
    {
        var record = new HandoverRecord { JobId = TestJobId };
        record.Items.Add(new HandoverChecklistItem
        {
            HandoverRecordId = record.Id, ItemCode = "key", Name = "กุญแจรถ"
        });
        record.Items.Add(new HandoverChecklistItem
        {
            HandoverRecordId = record.Id, ItemCode = "manual", Name = "คู่มือรถ"
        });
        repo.Seed(record);
        return record;
    }

    /// <summary>
    /// [BIZ] กฎจริงของร้าน: ลูกค้าจ่ายเงิน → ออกใบเสร็จ → ค่อยส่งมอบรถ
    /// เดิมสามเงื่อนไขนี้ถูกตรวจพร้อมกัน "ตอนปิดงาน" เท่านั้น จึงเซ็นรับรถก่อนจ่ายเงินได้จริง
    /// </summary>
    [Fact]
    public async Task SubmitAsync_refuses_to_hand_the_car_over_before_the_receipt_is_issued()
    {
        var repo = new FakeHandoverRepository();
        DecideEveryItem(SeedRecord(repo));
        var service = CreateService(repo, receiptIssued: false);

        var result = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("HANDOVER_RECEIPT_REQUIRED");
    }

    [Fact]
    public async Task GetOrCreateAsync_reports_whether_the_receipt_is_issued_so_the_app_can_explain_the_disabled_button()
    {
        var withoutReceipt = await CreateService(new FakeHandoverRepository(), receiptIssued: false)
            .GetOrCreateAsync(TestJobId);
        withoutReceipt.Data!.ReceiptIssued.Should().BeFalse();
        withoutReceipt.Data.ReceiptDocumentNo.Should().BeNull();

        var withReceipt = await CreateService(new FakeHandoverRepository()).GetOrCreateAsync(TestJobId);
        withReceipt.Data!.ReceiptIssued.Should().BeTrue();
        withReceipt.Data.ReceiptDocumentNo.Should().Be("RC-26-0001");
    }

    [Fact]
    public async Task GetOrCreateAsync_is_open_to_front_desk_because_handing_the_car_back_is_their_job()
    {
        var result = await CreateService(new FakeHandoverRepository(), UserRole.FrontDesk)
            .GetOrCreateAsync(TestJobId);

        result.Success.Should().BeTrue();
    }

    /// <summary>
    /// ช่าง/หัวหน้าช่างคือคนที่เข็นรถออกมายืนอยู่กับลูกค้าตอนเซ็นรับ — เปิดสิทธิ์ให้แล้ว (2026-09-17)
    /// สิ่งที่กันการส่งมอบก่อนเวลาคือใบเสร็จ ไม่ใช่รายชื่อ role (ดูเทสต์ใบเสร็จด้านบน)
    /// </summary>
    [Theory]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Lead)]
    public async Task Workshop_roles_may_hand_the_car_back_once_the_receipt_exists(UserRole role)
    {
        var repo = new FakeHandoverRepository();
        DecideEveryItem(SeedRecord(repo));
        var service = CreateService(repo, role);

        (await service.GetOrCreateAsync(TestJobId)).Success.Should().BeTrue();

        var submitted = await service.SubmitAsync(TestJobId, new SubmitHandoverRequest("attachments/handover/sig.png"));
        submitted.Success.Should().BeTrue();
        submitted.Data!.IsLocked.Should().BeTrue();
    }

    private static void DecideEveryItem(HandoverRecord record)
    {
        foreach (var item in record.Items)
        {
            item.IsReturned = true;
            item.UpdatedAt = DateTime.UtcNow;
        }
    }

    private static readonly Receipt IssuedReceipt = new()
    {
        JobId = TestJobId, DocumentNo = "RC-26-0001", TotalAmount = 1_000m, IssuedByName = "แคชเชียร์ทดสอบ"
    };

    private static HandoverService CreateService(
        FakeHandoverRepository repo,
        UserRole role = UserRole.Cashier,
        bool receiptIssued = true) =>
        new(repo, new FakeJobRepository(), new FakePosRepository(receiptIssued ? IssuedReceipt : null),
            new StubCurrentUser(role), TimeProvider.System);

    /// <summary>ออกใบเสร็จแล้วหรือยัง — เป็นเงื่อนไขเดียวที่ HandoverService อ่านจากฝั่ง POS</summary>
    private sealed class FakePosRepository(Receipt? receipt) : IPosRepository
    {
        public Task<Receipt?> GetReceiptByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(receipt);

        public Task<IReadOnlyList<Payment>> GetPaymentsByJobAsync(Guid jobId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Payment?> GetPaymentByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task<Payment?> GetPaymentAsync(Guid jobId, Guid paymentId, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddPaymentAsync(Payment payment, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task RemovePaymentAsync(Payment payment, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddReceiptAsync(Receipt r, CancellationToken ct = default) =>
            throw new NotImplementedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class StubCurrentUser(UserRole role = UserRole.Cashier) : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "ผู้ใช้ทดสอบ";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public Guid? SessionId => null;
        public bool IsAdministrator => false;
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<Job?>(new Job
            {
                Id = jobId, LegacyShardKey = "db2", BranchId = 105, JobNo = "JB2608310105001",
                CustomerName = "ลูกค้าทดสอบ", VehicleRegistration = "1กก-1234", JobTypeId = 9,
            });
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

    private sealed class FakeHandoverRepository : IHandoverRepository
    {
        private HandoverRecord? _record;

        public void Seed(HandoverRecord record) => _record = record;

        public Task<HandoverRecord?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(_record);

        public Task AddAsync(HandoverRecord record, CancellationToken ct = default)
        {
            _record = record;
            return Task.CompletedTask;
        }

        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
