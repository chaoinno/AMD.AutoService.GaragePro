using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Pos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class PosServiceTests
{
    private static readonly Guid TestJobId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid RequestId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static Quotation ApprovedQuotation(decimal unitPrice = 900m)
    {
        var quotation = new Quotation { JobId = TestJobId, Code = "QT-1" };
        quotation.Lines.Add(new QuotationLine
        {
            QuotationId = quotation.Id, CatalogCode = "PRT-001", Name = "ผ้าเบรกหน้า",
            Type = LineType.Part, Quantity = 1, UnitPrice = unitPrice,
            ApprovalStatus = LineApprovalStatus.Approved
        });
        return quotation;
    }

    [Fact]
    public async Task RecordPaymentAsync_rejects_non_pos_roles()
    {
        var repo = new FakePosRepository();
        var service = CreateService(repo, quotation: ApprovedQuotation(), role: UserRole.Technician);

        var result = await service.RecordPaymentAsync(
            TestJobId, new RecordPaymentRequest("cash", 100m, null, RequestId));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("POS_FORBIDDEN");
    }

    [Fact]
    public async Task RecordPaymentAsync_replays_the_same_result_when_request_id_is_reused_with_identical_data()
    {
        var repo = new FakePosRepository();
        var service = CreateService(repo, quotation: ApprovedQuotation());
        var request = new RecordPaymentRequest("cash", 963m, "ref-1", RequestId);

        var first = await service.RecordPaymentAsync(TestJobId, request);
        var second = await service.RecordPaymentAsync(TestJobId, request);

        first.Success.Should().BeTrue();
        second.Success.Should().BeTrue();
        second.Data!.Id.Should().Be(first.Data!.Id);
        repo.Payments.Should().ContainSingle();
    }

    [Fact]
    public async Task RecordPaymentAsync_rejects_when_request_id_reused_with_different_data()
    {
        var repo = new FakePosRepository();
        var service = CreateService(repo, quotation: ApprovedQuotation());

        var first = await service.RecordPaymentAsync(
            TestJobId, new RecordPaymentRequest("cash", 963m, null, RequestId));
        first.Success.Should().BeTrue();

        var second = await service.RecordPaymentAsync(
            TestJobId, new RecordPaymentRequest("cash", 500m, null, RequestId));

        second.Success.Should().BeFalse();
        second.Error!.Code.Should().Be("POS_CONFLICT");
    }

    [Fact]
    public async Task RecordPaymentAsync_rejects_after_receipt_already_issued()
    {
        var repo = new FakePosRepository(receipt: new Receipt { JobId = TestJobId, DocumentNo = "RC-26-0001" });
        var service = CreateService(repo, quotation: ApprovedQuotation());

        var result = await service.RecordPaymentAsync(
            TestJobId, new RecordPaymentRequest("cash", 963m, null, RequestId));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("POS_RECEIPT_ISSUED");
    }

    [Fact]
    public async Task IssueReceiptAsync_rejects_when_balance_is_not_settled()
    {
        var repo = new FakePosRepository();
        var service = CreateService(repo, quotation: ApprovedQuotation());

        var result = await service.IssueReceiptAsync(TestJobId);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("POS_BALANCE_NOT_SETTLED");
    }

    [Fact]
    public async Task IssueReceiptAsync_issues_once_balance_settled_and_is_idempotent_on_retry()
    {
        var repo = new FakePosRepository(payments: [new Payment { JobId = TestJobId, Amount = 963m }]);
        var service = CreateService(repo, quotation: ApprovedQuotation());

        var first = await service.IssueReceiptAsync(TestJobId);
        first.Success.Should().BeTrue();
        first.Data!.DocumentNo.Should().Be("RC-26-0001");

        var second = await service.IssueReceiptAsync(TestJobId);
        second.Success.Should().BeTrue();
        second.Data!.Id.Should().Be(first.Data.Id);
        repo.Receipts.Should().ContainSingle();
    }

    // ---------- ใบเสนอราคาหลายใบต่อจ๊อบ: บิลแยกเฉพาะใบเสนอราคา ใบเสร็จรวม (2026-10-02) ----------

    private static Quotation SignedQuotation(string code, decimal unitPrice, int version)
    {
        var quotation = ApprovedQuotation(unitPrice);
        quotation.Code = code;
        quotation.Version = version;
        quotation.Status = QuotationStatus.Approved;
        quotation.Approval = new QuotationApproval { QuotationId = quotation.Id, QuotationVersion = version };
        return quotation;
    }

    [Fact]
    public async Task GetSummaryAsync_sums_approved_totals_of_every_active_quotation()
    {
        var superseded = SignedQuotation("QT-0", 5000m, 1);
        superseded.Status = QuotationStatus.Superseded;
        var service = CreateService(new FakePosRepository(),
            quotation: SignedQuotation("QT-1", 900m, 2), others: [SignedQuotation("QT-2", 100m, 3), superseded]);

        var result = await service.GetSummaryAsync(TestJobId);

        result.Success.Should().BeTrue();
        // (900 + 100) × 1.07 — ใบที่ถูกแทนที่ไม่นับ
        result.Data!.GrandTotal.Should().Be(1070m);
        result.Data.QuotationCodes.Should().Equal("QT-1", "QT-2");
        result.Data.AwaitingCustomerQuotationCodes.Should().BeEmpty();
    }

    [Fact]
    public async Task IssueReceiptAsync_rejects_while_another_quotation_still_awaits_the_customer()
    {
        var awaiting = ApprovedQuotation(100m);
        awaiting.Code = "QT-2";
        awaiting.Version = 2;
        awaiting.Status = QuotationStatus.Sent;   // อนุมัติบรรทัดแล้วแต่ยังไม่เซ็น — ยังไม่นับเป็นยอดที่ต้องจ่าย
        var repo = new FakePosRepository(payments: [new Payment { JobId = TestJobId, Amount = 5000m }]);
        var service = CreateService(repo, quotation: SignedQuotation("QT-1", 900m, 1), others: [awaiting]);

        var result = await service.IssueReceiptAsync(TestJobId);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("POS_QUOTATION_AWAITING_CUSTOMER");
        result.Error.MessageTh.Should().Contain("QT-2");
        repo.Receipts.Should().BeEmpty();
    }

    [Fact]
    public async Task IssueReceiptAsync_ignores_a_sent_quotation_whose_lines_were_all_rejected()
    {
        var rejected = ApprovedQuotation(100m);
        rejected.Code = "QT-2";
        rejected.Version = 2;
        rejected.Status = QuotationStatus.Sent;
        rejected.Lines.Single().ApprovalStatus = LineApprovalStatus.Rejected;
        rejected.Lines.Single().RejectReason = "ขอทำครั้งหน้า";
        var repo = new FakePosRepository(payments: [new Payment { JobId = TestJobId, Amount = 963m }]);
        var service = CreateService(repo, quotation: SignedQuotation("QT-1", 900m, 1), others: [rejected]);

        var result = await service.IssueReceiptAsync(TestJobId);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task RemovePaymentAsync_requires_a_reason_and_blocks_once_receipted()
    {
        var payment = new Payment { Id = Guid.NewGuid(), JobId = TestJobId, Amount = 963m };
        var repo = new FakePosRepository(payments: [payment]);
        var service = CreateService(repo, quotation: ApprovedQuotation());

        var withoutReason = await service.RemovePaymentAsync(TestJobId, payment.Id, null);
        withoutReason.Success.Should().BeFalse();
        withoutReason.Error!.Code.Should().Be("POS_VALIDATION");

        var receiptedRepo = new FakePosRepository(
            payments: [payment], receipt: new Receipt { JobId = TestJobId, DocumentNo = "RC-26-0001" });
        var receiptedService = CreateService(receiptedRepo, quotation: ApprovedQuotation());

        var afterReceipt = await receiptedService.RemovePaymentAsync(TestJobId, payment.Id, "บันทึกผิด");
        afterReceipt.Success.Should().BeFalse();
        afterReceipt.Error!.Code.Should().Be("POS_RECEIPT_ISSUED");

        var removed = await service.RemovePaymentAsync(TestJobId, payment.Id, "บันทึกผิด");
        removed.Success.Should().BeTrue();
        repo.Payments.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSummaryAsync_defaults_to_vat_included()
    {
        var repo = new FakePosRepository();
        var service = CreateService(repo, quotation: ApprovedQuotation());

        var result = await service.GetSummaryAsync(TestJobId);

        result.Success.Should().BeTrue();
        result.Data!.VatIncluded.Should().BeTrue();
        result.Data.VatLocked.Should().BeFalse();
        result.Data.VatAmount.Should().Be(63m);
        result.Data.GrandTotal.Should().Be(963m);
    }

    [Fact]
    public async Task SetVatIncludedAsync_recomputes_the_grand_total_without_vat()
    {
        var repo = new FakePosRepository();
        var jobs = new FakeJobRepository();
        var service = CreateService(repo, quotation: ApprovedQuotation(), jobs: jobs);

        var result = await service.SetVatIncludedAsync(TestJobId, false);

        result.Success.Should().BeTrue();
        result.Data!.VatIncluded.Should().BeFalse();
        result.Data.VatAmount.Should().Be(0m);
        result.Data.GrandTotal.Should().Be(900m);

        // ยอดคงเหลือของ GetSummaryAsync ครั้งถัดไปต้องอ่านค่าที่ toggle ไว้แล้ว (ไม่ใช่ค่า default)
        var summary = await service.GetSummaryAsync(TestJobId);
        summary.Data!.VatIncluded.Should().BeFalse();
        summary.Data.GrandTotal.Should().Be(900m);
    }

    [Fact]
    public async Task SetVatIncludedAsync_is_locked_once_a_payment_or_receipt_exists()
    {
        var jobs = new FakeJobRepository();
        var withPayment = new FakePosRepository(payments: [new Payment { JobId = TestJobId, Amount = 100m }]);
        var service = CreateService(withPayment, quotation: ApprovedQuotation(), jobs: jobs);

        var result = await service.SetVatIncludedAsync(TestJobId, false);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("POS_VAT_LOCKED");
    }

    // ---------- helpers ----------

    private static PosService CreateService(
        FakePosRepository repo, Quotation? quotation = null, UserRole role = UserRole.Cashier,
        FakeJobRepository? jobs = null, params Quotation[] others)
    {
        var quotations = new FakeQuotationRepository(quotation);
        quotations.Others.AddRange(others);
        return new(repo, jobs ?? new FakeJobRepository(), quotations,
            new FakeReceiptNumberGenerator(), new StubCurrentUser(role), TimeProvider.System);
    }

    private sealed class StubCurrentUser(UserRole role) : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "แคชเชียร์ ทดสอบ";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public Guid? SessionId => null;
        public bool IsAdministrator => false;
    }

    private sealed class FakeJobRepository(bool vatIncluded = true) : IJobRepository
    {
        // instance เดียวคงอยู่ตลอดชีวิต repo — จำลองการ track/save ของ EF ให้ VatIncluded ที่ toggle แล้วอ่านต่อได้
        private readonly Job _job = new()
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 105, JobNo = "JB2608310105001",
            CustomerName = "ลูกค้าทดสอบ", VehicleRegistration = "1กก-1234", JobTypeId = 9,
            VatIncluded = vatIncluded,
        };

        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult<Job?>(_job);
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
        public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) => Task.FromResult(quotation);
        public Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default) => Task.FromResult(quotation);
        public Task<IReadOnlyList<Quotation>> GetActiveForJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>(
                new[] { quotation }.Concat(Others).Where(q => q is not null && q.Status != QuotationStatus.Superseded)
                    .Select(q => q!).ToList());
        /// <summary>ใบเสนอราคาอื่นของจ๊อบเดียวกัน — จำลองจ๊อบที่มีหลายใบ (บิลแยก)</summary>
        public List<Quotation> Others { get; } = [];
        public Task<IReadOnlyList<Quotation>> GetQueueAsync(
            string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>(quotation is null ? [] : [quotation]);
        public Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(1);
        public Task AddAsync(Quotation q, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeReceiptNumberGenerator : IReceiptNumberGenerator
    {
        private int _sequence;
        public Task<string> NextAsync(string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default) =>
            Task.FromResult($"RC-26-{++_sequence:D4}");
    }

    private sealed class FakePosRepository(
        IReadOnlyList<Payment>? payments = null, Receipt? receipt = null) : IPosRepository
    {
        public List<Payment> Payments { get; } = payments?.ToList() ?? [];
        public List<Receipt> Receipts { get; } = receipt is null ? [] : [receipt];

        public Task<IReadOnlyList<Payment>> GetPaymentsByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Payment>>(Payments.Where(p => p.JobId == jobId).ToList());

        public Task<Payment?> GetPaymentByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(Payments.FirstOrDefault(p => p.RequestId == requestId));

        public Task<Payment?> GetPaymentAsync(Guid jobId, Guid paymentId, CancellationToken ct = default) =>
            Task.FromResult(Payments.FirstOrDefault(p => p.JobId == jobId && p.Id == paymentId));

        public Task AddPaymentAsync(Payment payment, CancellationToken ct = default)
        {
            Payments.Add(payment);
            return Task.CompletedTask;
        }

        public Task RemovePaymentAsync(Payment payment, CancellationToken ct = default)
        {
            Payments.Remove(payment);
            return Task.CompletedTask;
        }

        public Task<Receipt?> GetReceiptByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(Receipts.FirstOrDefault(r => r.JobId == jobId));

        public Task AddReceiptAsync(Receipt receipt, CancellationToken ct = default)
        {
            Receipts.Add(receipt);
            return Task.CompletedTask;
        }

        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
