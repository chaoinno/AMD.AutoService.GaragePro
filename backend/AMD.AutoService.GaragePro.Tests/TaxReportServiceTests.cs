using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Pos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;
using T = AMD.AutoService.GaragePro.Tests.TaxInvoiceServiceTests;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>รายงานภาษีขาย/ภาษีซื้อ + ใบกำกับภาษีซื้อ — docs/20 §3–4 (Q1 Q4 Q5 Q7)</summary>
public sealed class TaxReportServiceTests
{
    // 2026-11-09 10:00 เวลาไทย
    private static readonly DateTime NowUtc = new(2026, 11, 9, 3, 0, 0, DateTimeKind.Utc);

    /// <summary>เวลาไทย → UTC</summary>
    private static DateTime Thai(int y, int m, int d, int h = 10) => new DateTime(y, m, d, h, 0, 0, DateTimeKind.Utc).AddHours(-7);

    // ---------- ภาษีขาย ----------

    [Fact]
    public async Task Output_vat_puts_invoices_and_their_credit_notes_in_part_one_and_uninvoiced_money_in_part_two()
    {
        var f = new Fixture();
        f.Repo.Invoices.Add(Invoice("IV-26-0002", Thai(2026, 11, 5), 2000m, 140m));
        f.Repo.Invoices.Add(Invoice("IV-26-0001", Thai(2026, 11, 3), 1000m, 70m));
        f.Repo.Notes.Add(Credit("CN-26-0001", Thai(2026, 11, 6), CreditNoteSourceType.TaxInvoice, "IV-26-0001", 100m, 7m));
        f.Repo.Notes.Add(Credit("CN-26-0002", Thai(2026, 11, 7), CreditNoteSourceType.Receipt, "RC-26-0009", 50m, 3.5m));
        f.Repo.Receipts.Add(new UninvoicedReceiptRow(Guid.NewGuid(), Guid.NewGuid(), "RC-26-0009", Thai(2026, 11, 4),
            "JB2611040105001", "สมชาย", 500m, 35m, 535m));
        f.Repo.Sales.Add(new VatSaleRow(Guid.NewGuid(), "SL-26-0001", Thai(2026, 11, 2), null, 200m, 14m, 214m));

        var r = (await f.Service.OutputVatAsync(2026, 11)).Data!;

        r.Rows.Select(x => (x.Sequence, x.DocumentNo)).Should().Equal(
            (1, "IV-26-0001"), (2, "IV-26-0002"), (3, "CN-26-0001"));
        r.Rows[2].Should().Match<OutputVatRowDto>(x =>
            x.Kind == "creditNote" && x.NetAmount == -100m && x.VatAmount == -7m && x.TotalAmount == -107m
            && x.ReferenceDocumentNo == "IV-26-0001");
        (r.NetAmount, r.VatAmount, r.TotalAmount).Should().Be((2900m, 203m, 3103m));

        r.Uninvoiced.Select(x => (x.Kind, x.DocumentNo)).Should().Equal(
            ("sale", "SL-26-0001"), ("receipt", "RC-26-0009"), ("creditNote", "CN-26-0002"));
        r.Uninvoiced[0].BuyerName.Should().Be("ลูกค้าทั่วไป");
        r.Uninvoiced[1].ReferenceDocumentNo.Should().Be("JB2611040105001");
        (r.UninvoicedNetAmount, r.UninvoicedVatAmount).Should().Be((650m, 45.5m));

        r.Seller!.TaxId.Should().Be(T.SellerTaxId);
        r.Seller.BranchNo.Should().Be("00000");
    }

    [Fact]
    public async Task Output_vat_uses_the_thai_calendar_month()
    {
        var f = new Fixture();
        // 1 พ.ย. 00:30 เวลาไทย = 31 ต.ค. UTC — ต้องอยู่เดือน พ.ย.
        f.Repo.Invoices.Add(Invoice("IV-26-0005", Thai(2026, 11, 1, 0).AddMinutes(30), 100m, 7m));
        // 31 ต.ค. 23:30 เวลาไทย — ต้องอยู่เดือน ต.ค.
        f.Repo.Invoices.Add(Invoice("IV-26-0004", Thai(2026, 10, 31, 23).AddMinutes(30), 100m, 7m));

        (await f.Service.OutputVatAsync(2026, 11)).Data!.Rows.Select(x => x.DocumentNo).Should().Equal("IV-26-0005");
        (await f.Service.OutputVatAsync(2026, 10)).Data!.Rows.Select(x => x.DocumentNo).Should().Equal("IV-26-0004");
    }

