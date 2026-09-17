using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ครอบคลุมรายการนอกแคตตาล็อก (ad-hoc line) — docs/07-quotation-adhoc-line.md
/// </summary>
public sealed class QuotationServiceTests
{
    [Fact]
    public async Task AdHoc_line_is_added_with_minimum_required_fields()
    {
        var (service, repo, quotation) = Build(UserRole.Manager);

        var result = await service.AddLineAsync(quotation.Id, AdHocRequest(name: "ถอดล้างเทอร์โบ", price: 1500m));

        Assert.True(result.Success);
        var line = quotation.Lines.Single();
        Assert.Equal(string.Empty, line.CatalogCode);
        Assert.Equal("ถอดล้างเทอร์โบ", line.Name);
        Assert.Equal(LineType.Labor, line.Type);
        Assert.Equal(1500m, line.UnitPrice);
        Assert.Equal(1m, line.Quantity);
        Assert.Equal("งาน", line.Unit); // default หน่วยของค่าแรง
        var dtoLine = result.Data!.Lines.Single();
        Assert.True(dtoLine.IsAdHoc);
        Assert.Single(repo.SavedEvents);
    }

    [Fact]
    public async Task AdHoc_part_line_defaults_to_piece_unit()
    {
        var (service, _, quotation) = Build(UserRole.Manager);

        var result = await service.AddLineAsync(quotation.Id,
            AdHocRequest(name: "ปะเก็นเฉพาะกิจ", price: 200m, type: LineType.Part));

        Assert.True(result.Success);
        Assert.Equal("ชิ้น", quotation.Lines.Single().Unit);
    }

    [Theory]
    [InlineData(null, "ค่าแรง", 100.0, "QUOTE_LINE_NAME_REQUIRED")]
    [InlineData("   ", "ค่าแรง", 100.0, "QUOTE_LINE_NAME_REQUIRED")]
    public async Task AdHoc_line_requires_name(string? name, string _, double price, string expectedCode)
    {
        var (service, _, quotation) = Build(UserRole.Manager);

        var result = await service.AddLineAsync(quotation.Id, AdHocRequest(name: name, price: (decimal)price));

        Assert.False(result.Success);
        Assert.Equal(expectedCode, result.Error?.Code);
    }

    [Fact]
    public async Task AdHoc_line_requires_type()
    {
        var (service, _, quotation) = Build(UserRole.Manager);

        var request = new UpsertLineRequest(
            CatalogCode: "", Quantity: 1, UnitPrice: 500m, DiscountPercent: 0,
            Promotion: PromotionKind.None, Source: LineSource.Customer, AssignedTechnicianId: null,
            Note: null, Name: "ของซื้อนอก", Type: null);

        var result = await service.AddLineAsync(quotation.Id, request);

        Assert.False(result.Success);
        Assert.Equal("QUOTE_LINE_TYPE_REQUIRED", result.Error?.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-10.0)]
    public async Task AdHoc_line_requires_positive_price(double? price)
    {
        var (service, _, quotation) = Build(UserRole.Manager);

        var result = await service.AddLineAsync(quotation.Id,
            AdHocRequest(name: "ของซื้อนอก", price: price is null ? null : (decimal)price.Value));

        Assert.False(result.Success);
        Assert.Equal("QUOTE_LINE_PRICE_REQUIRED", result.Error?.Code);
    }

    [Fact]
    public async Task AdHoc_line_cost_is_stripped_for_non_manager_even_if_client_sends_it()
    {
        var (service, _, quotation) = Build(UserRole.Office); // ไม่มี CanSeeCost

        var result = await service.AddLineAsync(quotation.Id,
            AdHocRequest(name: "ของซื้อนอก", price: 500m, cost: 300m));

        Assert.True(result.Success);
        Assert.Equal(0m, quotation.Lines.Single().UnitCost); // [BIZ] กฎข้อ 7 — server ควบคุมต้นทุน
    }

    [Fact]
    public async Task AdHoc_line_cost_is_kept_for_manager()
    {
        var (service, _, quotation) = Build(UserRole.Manager);

        var result = await service.AddLineAsync(quotation.Id,
            AdHocRequest(name: "ของซื้อนอก", price: 500m, cost: 300m));

        Assert.True(result.Success);
        Assert.Equal(300m, quotation.Lines.Single().UnitCost);
    }

