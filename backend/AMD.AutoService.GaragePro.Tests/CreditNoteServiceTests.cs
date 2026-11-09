using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Pos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;
using T = AMD.AutoService.GaragePro.Tests.TaxInvoiceServiceTests;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>ใบลดหนี้ CN- — docs/20 §2 (อ้าง IV-/RC- · ผู้จัดการเท่านั้น · บรรทัดต้องมาจากบิล · คืนเงินไม่เกินยอด · Q9)</summary>
public sealed class CreditNoteServiceTests
{
    // ---------- แหล่งบิล ----------

    [Fact]
    public async Task Receipt_source_offers_only_approved_lines_and_issues_a_non_tax_credit_note()
    {
        var f = new Fixture();

        var state = (await f.Service.GetStateAsync(T.TestJobId)).Data!;
        state.SourceType.Should().Be("receipt");
        state.SourceDocumentNo.Should().Be("RC-26-0007");
        state.VatRate.Should().Be(0.07m);
        state.Lines.Select(l => l.Description).Should().Equal("ผ้าเบรกหน้า", "น้ำมันเครื่อง");
        state.CanIssue.Should().BeTrue();

        var note = (await f.Issue(Amount(state.Lines[0].SourceLineId, 100m))).Data!;
        note.DocumentNo.Should().Be("CN-26-0001");
        note.IsTaxDocument.Should().BeFalse();
        note.SourceDocumentNo.Should().Be("RC-26-0007");
        (note.DiffNetAmount, note.VatAmount, note.TotalAmount).Should().Be((100m, 7m, 107m));
        (note.OriginalNetAmount, note.CorrectNetAmount).Should().Be((1000m, 900m));
        note.Buyer.Name.Should().Be("ลูกค้าทดสอบ");
        note.Seller.BranchNo.Should().Be("00000");
        note.JobNo.Should().Be("JB2610020105001");
    }

    [Fact]
    public async Task Job_with_a_tax_invoice_must_credit_the_invoice_lines_not_the_quotation_lines()
    {
        var f = new Fixture(withInvoice: true);
        var state = (await f.Service.GetStateAsync(T.TestJobId)).Data!;
        state.SourceType.Should().Be("taxInvoice");
        state.SourceDocumentNo.Should().Be("IV-26-0003");

        var quotationLineId = f.Quotations.SelectMany(q => q.Lines).First().Id;
        (await f.Issue(Amount(quotationLineId, 10m))).Error!.Code.Should().Be("CREDIT_NOTE_LINE_NOT_IN_SOURCE");

        var note = (await f.Issue(Amount(state.Lines[0].SourceLineId, 10m))).Data!;
        note.IsTaxDocument.Should().BeTrue();
        note.Buyer.Name.Should().Be("บริษัท ผู้ซื้อ จำกัด");
        note.Buyer.TaxId.Should().Be("0105559999999");
        note.Seller.TaxId.Should().Be(T.SellerTaxId);
        note.Seller.BranchNo.Should().Be("00002");   // สำเนาจากใบกำกับ ไม่ใช่ค่าตั้งปัจจุบัน
    }

    [Fact]
    public async Task No_receipt_means_nothing_to_credit()
    {
        var f = new Fixture(noReceipt: true);
        var state = (await f.Service.GetStateAsync(T.TestJobId)).Data!;
        state.CanIssue.Should().BeFalse();
        state.BlockedReasonTh.Should().Contain("ออกใบเสร็จก่อน");
        (await f.Issue(Amount(Guid.NewGuid(), 1m))).Error!.Code.Should().Be("CREDIT_NOTE_SOURCE_REQUIRED");
    }