    [Fact]
    public async Task Output_vat_defaults_to_this_month_and_rejects_bad_months()
    {
        var f = new Fixture();
        var r = (await f.Service.OutputVatAsync(null, null)).Data!;
        (r.Year, r.Month).Should().Be((2026, 11));
        (await f.Service.OutputVatAsync(2026, 13)).Error!.Code.Should().Be("TAX_REPORT_VALIDATION");
    }

    [Theory]
    [InlineData(UserRole.Cashier)]
    [InlineData(UserRole.FrontDesk)]
    [InlineData(UserRole.Technician)]
    public async Task Only_managers_and_office_see_tax_reports(UserRole role)
    {
        var f = new Fixture(role);
        (await f.Service.OutputVatAsync(2026, 11)).Error!.Code.Should().Be("TAX_REPORT_FORBIDDEN");
        (await f.Service.InputVatAsync(2026, 11)).Error!.Code.Should().Be("TAX_REPORT_FORBIDDEN");
        (await f.Record(Request())).Error!.Code.Should().Be("TAX_REPORT_FORBIDDEN");
    }

    [Fact]
    public async Task Office_can_use_tax_reports()
    {
        var f = new Fixture(UserRole.Office);
        (await f.Service.OutputVatAsync(2026, 11)).Success.Should().BeTrue();
        (await f.Record(Request())).Success.Should().BeTrue();
    }

    // ---------- ใบกำกับภาษีซื้อ ----------

    [Fact]
    public async Task Recording_a_purchase_invoice_puts_it_in_the_claim_month_report()
    {
        var f = new Fixture();
        var saved = (await f.Record(Request(claimMonth: 11, invoiceDate: new DateOnly(2026, 10, 20)))).Data!;
        saved.Should().Match<PurchaseTaxInvoiceDto>(x =>
            x.ClaimYear == 2026 && x.ClaimMonth == 11 && x.TotalAmount == 1070m && x.SupplierBranchNo == "00000"
            && x.RecordedByName == "แคชเชียร์ ทดสอบ" && !x.IsVoided);

        (await f.Service.InputVatAsync(2026, 10)).Data!.Rows.Should().BeEmpty();
        var nov = (await f.Service.InputVatAsync(2026, 11)).Data!;
        nov.Rows.Should().ContainSingle(x => x.InvoiceNo == "INV-001");
        (nov.NetAmount, nov.VatAmount, nov.TotalAmount).Should().Be((1000m, 70m, 1070m));
        f.Repo.Events.Should().ContainSingle(e => e.EventType == "purchase_tax_invoice.recorded");
    }

    [Fact]
    public async Task Claim_month_defaults_to_the_invoice_month_and_must_be_within_six_months()
    {
        var f = new Fixture();
        var date = new DateOnly(2026, 5, 31);
        (await f.Record(Request(invoiceDate: date, claimYear: null, claimMonth: null))).Data!.ClaimMonth.Should().Be(5);

        (await f.Record(Request("INV-2", invoiceDate: date, claimMonth: 4))).Error!.Field.Should().Be("claimMonth");
        (await f.Record(Request("INV-3", invoiceDate: date, claimMonth: 11))).Success.Should().BeTrue();   // +6 เดือน
        var late = await f.Record(Request("INV-4", invoiceDate: date, claimMonth: 12));                 // +7 เดือน
        late.Error!.Code.Should().Be("PURCHASE_TAX_INVOICE_VALIDATION");
        late.Error.MessageTh.Should().Contain("6 เดือน");
    }

    [Fact]
    public async Task Claim_window_crosses_the_year()
    {
        var f = new Fixture(now: new DateTime(2027, 6, 1, 3, 0, 0, DateTimeKind.Utc));
        (await f.Record(Request(invoiceDate: new DateOnly(2026, 9, 15), claimYear: 2027, claimMonth: 3))).Success.Should().BeTrue();
        (await f.Record(Request("X2", invoiceDate: new DateOnly(2026, 9, 15), claimYear: 2027, claimMonth: 4)))
            .Error!.Field.Should().Be("claimMonth");
    }