    [Fact]
    public async Task Update_adhoc_line_can_change_name_unit_and_cost()
    {
        var (service, _, quotation) = Build(UserRole.Manager);
        await service.AddLineAsync(quotation.Id, AdHocRequest(name: "ชื่อเดิม", price: 500m, cost: 100m));
        var line = quotation.Lines.Single();

        var update = new UpsertLineRequest(
            CatalogCode: "", Quantity: 2, UnitPrice: 600m, DiscountPercent: 0,
            Promotion: PromotionKind.None, Source: LineSource.Customer, AssignedTechnicianId: null,
            Note: null, Name: "ชื่อใหม่", Type: LineType.Labor, Unit: "ครั้ง", UnitCost: 150m);

        var result = await service.UpdateLineAsync(quotation.Id, line.Id, update);

        Assert.True(result.Success);
        Assert.Equal("ชื่อใหม่", line.Name);
        Assert.Equal("ครั้ง", line.Unit);
        Assert.Equal(150m, line.UnitCost);
        Assert.Equal(600m, line.UnitPrice);
        Assert.Equal(2m, line.Quantity);
    }

    [Fact]
    public async Task Update_catalog_line_ignores_name_sent_by_client()
    {
        var (service, _, quotation) = Build(UserRole.Manager);
        await service.AddLineAsync(quotation.Id, CatalogRequest("P-001"));
        var line = quotation.Lines.Single();
        var originalName = line.Name;

        var update = new UpsertLineRequest(
            CatalogCode: "P-001", Quantity: 3, UnitPrice: null, DiscountPercent: 0,
            Promotion: PromotionKind.None, Source: LineSource.Customer, AssignedTechnicianId: null,
            Note: null, Name: "พยายามเปลี่ยนชื่อ");

        var result = await service.UpdateLineAsync(quotation.Id, line.Id, update);

        Assert.True(result.Success);
        Assert.Equal(originalName, line.Name); // บรรทัดจากแคตตาล็อกยังล็อกชื่อไว้เหมือนเดิม
        Assert.Equal(3m, line.Quantity);
    }

    [Fact]
    public void ValidateForSend_allows_multiple_adhoc_lines_with_different_names()
    {
        var quotation = QuotationWithLines(
            AdHocLine("รายการเอ", LineType.Labor, technicianId: 1),
            AdHocLine("รายการบี", LineType.Labor, technicianId: 1));
        QuotationCalculator.ApplyQuotationTotals(quotation);

        var result = QuotationValidator.ValidateForSend(quotation, UserRole.Office);

        Assert.DoesNotContain(result.Errors, e => e.Code == "QUOTE_DUPLICATE_LINE");
    }

    [Fact]
    public void ValidateForSend_flags_adhoc_lines_with_same_name_as_duplicate()
    {
        var quotation = QuotationWithLines(
            AdHocLine("รายการเอ", LineType.Labor, technicianId: 1),
            AdHocLine("รายการเอ", LineType.Labor, technicianId: 1));
        QuotationCalculator.ApplyQuotationTotals(quotation);

        var result = QuotationValidator.ValidateForSend(quotation, UserRole.Office);

        Assert.Contains(result.Errors, e => e.Code == "QUOTE_DUPLICATE_LINE");
    }

    [Fact]
    public void ValidateForSend_still_requires_technician_for_adhoc_labor_line()
    {
        var quotation = QuotationWithLines(AdHocLine("ค่าแรงเฉพาะกิจ", LineType.Labor, technicianId: null));
        QuotationCalculator.ApplyQuotationTotals(quotation);

        var result = QuotationValidator.ValidateForSend(quotation, UserRole.Office);

        Assert.Contains(result.Errors, e => e.Code == "QUOTE_LINE_NO_TECHNICIAN");
    }

