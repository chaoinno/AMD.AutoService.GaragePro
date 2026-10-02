using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Pos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class TaxInvoiceServiceTests
{
    private static readonly Guid TestJobId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private const string SellerTaxId = "0105551234567";

    private static readonly IssueTaxInvoiceRequest Buyer =
        new("บริษัท ทดสอบ จำกัด", "99 ถ.สุขุมวิท กรุงเทพฯ 10110", "0105559999999", "00000");

    private static Quotation Signed(string code, int version, params (string Name, decimal Price, LineApprovalStatus Status)[] lines)
    {
        var q = new Quotation
        {
            JobId = TestJobId, Code = code, Version = version, Status = QuotationStatus.Approved, VatRate = 0.07m,
        };
        var seq = 1;
        foreach (var (name, price, status) in lines)
            q.Lines.Add(new QuotationLine
            {
                QuotationId = q.Id, Sequence = seq++, CatalogCode = $"C-{name}", Name = name, Type = LineType.Part,
                Quantity = 1, Unit = "ชิ้น", UnitPrice = price, ApprovalStatus = status
            });
        q.Approval = new QuotationApproval { QuotationId = q.Id, QuotationVersion = version };
        return q;
    }

    // QT-1: 900 (+ บรรทัดที่ไม่อนุมัติ) · QT-2: 100 → สุทธิ 1,000 VAT 70 รวม 1,070 (คิด VAT ต่อใบแล้วรวม)
    private static Quotation[] TwoQuotations() =>
    [
        Signed("QT-1", 1, ("ผ้าเบรกหน้า", 900m, LineApprovalStatus.Approved), ("ใบปัดน้ำฝน", 300m, LineApprovalStatus.Rejected)),
        Signed("QT-2", 2, ("น้ำมันเครื่อง", 100m, LineApprovalStatus.Approved)),
    ];

    private static Receipt PaidReceipt(decimal net = 1000m, decimal vat = 70m) => new()
    {
        JobId = TestJobId, DocumentNo = "RC-26-0007", NetAmount = net, VatAmount = vat, TotalAmount = net + vat,
    };

    [Fact]
    public async Task IssueAsync_requires_a_receipt_first()
    {
        var (service, _) = Create(receipt: null);

        var state = await service.GetStateAsync(TestJobId);
        state.Data!.CanIssue.Should().BeFalse();
        state.Data.BlockedReasonTh.Should().Contain("ออกใบเสร็จก่อน");

        var result = await service.IssueAsync(TestJobId, Buyer);
        result.Error!.Code.Should().Be("TAX_INVOICE_RECEIPT_REQUIRED");
    }

    [Fact]
    public async Task IssueAsync_rejects_jobs_that_do_not_charge_vat()
    {
        var (service, _) = Create(receipt: PaidReceipt(vat: 0m), vatIncluded: false);

        var result = await service.IssueAsync(TestJobId, Buyer);

        result.Error!.Code.Should().Be("TAX_INVOICE_NO_VAT");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    public async Task IssueAsync_rejects_when_the_branch_has_no_valid_seller_tax_id(string? branchTaxId)
    {
        var (service, _) = Create(receipt: PaidReceipt(), branchTaxId: branchTaxId);

        var result = await service.IssueAsync(TestJobId, Buyer);

        result.Error!.Code.Should().Be("TAX_INVOICE_SELLER_TAX_ID_MISSING");
    }

    [Fact]
    public async Task IssueAsync_snapshots_approved_lines_of_every_quotation_and_takes_totals_from_the_receipt()
    {
        var (service, repo) = Create(receipt: PaidReceipt(), branchTaxId: "0-1055-51234-56-7");

        var result = await service.IssueAsync(TestJobId, Buyer);

        result.Success.Should().BeTrue();
        var dto = result.Data!;
        dto.DocumentNo.Should().Be("IV-26-0001");
        dto.ReceiptDocumentNo.Should().Be("RC-26-0007");
        dto.Seller.TaxId.Should().Be(SellerTaxId, "เก็บเฉพาะตัวเลข 13 หลัก แม้ข้อมูลเดิมมีขีด");
        dto.Buyer.Should().BeEquivalentTo(new TaxInvoicePartyDto(
            "บริษัท ทดสอบ จำกัด", "99 ถ.สุขุมวิท กรุงเทพฯ 10110", "0105559999999", null, "00000"));
        dto.Lines.Select(l => (l.Sequence, l.QuotationCode, l.Description, l.NetAmount)).Should().Equal(
            (1, "QT-1", "ผ้าเบรกหน้า", 900m),
            (2, "QT-2", "น้ำมันเครื่อง", 100m));
        dto.NetAmount.Should().Be(1000m);
        dto.VatAmount.Should().Be(70m);
        dto.TotalAmount.Should().Be(1070m);
        repo.Invoices.Should().ContainSingle();
    }

    [Fact]
    public async Task IssueAsync_is_idempotent_and_keeps_the_first_buyer_details()
    {
        var (service, repo) = Create(receipt: PaidReceipt());

        var first = await service.IssueAsync(TestJobId, Buyer);
        var retry = await service.IssueAsync(TestJobId, Buyer with { BuyerName = "ชื่ออื่น" });

        retry.Data!.Id.Should().Be(first.Data!.Id);
        retry.Data.DocumentNo.Should().Be("IV-26-0001");
        retry.Data.Buyer.Name.Should().Be("บริษัท ทดสอบ จำกัด");
        repo.Invoices.Should().ContainSingle();

        var state = await service.GetStateAsync(TestJobId);
        state.Data!.Issued!.Id.Should().Be(first.Data.Id);
        state.Data.CanIssue.Should().BeFalse();
    }

    [Theory]
    [InlineData("", "ที่อยู่", null, null, "buyerName")]
    [InlineData("ลูกค้า", "  ", null, null, "buyerAddress")]
    [InlineData("ลูกค้า", "ที่อยู่", "123", null, "buyerTaxId")]
    [InlineData("ลูกค้า", "ที่อยู่", "0105559999999", "12", "buyerBranchNo")]
    [InlineData("ลูกค้า", "ที่อยู่", null, "00000", "buyerTaxId")]
    public async Task IssueAsync_validates_buyer_details(
        string name, string address, string? taxId, string? branchNo, string field)
    {
        var (service, repo) = Create(receipt: PaidReceipt());

        var result = await service.IssueAsync(TestJobId, new IssueTaxInvoiceRequest(name, address, taxId, branchNo));

        result.Error!.Code.Should().Be("TAX_INVOICE_VALIDATION");
        result.Error.Field.Should().Be(field);
        repo.Invoices.Should().BeEmpty();
    }

    [Fact]
    public async Task IssueAsync_accepts_an_individual_buyer_without_tax_id()
    {
        var (service, _) = Create(receipt: PaidReceipt());

        var result = await service.IssueAsync(TestJobId, new IssueTaxInvoiceRequest("สมชาย ใจดี", "1 หมู่ 2 ต.ในเมือง", null, null));

        result.Success.Should().BeTrue();
        result.Data!.Buyer.TaxId.Should().BeNull();
        result.Data.Buyer.BranchNo.Should().BeNull();
    }

    [Fact]
    public async Task IssueAsync_refuses_when_approved_lines_do_not_add_up_to_the_receipt()
    {
        var (service, repo) = Create(receipt: PaidReceipt(net: 1200m, vat: 84m));

        var result = await service.IssueAsync(TestJobId, Buyer);

        result.Error!.Code.Should().Be("TAX_INVOICE_TOTAL_MISMATCH");
        repo.Invoices.Should().BeEmpty();
    }

    [Fact]
    public async Task GetStateAsync_prefills_buyer_from_the_customer_record()
    {
        var customer = Customer(idCard: "1-1002-00345-67-8");
        var (service, _) = Create(receipt: PaidReceipt(), customer: customer);

        var state = await service.GetStateAsync(TestJobId);

        state.Data!.CanIssue.Should().BeTrue();
        state.Data.Seller!.TaxId.Should().Be(SellerTaxId);
        state.Data.Prefill.Should().Be(new IssueTaxInvoiceRequest(
            "สมชาย ใจดี", "12/3 หมู่ 4 ต.ในเมือง อ.เมืองขอนแก่น จ.ขอนแก่น 40000", "1100200345678", null));
    }

    [Fact]
    public async Task GetStateAsync_falls_back_to_job_customer_name_when_the_customer_cannot_be_read()
    {
        var (service, _) = Create(receipt: PaidReceipt(), customer: null);

        var state = await service.GetStateAsync(TestJobId);

        state.Data!.Prefill.Should().Be(new IssueTaxInvoiceRequest("ลูกค้าทดสอบ", "", null, null));
    }

    [Fact]
    public void FormatAddress_uses_khwaeng_khet_for_bangkok()
    {
        var address = TaxInvoiceService.FormatAddress(Customer() with
        {
            DistrictName = "คลองตัน", AmphureName = "คลองเตย", ProvinceName = "กรุงเทพมหานคร", ZipCode = "10110"
        });

        address.Should().Be("12/3 หมู่ 4 แขวงคลองตัน เขตคลองเตย กรุงเทพมหานคร 10110");
    }

    [Fact]
    public async Task Non_pos_roles_are_forbidden()
    {
        var (service, _) = Create(receipt: PaidReceipt(), role: UserRole.Technician);

        (await service.GetStateAsync(TestJobId)).Error!.Code.Should().Be("POS_FORBIDDEN");
        (await service.IssueAsync(TestJobId, Buyer)).Error!.Code.Should().Be("POS_FORBIDDEN");
    }

    // ---------- helpers ----------

    private static CustomerDetailDto Customer(string? idCard = null) => new(
        Id: 501, Code: "C501", FirstName: "สมชาย", LastName: "ใจดี", IdCard: idCard, DriverLicense: null,
        GenderId: null, DateOfBirth: null, Address1: "12/3", Address2: "หมู่ 4", ProvinceId: null,
        ProvinceName: "ขอนแก่น", AmphureId: null, AmphureName: "เมืองขอนแก่น", DistrictId: null,
        DistrictName: "ในเมือง", ZipCode: "40000", PhoneNumber1: null, PhoneNumber2: null, Email: null,
        LineId: null, IsBlacklist: false, BlacklistRemark: null, IsDeleted: false, CreatedDate: null,
        LastUpdated: null, Vehicles: []);

    private static (TaxInvoiceService Service, FakeTaxInvoiceRepository Repo) Create(
        Receipt? receipt, bool vatIncluded = true, string? branchTaxId = SellerTaxId,
        CustomerDetailDto? customer = null, UserRole role = UserRole.Cashier, Quotation[]? quotations = null)
    {
        var repo = new FakeTaxInvoiceRepository();
        var service = new TaxInvoiceService(
            repo,
            new FakePosRepository(receipt),
            new FakeJobRepository(vatIncluded),
            new FakeQuotationRepository(quotations ?? TwoQuotations()),
            new FakeLegacyReader(branchTaxId),
            new FakeCustomerService(customer),
            new StubCurrentUser(role),
            TimeProvider.System);
        return (service, repo);
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

    private sealed class FakeTaxInvoiceRepository : ITaxInvoiceRepository
    {
        private int _sequence;
        public List<TaxInvoice> Invoices { get; } = [];

        public Task<TaxInvoice?> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(Invoices.FirstOrDefault(x => x.JobId == jobId));
        public Task<string> NextDocumentNoAsync(string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default) =>
            Task.FromResult($"IV-26-{++_sequence:D4}");
        public Task AddAsync(TaxInvoice invoice, CancellationToken ct = default)
        {
            Invoices.Add(invoice);
            return Task.CompletedTask;
        }
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakePosRepository(Receipt? receipt) : IPosRepository
    {
        public Task<IReadOnlyList<Payment>> GetPaymentsByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Payment>>([]);
        public Task<Payment?> GetPaymentByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
            Task.FromResult<Payment?>(null);
        public Task<Payment?> GetPaymentAsync(Guid jobId, Guid paymentId, CancellationToken ct = default) =>
            Task.FromResult<Payment?>(null);
        public Task AddPaymentAsync(Payment payment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RemovePaymentAsync(Payment payment, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Receipt?> GetReceiptByJobAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(receipt);
        public Task AddReceiptAsync(Receipt r, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeJobRepository(bool vatIncluded) : IJobRepository
    {
        private readonly Job _job = new()
        {
            Id = TestJobId, LegacyShardKey = "db2", BranchId = 105, JobNo = "JB2610020105001",
            CustomerId = 501, CustomerName = "ลูกค้าทดสอบ", VehicleRegistration = "1กก-1234", JobTypeId = 9,
            VatIncluded = vatIncluded,
        };

        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult<Job?>(_job);
        public Task<Job?> GetOpenByVehicleAsync(string shardKey, int branchId, long vehicleId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Job>> GetAppointmentsAsync(JobAppointmentQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<int> CountOpenAsync(string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<JobStatusTally>> CountOpenByStatusAsync(
            string shardKey, int branchId, int? jobTypeId, DateTime nowUtc, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task AddAsync(Job job, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeQuotationRepository(IReadOnlyList<Quotation> quotations) : IQuotationRepository
    {
        public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(quotations.FirstOrDefault(q => q.Id == id));
        public Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default) => GetAsync(id, ct);
        public Task<IReadOnlyList<Quotation>> GetActiveForJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>(
                quotations.Where(q => q.Status != QuotationStatus.Superseded).OrderBy(q => q.Version).ToList());
        public Task<IReadOnlyList<Quotation>> GetQueueAsync(
            string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default) =>
            Task.FromResult(quotations);
        public Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default) => Task.FromResult(1);
        public Task AddAsync(Quotation q, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeLegacyReader(string? taxId) : ILegacyReader
    {
        public Task<LegacyBranchDto?> GetBranchAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult<LegacyBranchDto?>(new LegacyBranchDto(branchId, "อู่ทดสอบ", "1 ถ.มิตรภาพ ขอนแก่น", taxId, "043000000"));
        public Task<IReadOnlyList<LegacyTechnicianDto>> GetTechniciansAsync(
            string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LegacyTechnicianDto>>([]);
    }

    private sealed class FakeCustomerService(CustomerDetailDto? customer) : ICustomerVehicleService
    {
        public Task<Result<CustomerDetailDto>> GetCustomerAsync(long id, CancellationToken ct = default) =>
            Task.FromResult(customer is null
                ? Result<CustomerDetailDto>.Fail("CUSTOMER_NOT_FOUND", "ไม่พบข้อมูลลูกค้า")
                : Result<CustomerDetailDto>.Ok(customer));

        public Task<Result<PagedResult<CustomerSummaryDto>>> SearchCustomersAsync(
            CustomerSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<CustomerSummaryDto>>> ExportCustomersAsync(
            CustomerSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CustomerDetailDto>> CreateCustomerAsync(
            CustomerUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CustomerDetailDto>> UpdateCustomerAsync(
            long id, CustomerUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> DeleteCustomerAsync(long id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Result<PagedResult<VehicleSummaryDto>>> SearchVehiclesAsync(
            VehicleSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VehicleSummaryDto>>> ExportVehiclesAsync(
            VehicleSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> GetVehicleAsync(long id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> CreateVehicleAsync(
            VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> UpdateVehicleAsync(
            long id, VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Result<bool>> DeleteVehicleAsync(long id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Result<VehicleImageFile>> OpenVehicleImageAsync(long id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> UpdateVehicleImageAsync(
            long id, VehicleImageUpload image, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