    [Fact]
    public async Task Invoice_fields_are_validated()
    {
        var f = new Fixture();
        (await f.Record(Request() with { SupplierTaxId = "12345" })).Error!.Field.Should().Be("supplierTaxId");
        (await f.Record(Request() with { SupplierName = " " })).Error!.Field.Should().Be("supplierName");
        (await f.Record(Request() with { SupplierBranchNo = "12" })).Error!.Field.Should().Be("supplierBranchNo");
        (await f.Record(Request() with { InvoiceNo = "" })).Error!.Field.Should().Be("invoiceNo");
        (await f.Record(Request() with { InvoiceDate = null })).Error!.Field.Should().Be("invoiceDate");
        (await f.Record(Request(invoiceDate: new DateOnly(2026, 11, 10)))).Error!.Field.Should().Be("invoiceDate");   // อนาคต
        (await f.Record(Request() with { NetAmount = 0m })).Error!.Field.Should().Be("netAmount");
        (await f.Record(Request() with { VatAmount = 0m })).Error!.Field.Should().Be("vatAmount");
        (await f.Record(Request() with { VatAmount = 1000m })).Error!.Field.Should().Be("vatAmount");
        (await f.Record(Request() with { VatAmount = 70.005m })).Error!.Field.Should().Be("vatAmount");
        f.Repo.Purchases.Should().BeEmpty();
    }

    [Fact]
    public async Task Same_supplier_invoice_number_cannot_be_recorded_twice_until_voided()
    {
        var f = new Fixture();
        var first = (await f.Record(Request())).Data!;

        var dup = await f.Record(Request() with { SupplierTaxId = "0-1055-60000-00-1" });   // ขีดคั่นยังนับว่าเลขเดียวกัน
        dup.Error!.Code.Should().Be("PURCHASE_TAX_INVOICE_DUPLICATE");
        dup.Error.MessageTh.Should().Contain("พฤศจิกายน 2569");

        // ผู้ขายอื่นใช้เลขเดียวกันได้
        (await f.Record(Request() with { SupplierTaxId = "0105560000002" })).Success.Should().BeTrue();

        (await f.Service.VoidPurchaseInvoiceAsync(first.Id, new VoidPurchaseTaxInvoiceRequest("พิมพ์ยอดผิด"))).Data!
            .Should().Match<PurchaseTaxInvoiceDto>(x => x.IsVoided && x.VoidReason == "พิมพ์ยอดผิด");
        (await f.Record(Request())).Success.Should().BeTrue();

        var report = (await f.Service.InputVatAsync(2026, 11)).Data!;
        report.Rows.Should().HaveCount(2);
        report.Voided.Should().ContainSingle(x => x.Id == first.Id);
    }

    [Fact]
    public async Task Request_id_replay_returns_the_same_invoice_and_reuse_with_other_data_is_rejected()
    {
        var f = new Fixture();
        var req = Request();
        var a = (await f.Service.RecordPurchaseInvoiceAsync(req)).Data!;
        var b = (await f.Service.RecordPurchaseInvoiceAsync(req)).Data!;
        b.Id.Should().Be(a.Id);
        f.Repo.Purchases.Should().HaveCount(1);

        (await f.Service.RecordPurchaseInvoiceAsync(req with { NetAmount = 2000m, VatAmount = 140m }))
            .Error!.Code.Should().Be("PURCHASE_TAX_INVOICE_CONFLICT");
    }

    [Fact]
    public async Task Void_needs_a_reason_and_happens_once()
    {
        var f = new Fixture();
        var saved = (await f.Record(Request())).Data!;
        (await f.Service.VoidPurchaseInvoiceAsync(saved.Id, new VoidPurchaseTaxInvoiceRequest(" "))).Error!.Field.Should().Be("reason");
        (await f.Service.VoidPurchaseInvoiceAsync(saved.Id, new VoidPurchaseTaxInvoiceRequest("ซ้ำ"))).Success.Should().BeTrue();
        (await f.Service.VoidPurchaseInvoiceAsync(saved.Id, new VoidPurchaseTaxInvoiceRequest("ซ้ำ"))).Error!.Code
            .Should().Be("PURCHASE_TAX_INVOICE_VOIDED");
        (await f.Service.VoidPurchaseInvoiceAsync(Guid.NewGuid(), new VoidPurchaseTaxInvoiceRequest("x"))).Error!.Code
            .Should().Be("PURCHASE_TAX_INVOICE_NOT_FOUND");
    }

    [Fact]
    public async Task Supplier_fills_name_and_tax_id_and_must_belong_to_the_branch()
    {
        var f = new Fixture();
        var supplier = new Supplier { Name = "ร้านอะไหล่ดี", TaxId = "0105560000001", LegacyShardKey = "db2", LegacyBranchId = 105 };
        f.Repo.Suppliers.Add(supplier);

        var saved = (await f.Record(Request() with { SupplierId = supplier.Id, SupplierName = null, SupplierTaxId = null })).Data!;
        (saved.SupplierId, saved.SupplierName, saved.SupplierTaxId).Should().Be((supplier.Id, "ร้านอะไหล่ดี", "0105560000001"));

        (await f.Record(Request("X2") with { SupplierId = Guid.NewGuid() })).Error!.Code.Should().Be("SUPPLIER_NOT_FOUND");
    }

