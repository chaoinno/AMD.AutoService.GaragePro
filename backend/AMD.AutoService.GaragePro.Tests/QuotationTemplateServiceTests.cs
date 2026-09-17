using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.QuotationTemplates;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>ครอบคลุมเทมเพลตใบเสนอราคา (master data) — docs/08-quotation-template.md</summary>
public sealed class QuotationTemplateServiceTests
{
    private static QuotationTemplateLineInput CatalogLine(string code, decimal qty = 1, decimal? unitPrice = null) =>
        new(code, null, null, null, qty, unitPrice, null, null, 0, PromotionKind.None, LineSource.Customer, null);

    private static QuotationTemplateLineInput AdHocLine(
        string? name, LineType? type, decimal? price, decimal qty = 1, decimal? cost = null) =>
        new("", name, type, null, qty, price, cost, null, 0, PromotionKind.None, LineSource.Customer, null);

    [Fact]
    public async Task Non_manager_cannot_create_update_or_change_status()
    {
        var (service, _) = Build(UserRole.Office, ["OIL-5W30"]);
        var request = new QuotationTemplateUpsertRequest("TPL-001", "เช็คระยะ", null, [CatalogLine("OIL-5W30")]);

        var create = await service.CreateAsync(request);
        create.Success.Should().BeFalse();
        create.Error!.Code.Should().Be("MASTER_DATA_MANAGE_FORBIDDEN");

        var status = await service.SetStatusAsync(Guid.NewGuid(), new MasterDataStatusRequest(false));
        status.Success.Should().BeFalse();
        status.Error!.Code.Should().Be("MASTER_DATA_MANAGE_FORBIDDEN");
    }

    [Fact]
    public async Task Non_manager_can_read_but_unit_cost_is_stripped()
    {
        var (service, repo) = Build(UserRole.Office, ["OIL-5W30"]);
        var template = SeedTemplate(repo, [new QuotationTemplateLine
        {
            CatalogCode = "", Name = "ค่าแรงเปลี่ยนน้ำมันเครื่อง", Type = LineType.Labor, Unit = "งาน",
            Quantity = 1, UnitPrice = 350m, UnitCost = 120m, Source = LineSource.Technician
        }]);

        var result = await service.GetAsync(template.Id);

        result.Success.Should().BeTrue();
        result.Data!.Lines.Single().UnitCost.Should().BeNull();
    }

    [Fact]
    public async Task Manager_sees_real_unit_cost()
    {
        var (service, repo) = Build(UserRole.Manager, ["OIL-5W30"]);
        var template = SeedTemplate(repo, [new QuotationTemplateLine
        {
            CatalogCode = "", Name = "ค่าแรงเปลี่ยนน้ำมันเครื่อง", Type = LineType.Labor, Unit = "งาน",
            Quantity = 1, UnitPrice = 350m, UnitCost = 120m, Source = LineSource.Technician
        }]);

        var result = await service.GetAsync(template.Id);

        result.Data!.Lines.Single().UnitCost.Should().Be(120m);
    }

    [Fact]
    public async Task Create_fails_with_no_lines()
    {
        var (service, _) = Build(UserRole.Manager, []);

        var result = await service.CreateAsync(new QuotationTemplateUpsertRequest("TPL-001", "เทมเพลตว่าง", null, []));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("QUOTE_TEMPLATE_NO_LINES");
    }

    [Fact]
    public async Task Create_fails_when_ad_hoc_line_is_missing_name_type_or_price()
    {
        var (service, _) = Build(UserRole.Manager, []);

        var noName = await service.CreateAsync(new QuotationTemplateUpsertRequest(
            "TPL-001", "ทดสอบ", null, [AdHocLine(null, LineType.Labor, 100)]));
        var noType = await service.CreateAsync(new QuotationTemplateUpsertRequest(
            "TPL-002", "ทดสอบ", null, [AdHocLine("งานทดสอบ", null, 100)]));
        var noPrice = await service.CreateAsync(new QuotationTemplateUpsertRequest(
            "TPL-003", "ทดสอบ", null, [AdHocLine("งานทดสอบ", LineType.Labor, null)]));

        noName.Error!.Code.Should().Be("QUOTE_TEMPLATE_LINE_NAME_REQUIRED");
        noType.Error!.Code.Should().Be("QUOTE_TEMPLATE_LINE_TYPE_REQUIRED");
        noPrice.Error!.Code.Should().Be("QUOTE_TEMPLATE_LINE_PRICE_REQUIRED");
    }