    [Fact]
    public async Task ReviseAsync_copies_adhoc_line_fields_into_new_version()
    {
        var (service, repo, quotation) = Build(UserRole.Manager);
        await service.AddLineAsync(quotation.Id, AdHocRequest(name: "ของซื้อนอก", price: 500m, cost: 100m));
        quotation.Status = QuotationStatus.Sent; // ต้องไม่ใช่ Draft ถึงจะออกฉบับแก้ไขได้

        var result = await service.ReviseAsync(quotation.Id, new ReviseQuotationRequest("ลูกค้าขอเพิ่มรายการ"));

        Assert.True(result.Success);
        var revision = repo.AddedQuotations.Single();
        var copiedLine = revision.Lines.Single();
        Assert.Equal(string.Empty, copiedLine.CatalogCode);
        Assert.Equal("ของซื้อนอก", copiedLine.Name);
        Assert.Equal("งาน", copiedLine.Unit);
        Assert.Equal(100m, copiedLine.UnitCost);
        Assert.Equal(LineApprovalStatus.Pending, copiedLine.ApprovalStatus); // [BIZ] กลับเป็น pending ทุกบรรทัด
    }

    // ---------- ApplyTemplateAsync (docs/08-quotation-template.md) ----------

    [Fact]
    public async Task ApplyTemplate_appends_catalog_and_adhoc_lines_in_a_single_event()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var (service, repo, quotation) = Build(UserRole.Manager, templateRepo);
        var template = SeedTemplate(templateRepo, [
            TemplateCatalogLine("OIL-5W30", qty: 4),
            TemplateAdHocLine("ค่าแรงเปลี่ยนถ่าย", LineType.Labor, 350m),
        ]);