    [Fact]
    public async Task Goods_receipt_prefill_computes_vat_once_and_links_the_po()
    {
        var f = new Fixture();
        var supplier = new Supplier { Name = "ร้านอะไหล่ดี", TaxId = "0105560000001", LegacyShardKey = "db2", LegacyBranchId = 105 };
        f.Repo.Suppliers.Add(supplier);
        var grn = GoodsReceipt("GRN-1", Thai(2026, 11, 2), supplier.Id, hasVat: true, net: 333.33m);
        f.Repo.GoodsReceipts.Add(grn);

        var p = (await f.Service.PrefillFromGoodsReceiptAsync(grn.GoodsReceiptId)).Data!;
        (p.NetAmount, p.VatAmount).Should().Be((333.33m, 23.33m));
        (p.SupplierName, p.SupplierTaxId, p.PurchaseOrderNumber).Should().Be(("ร้านอะไหล่ดี", "0105560000001", "PO-1"));
        p.ReceivedDate.Should().Be(new DateOnly(2026, 11, 2));

        var saved = (await f.Record(Request() with { GoodsReceiptId = grn.GoodsReceiptId })).Data!;
        (saved.GoodsReceiptNumber, saved.PurchaseOrderNumber).Should().Be(("GRN-1", "PO-1"));

        (await f.Service.PrefillFromGoodsReceiptAsync(Guid.NewGuid())).Error!.Code.Should().Be("GOODS_RECEIPT_NOT_FOUND");
    }

    [Fact]
    public async Task Report_lists_vat_goods_receipts_still_missing_an_invoice_from_the_last_six_months()
    {
        var f = new Fixture();
        f.Repo.GoodsReceipts.Add(GoodsReceipt("GRN-OLD", Thai(2026, 4, 30), null, true, 100m));     // ก่อนหน้าต่าง 6 เดือน
        f.Repo.GoodsReceipts.Add(GoodsReceipt("GRN-MAY", Thai(2026, 5, 1), null, true, 100m));
        f.Repo.GoodsReceipts.Add(GoodsReceipt("GRN-NOVAT", Thai(2026, 11, 1), null, false, 100m));
        var linked = GoodsReceipt("GRN-LINKED", Thai(2026, 11, 2), null, true, 100m);
        f.Repo.GoodsReceipts.Add(linked);
        await f.Record(Request() with { GoodsReceiptId = linked.GoodsReceiptId });

        var pending = (await f.Service.InputVatAsync(2026, 11)).Data!.PendingGoodsReceipts;
        pending.Select(x => x.Number).Should().Equal("GRN-MAY");
        pending[0].VatAmount.Should().Be(7m);
    }

    // ---------- helper ----------

    private static RecordPurchaseTaxInvoiceRequest Request(
        string invoiceNo = "INV-001", DateOnly? invoiceDate = null, int? claimYear = 2026, int? claimMonth = 11) =>
        new(Guid.NewGuid(), null, "ร้านอะไหล่ดี", "0105560000001", null, invoiceNo,
            invoiceDate ?? new DateOnly(2026, 11, 3), claimYear, claimMonth, 1000m, 70m, null, null);

    private static TaxInvoice Invoice(string no, DateTime at, decimal net, decimal vat) => new()
    {
        DocumentNo = no, IssuedAt = at, BuyerName = "ผู้ซื้อ", LegacyShardKey = "db2", LegacyBranchId = 105,
        NetAmount = net, VatAmount = vat, TotalAmount = net + vat, VatRate = 0.07m
    };

    private static CreditNote Credit(string no, DateTime at, CreditNoteSourceType type, string source, decimal net, decimal vat) => new()
    {
        DocumentNo = no, IssuedAt = at, SourceType = type, SourceDocumentNo = source, BuyerName = "ผู้ซื้อ",
        LegacyShardKey = "db2", LegacyBranchId = 105, DiffNetAmount = net, VatAmount = vat, TotalAmount = net + vat
    };

    private static GoodsReceiptVatRow GoodsReceipt(string no, DateTime at, Guid? supplierId, bool hasVat, decimal net) =>
        new(Guid.NewGuid(), no, at, "DO-1", Guid.NewGuid(), "PO-1", supplierId, "ร้านอะไหล่ดี", hasVat, 0.07m, net);