    [Fact]
    public async Task Create_fails_when_quantity_is_not_positive()
    {
        var (service, _) = Build(UserRole.Manager, ["OIL-5W30"]);

        var result = await service.CreateAsync(new QuotationTemplateUpsertRequest(
            "TPL-001", "ทดสอบ", null, [CatalogLine("OIL-5W30", qty: 0)]));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("QUOTE_TEMPLATE_LINE_QTY_INVALID");
    }

    [Fact]
    public async Task Create_fails_when_catalog_code_and_catalog_code_repeat_in_the_same_template()
    {
        var (service, _) = Build(UserRole.Manager, ["OIL-5W30"]);

        var result = await service.CreateAsync(new QuotationTemplateUpsertRequest(
            "TPL-001", "ทดสอบ", null, [CatalogLine("OIL-5W30"), CatalogLine("oil-5w30")]));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("QUOTE_TEMPLATE_DUPLICATE_LINE");
    }

    [Fact]
    public async Task Create_fails_when_two_ad_hoc_lines_share_the_same_name()
    {
        var (service, _) = Build(UserRole.Manager, []);

        var result = await service.CreateAsync(new QuotationTemplateUpsertRequest(
            "TPL-001", "ทดสอบ", null,
            [AdHocLine("ถอดล้างเทอร์โบ", LineType.Labor, 1500), AdHocLine("ถอดล้างเทอร์โบ", LineType.Labor, 1600)]));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("QUOTE_TEMPLATE_DUPLICATE_LINE");
    }

    [Fact]
    public async Task Create_fails_when_catalog_code_does_not_exist()
    {
        var (service, _) = Build(UserRole.Manager, ["OIL-5W30"]);

        var result = await service.CreateAsync(new QuotationTemplateUpsertRequest(
            "TPL-001", "ทดสอบ", null, [CatalogLine("NOT-EXIST")]));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("QUOTE_TEMPLATE_ITEM_MISSING");
    }

    [Fact]
    public async Task Create_succeeds_with_a_mix_of_catalog_and_ad_hoc_lines()
    {
        var (service, _) = Build(UserRole.Manager, ["OIL-5W30"]);

        var result = await service.CreateAsync(new QuotationTemplateUpsertRequest(
            "TPL-001", "เช็คระยะ 10,000 กม.", "ชุดมาตรฐาน",
            [CatalogLine("OIL-5W30", qty: 4), AdHocLine("ค่าแรงเปลี่ยนถ่าย", LineType.Labor, 350)]));

        result.Success.Should().BeTrue();
        result.Data!.LineCount.Should().Be(2);
        result.Data!.PartCount.Should().Be(1);
        result.Data!.LaborCount.Should().Be(1);
    }

    [Fact]
    public async Task Create_fails_when_code_already_exists()
    {
        var (service, repo) = Build(UserRole.Manager, ["OIL-5W30"]);
        SeedTemplate(repo, [new QuotationTemplateLine
        {
            CatalogCode = "OIL-5W30", Name = "น้ำมันเครื่อง", Type = LineType.Part, Unit = "ลิตร", Quantity = 1
        }], code: "TPL-001");

        var result = await service.CreateAsync(new QuotationTemplateUpsertRequest(
            "TPL-001", "อีกชื่อหนึ่ง", null, [CatalogLine("OIL-5W30")]));

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be("QUOTE_TEMPLATE_CODE_DUPLICATE");
    }