        var result = await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(template.Id, null));

        Assert.True(result.Success);
        Assert.Equal(2, quotation.Lines.Count);
        Assert.Single(repo.SavedEvents.Where(e => e.EventType == "quotation.lines.from_template"));
    }

    [Fact]
    public async Task ApplyTemplate_catalog_line_uses_live_catalog_price_and_name_even_when_template_cache_is_stale()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var catalogRepo = new FakeCatalogRepository();
        catalogRepo.Overrides["OIL-5W30"] = new CatalogItem
        {
            Code = "OIL-5W30", Name = "น้ำมันเครื่องสังเคราะห์แท้ (ชื่อใหม่)", Type = LineType.Part, Unit = "ลิตร",
            Price = 399m, Cost = 200m,
        };
        var (service, _, quotation, _) = BuildFull(UserRole.Manager, templateRepo, catalogRepo);
        var template = SeedTemplate(templateRepo, [TemplateCatalogLine("OIL-5W30", name: "ชื่อเก่าในเทมเพลต")]);

        var result = await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(template.Id, null));

        Assert.True(result.Success);
        var line = quotation.Lines.Single();
        Assert.Equal("น้ำมันเครื่องสังเคราะห์แท้ (ชื่อใหม่)", line.Name);
        Assert.Equal(399m, line.UnitPrice);
    }

    [Fact]
    public async Task ApplyTemplate_catalog_line_respects_template_price_override()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var (service, _, quotation) = Build(UserRole.Manager, templateRepo);
        var template = SeedTemplate(templateRepo, [TemplateCatalogLine("OIL-5W30", unitPrice: 1500m)]);

        var result = await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(template.Id, null));

        Assert.True(result.Success);
        Assert.Equal(1500m, quotation.Lines.Single().UnitPrice);
    }

    [Fact]
    public async Task ApplyTemplate_by_a_role_without_cost_visibility_keeps_real_cost_in_storage_but_hides_it_in_response()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var (service, _, quotation) = Build(UserRole.Office, templateRepo);
        var template = SeedTemplate(templateRepo, [
            TemplateAdHocLine("ค่าแรงเฉพาะกิจ", LineType.Labor, 500m, cost: 300m),
        ]);

        var result = await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(template.Id, null));

        Assert.True(result.Success);
        Assert.Equal(300m, quotation.Lines.Single().UnitCost); // ค่าจริงในฐานข้อมูล
        Assert.Null(result.Data!.Lines.Single().UnitCost);     // ถูก strip ที่ mapper ตาม role
    }

    [Fact]
    public async Task ApplyTemplate_fails_and_adds_nothing_when_a_catalog_code_is_missing()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var catalogRepo = new FakeCatalogRepository();
        catalogRepo.MissingCodes.Add("DISCONTINUED-01");
        var (service, _, quotation, _) = BuildFull(UserRole.Manager, templateRepo, catalogRepo);
        var template = SeedTemplate(templateRepo, [
            TemplateCatalogLine("OIL-5W30"), TemplateCatalogLine("DISCONTINUED-01"),
        ]);

        var result = await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(template.Id, null));

        Assert.False(result.Success);
        Assert.Equal("QUOTE_TEMPLATE_ITEM_MISSING", result.Error?.Code);
        Assert.Empty(quotation.Lines); // all-or-nothing — ไม่เพิ่มสักบรรทัด
    }

    [Fact]
    public async Task ApplyTemplate_fails_and_adds_nothing_when_a_line_collides_with_an_existing_line()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var (service, _, quotation) = Build(UserRole.Manager, templateRepo);
        await service.AddLineAsync(quotation.Id,
            new UpsertLineRequest("OIL-5W30", 1, null, 0, PromotionKind.None, LineSource.Customer, null, null));
        var template = SeedTemplate(templateRepo, [TemplateCatalogLine("OIL-5W30")]);

        var result = await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(template.Id, null));

        Assert.False(result.Success);
        Assert.Equal("QUOTE_TEMPLATE_DUPLICATE_LINE", result.Error?.Code);
        Assert.Single(quotation.Lines); // ยังเหลือแค่บรรทัดเดิม ไม่เพิ่มซ้ำ
    }

    [Fact]
    public async Task ApplyTemplate_fails_when_quotation_is_not_editable()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var (service, _, quotation) = Build(UserRole.Manager, templateRepo);
        quotation.Status = QuotationStatus.Sent;
        var template = SeedTemplate(templateRepo, [TemplateCatalogLine("OIL-5W30")]);

        var result = await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(template.Id, null));

        Assert.False(result.Success);
        Assert.Equal("QUOTE_NOT_EDITABLE", result.Error?.Code);
    }

    [Fact]
    public async Task ApplyTemplate_fails_when_template_is_inactive_or_empty()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var (service, _, quotation) = Build(UserRole.Manager, templateRepo);
        var inactive = SeedTemplate(templateRepo, [TemplateCatalogLine("OIL-5W30")]);
        inactive.IsActive = false;
        var empty = SeedTemplate(templateRepo, []);

        var inactiveResult = await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(inactive.Id, null));
        var emptyResult = await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(empty.Id, null));

        Assert.Equal("QUOTE_TEMPLATE_INACTIVE", inactiveResult.Error?.Code);
        Assert.Equal("QUOTE_TEMPLATE_EMPTY", emptyResult.Error?.Code);
    }

    [Fact]
    public async Task ApplyTemplate_leaves_technician_unassigned_so_ValidateForSend_still_blocks_sending()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var (service, _, quotation) = Build(UserRole.Manager, templateRepo);
        var template = SeedTemplate(templateRepo, [TemplateAdHocLine("ค่าแรงเฉพาะกิจ", LineType.Labor, 500m)]);

        await service.ApplyTemplateAsync(quotation.Id, new ApplyTemplateRequest(template.Id, null));
        QuotationCalculator.ApplyQuotationTotals(quotation);
        var validation = QuotationValidator.ValidateForSend(quotation, UserRole.Manager);

        Assert.Null(quotation.Lines.Single().AssignedTechnicianId);
        Assert.Contains(validation.Errors, e => e.Code == "QUOTE_LINE_NO_TECHNICIAN");
    }

    [Fact]
    public async Task ApplyTemplate_source_override_applies_to_every_line_when_provided()
    {
        var templateRepo = new FakeQuotationTemplateRepository();
        var (service, _, quotation) = Build(UserRole.Manager, templateRepo);
        var template = SeedTemplate(templateRepo, [
            TemplateCatalogLine("OIL-5W30", source: LineSource.Customer),
            TemplateAdHocLine("ค่าแรงเฉพาะกิจ", LineType.Labor, 500m, source: LineSource.Technician),
        ]);

        var result = await service.ApplyTemplateAsync(
            quotation.Id, new ApplyTemplateRequest(template.Id, LineSource.Technician));

        Assert.True(result.Success);
        Assert.All(quotation.Lines, l => Assert.Equal(LineSource.Technician, l.Source));
    }

    // ---------- helpers (เทมเพลต) ----------

    private static QuotationTemplateLine TemplateCatalogLine(
        string code, decimal qty = 1, decimal? unitPrice = null, string? name = null, LineSource source = LineSource.Customer) => new()
    {
        Id = Guid.NewGuid(), CatalogCode = code, Name = name ?? code, Type = LineType.Part, Unit = "ชิ้น",
        Quantity = qty, UnitPrice = unitPrice, Source = source,
    };

    private static QuotationTemplateLine TemplateAdHocLine(
        string name, LineType type, decimal price, decimal? cost = null, LineSource source = LineSource.Customer) => new()
    {
        Id = Guid.NewGuid(), CatalogCode = "", Name = name, Type = type, Unit = type == LineType.Labor ? "งาน" : "ชิ้น",
        Quantity = 1, UnitPrice = price, UnitCost = cost, Source = source,
    };

    private static QuotationTemplate SeedTemplate(
        FakeQuotationTemplateRepository repo, IReadOnlyList<QuotationTemplateLine> lines)
    {
        var template = new QuotationTemplate
        {
            Code = $"TPL-{Guid.NewGuid():N}"[..8], Name = "เทมเพลตทดสอบ",
            LegacyShardKey = "db2", LegacyBranchId = 105, IsActive = true, Lines = lines.ToList(),
        };
        var sequence = 1;
        foreach (var line in template.Lines) { line.QuotationTemplateId = template.Id; line.Sequence = sequence++; }
        repo.Seed(template);
        return template;
    }

    // ---------- helpers ----------

    private static (QuotationService Service, FakeQuotationRepository Repo, Quotation Quotation) Build(UserRole role) =>
        Build(role, new FakeQuotationTemplateRepository());

    private static (QuotationService Service, FakeQuotationRepository Repo, Quotation Quotation) Build(
        UserRole role, FakeQuotationTemplateRepository templateRepo)
    {
        var (service, repo, quotation, _) = BuildFull(role, templateRepo, new FakeCatalogRepository());
        return (service, repo, quotation);
    }

    private static (
        QuotationService Service, FakeQuotationRepository Repo, Quotation Quotation, FakeCatalogRepository Catalog)
        BuildFull(UserRole role, FakeQuotationTemplateRepository templateRepo, FakeCatalogRepository catalogRepo)
    {
        var job = new Job
        {
            Id = Guid.NewGuid(), LegacyShardKey = "db2", BranchId = 105,
            JobNo = "JB250915001", CustomerName = "ลูกค้าทดสอบ", VehicleRegistration = "กก-1234",
        };
        var quotation = new Quotation
        {
            Id = Guid.NewGuid(), JobId = job.Id, Job = job, JobNo = job.JobNo,
            Status = QuotationStatus.Draft, Code = "QT-250915001-01", Version = 1,
            CustomerName = job.CustomerName, VehicleRegistration = job.VehicleRegistration,
        };
        var repo = new FakeQuotationRepository(quotation);
        var service = new QuotationService(
            repo, catalogRepo, new FakeJobRepository(), templateRepo, new FakeLegacyReader(),
            new StubCurrentUser(role), TimeProvider.System);
        return (service, repo, quotation, catalogRepo);
    }

    private static UpsertLineRequest AdHocRequest(
        string? name, decimal? price, LineType type = LineType.Labor, decimal? cost = null) => new(
        CatalogCode: "", Quantity: 1, UnitPrice: price, DiscountPercent: 0,
        Promotion: PromotionKind.None, Source: LineSource.Customer, AssignedTechnicianId: null,
        Note: null, Name: name, Type: type, UnitCost: cost);

    private static UpsertLineRequest CatalogRequest(string code) => new(
        CatalogCode: code, Quantity: 1, UnitPrice: null, DiscountPercent: 0,
        Promotion: PromotionKind.None, Source: LineSource.Customer, AssignedTechnicianId: null, Note: null);

    private static Quotation QuotationWithLines(params QuotationLine[] lines)
    {
        var q = new Quotation { Id = Guid.NewGuid(), Status = QuotationStatus.Draft };
        foreach (var line in lines) q.Lines.Add(line);
        return q;
    }

    private static QuotationLine AdHocLine(string name, LineType type, long? technicianId) => new()
    {
        Id = Guid.NewGuid(), CatalogCode = "", Name = name, Type = type, Unit = "งาน",
        Quantity = 1, UnitPrice = 500m, AssignedTechnicianId = technicianId,
    };

    private sealed class StubCurrentUser(UserRole role) : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "พนักงาน ทดสอบ";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
    }

    private sealed class FakeQuotationRepository(Quotation? quotation) : IQuotationRepository
    {
        public List<Quotation> AddedQuotations { get; } = [];
        public List<ActivityEvent> SavedEvents { get; } = [];

        public Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(quotation?.Id == id ? quotation : null);
        public Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(quotation?.Id == id ? quotation : null);
        public Task<Quotation?> GetLatestForJobAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult(quotation);
        public Task<IReadOnlyList<Quotation>> GetQueueAsync(
            string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Quotation>>(quotation is null ? [] : [quotation]);
        public Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default) =>
            Task.FromResult((quotation?.Version ?? 0) + 1);
        public Task AddAsync(Quotation q, CancellationToken ct = default)
        {
            AddedQuotations.Add(q);
            return Task.CompletedTask;
        }
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default)
        {
            SavedEvents.Add(evt);
            return Task.CompletedTask;
        }
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class FakeCatalogRepository : ICatalogRepository
    {
        // เริ่มว่างเสมอ — ไม่กระทบเทสต์เดิมที่คาดว่าทุกรหัสหาเจอ; ใส่รหัสที่นี่เฉพาะเทสต์ที่ต้องจำลอง "รหัสหาย"
        public HashSet<string> MissingCodes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CatalogItem> Overrides { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<CatalogItem>> GetByCodesAsync(
            string shardKey, int branchId, IEnumerable<string> codes, CancellationToken ct = default)
        {
            IReadOnlyList<CatalogItem> items = codes
                .Where(code => !MissingCodes.Contains(code))
                .Select(code => Overrides.TryGetValue(code, out var over) ? over : new CatalogItem
                {
                    Code = code, Name = "สินค้าแคตตาล็อก", Type = LineType.Part, Unit = "ชิ้น",
                    Price = 250m, Cost = 120m, LegacyShardKey = shardKey, LegacyBranchId = branchId,
                }).ToList();
            return Task.FromResult(items);
        }

        public Task<IReadOnlyList<CatalogItem>> SearchAsync(
            string shardKey, int branchId, string? keyword, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<(IReadOnlyList<CatalogItem> Items, int Total)> SearchManagementAsync(
            string shardKey, int branchId, CatalogManagementQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<CatalogItem?> GetAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<bool> CodeExistsAsync(string shardKey, int branchId, string code, Guid? excludingId,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(CatalogItem item, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeJobRepository : IJobRepository
    {
        public Task<Job?> GetAsync(Guid jobId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Job?> GetOpenByVehicleAsync(
            string shardKey, int branchId, long vehicleId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Job>> SearchAsync(JobSearchQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<IReadOnlyList<Job>> GetAppointmentsAsync(JobAppointmentQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<int> CountOpenAsync(string shardKey, int branchId, int? jobTypeId, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task AddAsync(Job job, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeQuotationTemplateRepository : IQuotationTemplateRepository
    {
        private readonly List<QuotationTemplate> _templates = [];
        public void Seed(QuotationTemplate template) => _templates.Add(template);

        public Task<IReadOnlyList<QuotationTemplate>> SearchAsync(
            string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<QuotationTemplate?> GetWithLinesAsync(
            string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(_templates.FirstOrDefault(t =>
                t.Id == id && t.LegacyShardKey == shardKey && t.LegacyBranchId == branchId));

        public Task<bool> CodeExistsAsync(
            string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(QuotationTemplate template, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public void RemoveLines(IEnumerable<QuotationTemplateLine> lines) => throw new NotSupportedException();

        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeLegacyReader : ILegacyReader
    {
        public Task<LegacyBranchDto?> GetBranchAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult<LegacyBranchDto?>(new LegacyBranchDto(branchId, "อู่ทดสอบ", null, null, null));
        public Task<IReadOnlyList<LegacyTechnicianDto>> GetTechniciansAsync(
            string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<LegacyTechnicianDto>>([]);
    }
}