    private sealed class Fixture
    {
        public FakeRepo Repo { get; } = new();
        public TaxReportService Service { get; }

        public Fixture(UserRole role = UserRole.Manager, DateTime? now = null)
        {
            Service = new TaxReportService(Repo, new CreditNoteServiceTests.FakeCreditNoteRepository(),
                new T.FakeLegacyReader(T.SellerTaxId), new T.StubCurrentUser(role), new PackageTestClock(now ?? NowUtc));
        }

        public Task<Application.Common.Result<PurchaseTaxInvoiceDto>> Record(RecordPurchaseTaxInvoiceRequest r) =>
            Service.RecordPurchaseInvoiceAsync(r);
    }

    private sealed class FakeRepo : ITaxReportRepository
    {
        public List<TaxInvoice> Invoices { get; } = [];
        public List<CreditNote> Notes { get; } = [];
        public List<UninvoicedReceiptRow> Receipts { get; } = [];
        public List<VatSaleRow> Sales { get; } = [];
        public List<PurchaseTaxInvoice> Purchases { get; } = [];
        public List<Supplier> Suppliers { get; } = [];
        public List<GoodsReceiptVatRow> GoodsReceipts { get; } = [];
        public List<ActivityEvent> Events { get; } = [];

        private static bool In(DateTime at, DateTime from, DateTime to) => at >= from && at < to;

        public Task<IReadOnlyList<TaxInvoice>> TaxInvoicesAsync(string s, int b, DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TaxInvoice>>(Invoices.Where(x => In(x.IssuedAt, from, to)).ToList());
        public Task<IReadOnlyList<CreditNote>> CreditNotesAsync(string s, int b, DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<CreditNote>>(Notes.Where(x => In(x.IssuedAt, from, to)).ToList());
        public Task<IReadOnlyList<UninvoicedReceiptRow>> UninvoicedVatReceiptsAsync(string s, int b, DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<UninvoicedReceiptRow>>(Receipts.Where(x => In(x.IssuedAt, from, to)).ToList());
        public Task<IReadOnlyList<VatSaleRow>> VatSalesAsync(string s, int b, DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VatSaleRow>>(Sales.Where(x => In(x.IssuedAt, from, to)).ToList());

        public Task<IReadOnlyList<PurchaseTaxInvoice>> PurchaseInvoicesAsync(string s, int b, int period, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PurchaseTaxInvoice>>(Purchases.Where(x => x.ClaimPeriod == period).ToList());
        public Task<PurchaseTaxInvoice?> GetPurchaseInvoiceAsync(string s, int b, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Purchases.FirstOrDefault(x => x.Id == id));
        public Task<PurchaseTaxInvoice?> GetPurchaseInvoiceByRequestIdAsync(Guid requestId, CancellationToken ct = default) =>
            Task.FromResult(Purchases.FirstOrDefault(x => x.RequestId == requestId));
        public Task<PurchaseTaxInvoice?> FindActivePurchaseInvoiceAsync(string s, int b, string taxId, string no, CancellationToken ct = default) =>
            Task.FromResult(Purchases.FirstOrDefault(x => x.SupplierTaxId == taxId && x.InvoiceNo == no && x.VoidedAt == null));
        public Task<Supplier?> GetSupplierAsync(string s, int b, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Suppliers.FirstOrDefault(x => x.Id == id && x.LegacyShardKey == s && x.LegacyBranchId == b));
        public Task<GoodsReceiptVatRow?> GetGoodsReceiptAsync(string s, int b, Guid id, CancellationToken ct = default) =>
            Task.FromResult(GoodsReceipts.FirstOrDefault(x => x.GoodsReceiptId == id));
        public Task<IReadOnlyList<GoodsReceiptVatRow>> VatGoodsReceiptsWithoutInvoiceAsync(string s, int b, DateTime from, DateTime to, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GoodsReceiptVatRow>>(GoodsReceipts
                .Where(x => x.HasVat && In(x.ReceivedAt, from, to)
                            && !Purchases.Any(p => p.GoodsReceiptId == x.GoodsReceiptId && p.VoidedAt == null))
                .ToList());

        public Task AddPurchaseInvoiceAsync(PurchaseTaxInvoice invoice, CancellationToken ct = default)
        {
            Purchases.Add(invoice);
            return Task.CompletedTask;
        }
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default)
        {
            Events.Add(evt);
            return Task.CompletedTask;
        }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }
}