    [Fact]
    public async Task Job_without_vat_issues_credit_notes_without_vat()
    {
        var f = new Fixture(receipt: T.PaidReceipt(vat: 0m), vatIncluded: false);
        var line = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0];
        var note = (await f.Issue(Amount(line.SourceLineId, 100m))).Data!;
        (note.VatRate, note.VatAmount, note.TotalAmount).Should().Be((0m, 0m, 100m));
    }

    // ---------- สิทธิ์ ----------

    [Fact]
    public async Task Only_managers_issue_but_cashiers_can_see_the_state()
    {
        var cashier = new Fixture(role: UserRole.Cashier);
        var state = (await cashier.Service.GetStateAsync(T.TestJobId)).Data!;
        state.CanIssue.Should().BeFalse();
        state.BlockedReasonTh.Should().Contain("ผู้จัดการ");
        (await cashier.Issue(Amount(state.Lines[0].SourceLineId, 1m))).Error!.Code.Should().Be("CREDIT_NOTE_FORBIDDEN");

        (await new Fixture(role: UserRole.Office).Issue(Amount(state.Lines[0].SourceLineId, 1m)))
            .Error!.Code.Should().Be("CREDIT_NOTE_FORBIDDEN");
        (await new Fixture(role: UserRole.Technician).Service.GetStateAsync(T.TestJobId))
            .Error!.Code.Should().Be("POS_FORBIDDEN");
    }

    // ---------- validation ----------

    [Fact]
    public async Task Lines_and_reason_are_required()
    {
        var f = new Fixture();
        var line = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0];

        (await f.Issue()).Error!.Code.Should().Be("CREDIT_NOTE_LINES_REQUIRED");
        (await f.Issue(reason: "", lines: Amount(line.SourceLineId, 1m))).Error!.Code.Should().Be("CREDIT_NOTE_REASON_REQUIRED");
        (await f.Issue(reason: "other", lines: Amount(line.SourceLineId, 1m))).Error!.Field.Should().Be("reasonText");
        (await f.Issue(reason: "other", reasonText: "ลูกค้าต่อรอง", lines: Amount(line.SourceLineId, 1m))).Success.Should().BeTrue();
    }

    [Fact]
    public async Task Each_line_needs_exactly_one_of_quantity_or_amount()
    {
        var f = new Fixture();
        var id = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0].SourceLineId;

        (await f.Issue(new IssueCreditNoteLineRequest(id, null, null))).Error!.Code.Should().Be("CREDIT_NOTE_VALIDATION");
        (await f.Issue(new IssueCreditNoteLineRequest(id, 1m, 10m))).Error!.Code.Should().Be("CREDIT_NOTE_VALIDATION");
        (await f.Issue(Amount(id, 0.001m))).Error!.Code.Should().Be("CREDIT_NOTE_VALIDATION");
        (await f.Issue(Amount(id, 10m), Amount(id, 5m))).Error!.Code.Should().Be("CREDIT_NOTE_VALIDATION");
    }

    [Fact]
    public async Task Credited_amounts_accumulate_across_notes_and_cannot_exceed_the_line()
    {
        var f = new Fixture();
        var id = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0].SourceLineId;   // 900

        (await f.Issue(Amount(id, 800m))).Success.Should().BeTrue();
        var exceeded = await f.Issue(Amount(id, 200m));
        exceeded.Error!.Code.Should().Be("CREDIT_NOTE_EXCEEDS_LINE");
        exceeded.Error.MessageTh.Should().Contain("100.00");

        var second = (await f.Issue(Amount(id, 100m))).Data!;
        second.DocumentNo.Should().Be("CN-26-0002");
        second.CorrectNetAmount.Should().Be(100m);   // 1,000 − 800 − 100

        var state = (await f.Service.GetStateAsync(T.TestJobId)).Data!;
        state.Lines[0].RemainingAmount.Should().Be(0m);
        state.RemainingNetAmount.Should().Be(100m);
    }

    [Fact]
    public async Task Quantity_credits_are_prorated_and_the_last_unit_takes_the_exact_remainder()
    {
        // 3 ชิ้น รวม 100 บาท — หาร 3 ไม่ลงตัว ชิ้นสุดท้ายต้องได้เศษที่เหลือพอดี ไม่ค้างสตางค์
        var q = T.Signed("QT-1", 1, ("ไส้กรอง", 0m, LineApprovalStatus.Approved));
        q.Lines[0].Quantity = 3; q.Lines[0].UnitPrice = 100m / 3m;
        var f = new Fixture(quotations: [q], receipt: T.PaidReceipt(net: 100m, vat: 7m));
        var line = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0];
        line.OriginalNetAmount.Should().Be(100m);

        (await f.Issue(Qty(line.SourceLineId, 1m))).Data!.DiffNetAmount.Should().Be(33.33m);
        (await f.Issue(Qty(line.SourceLineId, 3m))).Error!.Code.Should().Be("CREDIT_NOTE_EXCEEDS_LINE");
        (await f.Issue(Qty(line.SourceLineId, 2m))).Data!.DiffNetAmount.Should().Be(66.67m);

        var after = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0];
        (after.RemainingQuantity, after.RemainingAmount).Should().Be((0m, 0m));
    }

    [Fact]
    public async Task Same_request_id_returns_the_same_note_and_a_different_payload_conflicts()
    {
        var f = new Fixture();
        var id = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0].SourceLineId;
        var request = new IssueCreditNoteRequest(Guid.NewGuid(), "overcharged", null, [Amount(id, 50m)], null);

        var first = (await f.Service.IssueAsync(T.TestJobId, request)).Data!;
        var again = (await f.Service.IssueAsync(T.TestJobId, request)).Data!;
        again.Id.Should().Be(first.Id);
        f.Repo.Notes.Should().HaveCount(1);

        (await f.Service.IssueAsync(T.TestJobId, request with { Lines = [Amount(id, 60m)] }))
            .Error!.Code.Should().Be("CREDIT_NOTE_CONFLICT");
    }

    // ---------- คืนเงิน ----------

    [Fact]
    public async Task Refunds_can_be_recorded_with_the_note_or_later_but_never_beyond_its_total()
    {
        var f = new Fixture();
        var id = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0].SourceLineId;

        (await f.Issue(refund: Refund(200m), lines: Amount(id, 100m))).Error!.Code.Should().Be("CREDIT_NOTE_REFUND_EXCEEDS");

        var note = (await f.Issue(refund: Refund(50m), lines: Amount(id, 100m))).Data!;   // รวม 107
        (note.RefundedAmount, note.RefundStatus).Should().Be((50m, "partial"));

        (await f.Service.RecordRefundAsync(note.Id, Refund(60m))).Error!.Code.Should().Be("CREDIT_NOTE_REFUND_EXCEEDS");

        var last = Refund(57m);
        (await f.Service.RecordRefundAsync(note.Id, last)).Data!.RefundStatus.Should().Be("full");
        // retry ด้วย RequestId เดิม ไม่คืนเงินซ้ำ
        (await f.Service.RecordRefundAsync(note.Id, last)).Data!.RefundedAmount.Should().Be(107m);
        f.Repo.Notes.Single().Refunds.Should().HaveCount(2);

        (await new Fixture(role: UserRole.Cashier, repo: f.Repo).Service.RecordRefundAsync(note.Id, Refund(1m)))
            .Error!.Code.Should().Be("CREDIT_NOTE_FORBIDDEN");
    }

    // ---------- Q9 ----------

    [Fact]
    public async Task Tax_invoice_is_blocked_once_the_receipt_was_credited()
    {
        var f = new Fixture();
        var id = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0].SourceLineId;
        (await f.Issue(Amount(id, 100m))).Success.Should().BeTrue();

        var taxInvoice = new TaxInvoiceService(new T.FakeTaxInvoiceRepository(), new T.FakePosRepository(T.PaidReceipt()),
            new T.FakeJobRepository(true), new T.FakeQuotationRepository(T.TwoQuotations()), new T.FakeLegacyReader(T.SellerTaxId),
            new T.FakeCustomerService(null), new T.StubCurrentUser(UserRole.Cashier), TimeProvider.System, f.Repo);

        var state = (await taxInvoice.GetStateAsync(T.TestJobId)).Data!;
        state.CanIssue.Should().BeFalse();
        state.BlockedReasonTh.Should().Contain("ใบลดหนี้");
        (await taxInvoice.IssueAsync(T.TestJobId, new IssueTaxInvoiceRequest("ก", "ข", null, null)))
            .Error!.Code.Should().Be("TAX_INVOICE_RECEIPT_CREDITED");
    }

    [Fact]
    public async Task Tax_invoice_prints_the_branch_tax_profile_of_the_seller()
    {
        var f = new Fixture();
        f.Repo.Profile = new BranchTaxProfile { LegacyShardKey = "db2", LegacyBranchId = 105, SellerBranchNo = "00004" };
        var taxInvoice = new TaxInvoiceService(new T.FakeTaxInvoiceRepository(), new T.FakePosRepository(T.PaidReceipt()),
            new T.FakeJobRepository(true), new T.FakeQuotationRepository(T.TwoQuotations()), new T.FakeLegacyReader(T.SellerTaxId),
            new T.FakeCustomerService(null), new T.StubCurrentUser(UserRole.Cashier), TimeProvider.System, f.Repo);

        var issued = (await taxInvoice.IssueAsync(T.TestJobId, new IssueTaxInvoiceRequest("ก", "ข", null, null))).Data!;
        issued.Seller.BranchNo.Should().Be("00004");
    }

    // ---------- ค่าตั้งภาษีสาขา ----------

    [Fact]
    public async Task Branch_tax_profile_defaults_to_head_office_and_only_managers_change_it()
    {
        var f = new Fixture();
        var initial = (await f.Service.GetProfileAsync()).Data!;
        (initial.SellerBranchNo, initial.IsDefault, initial.CanEdit).Should().Be(("00000", true, true));

        (await f.Service.UpdateProfileAsync(new("123"))).Error!.Field.Should().Be("sellerBranchNo");
        (await f.Service.UpdateProfileAsync(new("0000a"))).Error!.Field.Should().Be("sellerBranchNo");
        (await f.Service.UpdateProfileAsync(new("00003"))).Data!.SellerBranchNo.Should().Be("00003");

        var cashier = new Fixture(role: UserRole.Cashier, repo: f.Repo);
        (await cashier.Service.GetProfileAsync()).Data!.CanEdit.Should().BeFalse();
        (await cashier.Service.UpdateProfileAsync(new("00001"))).Error!.Code.Should().Be("CREDIT_NOTE_FORBIDDEN");

        // ใบลดหนี้ที่อ้างใบเสร็จใช้ค่าตั้งปัจจุบัน
        var id = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0].SourceLineId;
        (await f.Issue(Amount(id, 1m))).Data!.Seller.BranchNo.Should().Be("00003");
    }

    // ---------- รายงาน ----------

    [Fact]
    public async Task Report_is_for_managers_and_office_with_a_bounded_range()
    {
        var f = new Fixture();
        var id = (await f.Service.GetStateAsync(T.TestJobId)).Data!.Lines[0].SourceLineId;
        await f.Issue(refund: Refund(107m), lines: Amount(id, 100m));
        await f.Issue(Amount(id, 200m));

        var report = (await new Fixture(role: UserRole.Office, repo: f.Repo).Service.ReportAsync(null, null)).Data!;
        report.Count.Should().Be(2);
        (report.DiffNetAmount, report.VatAmount, report.TotalAmount).Should().Be((300m, 21m, 321m));
        (report.RefundedAmount, report.OutstandingRefund).Should().Be((107m, 214m));

        (await new Fixture(role: UserRole.Cashier, repo: f.Repo).Service.ReportAsync(null, null))
            .Error!.Code.Should().Be("CREDIT_NOTE_FORBIDDEN");
        (await f.Service.ReportAsync(new DateOnly(2025, 1, 1), new DateOnly(2026, 6, 1))).Error!.Field.Should().Be("to");
        (await f.Service.ReportAsync(new DateOnly(2026, 6, 2), new DateOnly(2026, 6, 1))).Error!.Field.Should().Be("to");
    }

    // ---------- helpers ----------

    private static IssueCreditNoteLineRequest Amount(Guid id, decimal amount) => new(id, null, amount);
    private static IssueCreditNoteLineRequest Qty(Guid id, decimal quantity) => new(id, quantity, null);
    private static RecordCreditNoteRefundRequest Refund(decimal amount) => new(Guid.NewGuid(), "cash", amount, null);

    private sealed class Fixture
    {
        public FakeCreditNoteRepository Repo { get; }
        public CreditNoteService Service { get; }
        public Quotation[] Quotations { get; }

        public Fixture(UserRole role = UserRole.Manager, Quotation[]? quotations = null, bool withInvoice = false,
            bool vatIncluded = true, FakeCreditNoteRepository? repo = null, Receipt? receipt = null, bool noReceipt = false)
        {
            Repo = repo ?? new FakeCreditNoteRepository();
            Quotations = quotations ?? T.TwoQuotations();
            var invoices = new T.FakeTaxInvoiceRepository();
            if (withInvoice)
                invoices.Invoices.Add(new TaxInvoice
                {
                    JobId = T.TestJobId, DocumentNo = "IV-26-0003", SellerName = "อู่ทดสอบ", SellerTaxId = T.SellerTaxId,
                    SellerBranchNo = "00002", BuyerName = "บริษัท ผู้ซื้อ จำกัด", BuyerAddress = "99 ถ.สุขุมวิท",
                    BuyerTaxId = "0105559999999", BuyerBranchNo = "00000", VatRate = 0.07m,
                    NetAmount = 1000m, VatAmount = 70m, TotalAmount = 1070m, IssuedAt = DateTime.UtcNow,
                    Lines =
                    [
                        new TaxInvoiceLine { Sequence = 1, QuotationCode = "QT-1", Description = "ผ้าเบรกหน้า", Quantity = 1, Unit = "ชิ้น", NetAmount = 900m },
                        new TaxInvoiceLine { Sequence = 2, QuotationCode = "QT-2", Description = "น้ำมันเครื่อง", Quantity = 1, Unit = "ชิ้น", NetAmount = 100m },
                    ]
                });
            Service = new CreditNoteService(
                Repo, invoices,
                new T.FakePosRepository(noReceipt ? null : receipt ?? T.PaidReceipt()),
                new T.FakeJobRepository(vatIncluded),
                new T.FakeQuotationRepository(Quotations),
                new T.FakeLegacyReader(T.SellerTaxId),
                new T.StubCurrentUser(role),
                TimeProvider.System);
        }

        public Task<Application.Common.Result<CreditNoteDto>> Issue(params IssueCreditNoteLineRequest[] lines) =>
            Issue("defect", null, null, lines);

        public Task<Application.Common.Result<CreditNoteDto>> Issue(
            string reason = "defect", string? reasonText = null, RecordCreditNoteRefundRequest? refund = null,
            params IssueCreditNoteLineRequest[] lines) =>
            Service.IssueAsync(T.TestJobId, new IssueCreditNoteRequest(Guid.NewGuid(), reason, reasonText, lines, refund));
    }

    internal sealed class FakeCreditNoteRepository : ICreditNoteRepository
    {
        private int _sequence;
        public List<CreditNote> Notes { get; } = [];
        public BranchTaxProfile? Profile { get; set; }

        public Task<IReadOnlyList<CreditNote>> GetByJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CreditNote>>(Notes.Where(n => n.JobId == jobId).ToList());
        public Task<CreditNote?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(Notes.FirstOrDefault(n => n.Id == id));
        public Task<CreditNote?> GetByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(Notes.FirstOrDefault(n => n.RequestId == requestId));
        public Task<bool> RefundRequestExistsAsync(Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(Notes.SelectMany(n => n.Refunds).Any(r => r.RequestId == requestId));
        public Task<IReadOnlyList<CreditNote>> SearchAsync(
            string shardKey, int branchId, DateTime fromUtc, DateTime toUtc, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CreditNote>>(Notes
                .Where(n => n.LegacyShardKey == shardKey && n.LegacyBranchId == branchId && n.IssuedAt >= fromUtc && n.IssuedAt < toUtc)
                .ToList());
        public Task<string> NextDocumentNoAsync(string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default) =>
            Task.FromResult($"CN-26-{++_sequence:D4}");
        public Task<BranchTaxProfile?> GetProfileAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult(Profile);
        public Task AddProfileAsync(BranchTaxProfile profile, CancellationToken ct = default)
        {
            Profile = profile;
            return Task.CompletedTask;
        }
        public Task AddAsync(CreditNote note, CancellationToken ct = default)
        {
            Notes.Add(note);
            return Task.CompletedTask;
        }
        public Task AddRefundAsync(CreditNoteRefund refund, CancellationToken ct = default) => Task.CompletedTask;
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