    [Fact]
    public async Task Update_replaces_the_whole_line_set()
    {
        var (service, repo) = Build(UserRole.Manager, ["OIL-5W30", "BRK-001"]);
        var template = SeedTemplate(repo, [
            new QuotationTemplateLine { CatalogCode = "OIL-5W30", Name = "น้ำมันเครื่อง", Type = LineType.Part, Unit = "ลิตร", Quantity = 1, Sequence = 1 },
            new QuotationTemplateLine { CatalogCode = "BRK-001", Name = "ผ้าเบรก", Type = LineType.Part, Unit = "ชุด", Quantity = 1, Sequence = 2 },
        ]);

        var result = await service.UpdateAsync(template.Id, new QuotationTemplateUpsertRequest(
            template.Code, template.Name, null, [CatalogLine("OIL-5W30")]));

        result.Success.Should().BeTrue();
        result.Data!.LineCount.Should().Be(1);
        var reloaded = await service.GetAsync(template.Id);
        reloaded.Data!.Lines.Should().ContainSingle(l => l.Sequence == 1 && l.CatalogCode == "OIL-5W30");
    }

    private static (QuotationTemplateService Service, FakeQuotationTemplateRepository Repo) Build(
        UserRole role, IReadOnlyList<string> knownCatalogCodes)
    {
        var repo = new FakeQuotationTemplateRepository();
        var catalog = new FakeCatalogRepository(knownCatalogCodes);
        var service = new QuotationTemplateService(repo, catalog, new StubCurrentUser(role), TimeProvider.System);
        return (service, repo);
    }

    private static QuotationTemplate SeedTemplate(
        FakeQuotationTemplateRepository repo, IReadOnlyList<QuotationTemplateLine> lines, string code = "TPL-000")
    {
        var template = new QuotationTemplate
        {
            Code = code, Name = "เทมเพลตทดสอบ", LegacyShardKey = "db2", LegacyBranchId = 105,
            IsActive = true, Lines = lines.ToList()
        };
        foreach (var line in template.Lines) line.QuotationTemplateId = template.Id;
        repo.Seed(template);
        return template;
    }

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

    private sealed class FakeCatalogRepository(IReadOnlyList<string> knownCodes) : ICatalogRepository
    {
        public Task<IReadOnlyList<CatalogItem>> GetByCodesAsync(
            string shardKey, int branchId, IEnumerable<string> codes, CancellationToken ct = default)
        {
            IReadOnlyList<CatalogItem> items = codes
                .Where(c => knownCodes.Contains(c, StringComparer.OrdinalIgnoreCase))
                .Select(code => new CatalogItem
                {
                    Code = code, Name = $"สินค้า {code}", Type = LineType.Part, Unit = "ชิ้น",
                    Price = 250m, Cost = 120m, IsActive = true,
                    LegacyShardKey = shardKey, LegacyBranchId = branchId,
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

    private sealed class FakeQuotationTemplateRepository : IQuotationTemplateRepository
    {
        private readonly List<QuotationTemplate> _templates = [];
        public IReadOnlyList<ActivityEvent> Events => _events;
        private readonly List<ActivityEvent> _events = [];

        public void Seed(QuotationTemplate template) => _templates.Add(template);

        public Task<IReadOnlyList<QuotationTemplate>> SearchAsync(
            string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<QuotationTemplate>>(_templates
                .Where(t => t.LegacyShardKey == shardKey && t.LegacyBranchId == branchId && (includeInactive || t.IsActive))
                .ToList());

        public Task<QuotationTemplate?> GetWithLinesAsync(
            string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(_templates.FirstOrDefault(t =>
                t.Id == id && t.LegacyShardKey == shardKey && t.LegacyBranchId == branchId));

        public Task<bool> CodeExistsAsync(
            string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default) =>
            Task.FromResult(_templates.Any(t =>
                t.LegacyShardKey == shardKey && t.LegacyBranchId == branchId &&
                string.Equals(t.Code, code, StringComparison.OrdinalIgnoreCase) && t.Id != excludingId));

        public Task AddAsync(QuotationTemplate template, CancellationToken ct = default)
        {
            _templates.Add(template);
            return Task.CompletedTask;
        }

        public void RemoveLines(IEnumerable<QuotationTemplateLine> lines) { /* no-op — entity.Lines.Clear() ใน service จัดการแล้ว */ }

        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default)
        {
            _events.Add(activityEvent);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }
}
