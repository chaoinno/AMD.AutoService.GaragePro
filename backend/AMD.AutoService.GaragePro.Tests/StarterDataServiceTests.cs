using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Attachments;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Customers;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Jobs;
using AMD.AutoService.GaragePro.Application.Quotations;
using AMD.AutoService.GaragePro.Application.QuotationTemplates;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.Staff;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ตัวสร้าง/ล้างข้อมูลตัวอย่าง (S3) ด้วย fake ของ service จริงทุกตัว + ShardScopeFactory ตัวจริง
/// — พิสูจน์ว่าทุกคำสั่งวิ่งในนามเจ้าของอู่ (sp1 · Manager · Web) · ขั้นที่ล้มไม่หยุดขั้นอื่น · ledger ครบ · ล้างย้อนลำดับและทำซ้ำได้
/// </summary>
public class StarterDataServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid WorkspaceId = Guid.Parse("5b1f6c1e-0000-4000-8000-000000001001");
    private const int BranchId = 41;
    private const long OwnerUserId = 900;
    private const long OwnerStaffId = 700;
    private const string OwnerName = "สมศักดิ์ เจ้าของอู่";

    // ───────────────────────────────────────────── Load

    [Fact]
    public async Task Load_creates_everything_as_the_owner_and_records_every_row_in_the_ledger()
    {
        var (service, world) = Build();

        var result = await service.LoadAsync(WorkspaceId);

        result.Loaded.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Technicians.Should().Equal(
            new SampleCredential("SP1001.T01", "pw-T01"), new SampleCredential("SP1001.T02", "pw-T02"));

        // ทุกคำสั่งของ service จริงต้องมาจากเจ้าของอู่ใน shard sp1 — JobStateMachine ไม่ยอม System
        world.Calls.Should().NotBeEmpty().And.OnlyContain(c =>
            c.Shard == "sp1" && c.Branch == BranchId && c.Role == UserRole.Manager && c.Source == EventSource.Web &&
            c.UserId == OwnerUserId && c.StaffId == OwnerStaffId);

        world.Store.Ledger.GroupBy(e => e.EntityType).ToDictionary(g => g.Key, g => g.Count()).Should().BeEquivalentTo(
            new Dictionary<string, int>
            {
                [StarterDataService.EntityTypes.CatalogItem] = 42,
                [StarterDataService.EntityTypes.QuotationTemplate] = 2,
                [StarterDataService.EntityTypes.Staff] = 2,
                [StarterDataService.EntityTypes.Customer] = 3,
                [StarterDataService.EntityTypes.Car] = 3,
                [StarterDataService.EntityTypes.Job] = 2,
                [StarterDataService.EntityTypes.Quotation] = 2,
                [StarterDataService.EntityTypes.Attachment] = 1
            });
        world.Store.LoadedAt.Should().Be(Now.UtcDateTime);
        world.Store.Error.Should().BeNull();

        world.Seeded.Should().Be(("sp1", BranchId));
        world.Repo.Tags.Should().BeEquivalentTo(new[]
        {
            ("customer", 1L, "SAMPLE-C01"), ("customer", 2L, "SAMPLE-C02"), ("customer", 3L, "SAMPLE-C03"),
            ("car", 11L, "SAMPLE-V01"), ("car", 12L, "SAMPLE-V02"), ("car", 13L, "SAMPLE-V03")
        });
    }

    [Fact]
    public async Task Load_maps_categories_warehouse_and_starting_stock_onto_catalog_requests()
    {
        var (service, world) = Build();

        await service.LoadAsync(WorkspaceId);

        var labor = world.CatalogRequests.Single(r => r.Code == "L-PM10-01");
        labor.CategoryId.Should().Be(world.Repo.CategoryIds["SVC"]);
        labor.WarehouseId.Should().BeNull("ค่าแรงไม่มีคลัง");
        labor.StandardHours.Should().Be(1m);

        var oil = world.CatalogRequests.Single(r => r.Code == "P-OIL-5W30-4L");
        oil.CategoryId.Should().Be(world.Repo.CategoryIds["FLUID"]);
        oil.WarehouseId.Should().Be(world.Repo.WarehouseId);
        oil.Cost.Should().Be(820m);
        oil.Price.Should().Be(1250m);
        oil.OnHand.Should().BeGreaterThan(0);

        world.CatalogRequests.Single(r => r.Code == "P-FLT-OIL").CategoryId.Should().Be(world.Repo.CategoryIds["PART"]);
        world.TemplateRequests.Select(t => t.Code).Should().Equal("TPL-PM10", "TPL-BRK-F");
        world.TemplateRequests[0].Lines.Select(l => l.CatalogCode).Should().Equal("L-PM10-01", "P-OIL-5W30-4L", "P-FLT-OIL");
    }

    [Fact]
    public async Task Load_drives_job_A_to_waiting_for_customer_and_job_B_to_in_progress_in_order()
    {
        var (service, world) = Build();

        await service.LoadAsync(WorkspaceId);

        var jobA = world.Jobs[0];
        var jobB = world.Jobs[1];
        jobA.Request.MileageAtIntake.Should().Be(45_210);
        jobB.Request.MileageAtIntake.Should().Be(68_900);
        world.Jobs.Should().OnlyContain(j => j.Request.JobTypeId == 9);

        // WaitInspect→WaitQuote ไม่มีระบบตรวจอัตโนมัติ — ต้องส่งเหตุผล · เส้นอื่นคำนวณจากข้อมูลจริงจึงไม่ส่ง
        world.TransitionsOf(jobA.Id).Should().Equal(("waitquote", "ข้อมูลตัวอย่าง"), ("waitapprove", null));
        world.TransitionsOf(jobB.Id).Should().Equal(
            ("waitquote", "ข้อมูลตัวอย่าง"), ("waitapprove", null), ("approved", null), ("inprogress", null));

        // ใบ A ส่งแล้วยังไม่ตัดสินใจ · ใบ B ตัดสินใจครบแล้วเซ็นด้วยไฟล์ลายเซ็นที่อัปโหลด
        var quotationA = world.QuotationOf(jobA.Id);
        quotationA.Status.Should().Be(QuotationStatus.Sent);
        quotationA.Lines.Should().OnlyContain(l => l.ApprovalStatus == LineApprovalStatus.Pending);

        var quotationB = world.QuotationOf(jobB.Id);
        quotationB.Lines.Should().OnlyContain(l => l.ApprovalStatus == LineApprovalStatus.Approved);
        world.Signatures.Should().ContainSingle().Which.Should().Be((quotationB.Id, "sp1/41/sig.png"));
        world.Uploads.Should().ContainSingle().Which.Should().Match<UploadAttachmentRequest>(u =>
            u.JobId == jobB.Id && u.Kind == "signature" && u.EntityId == quotationB.Id && u.ContentType == "image/png");

        // [BIZ] ค่าแรงต้องระบุช่าง — ใช้ช่างตัวอย่างคนแรก · อะไหล่ไม่ระบุ
        var technicianId = world.StaffIds[0];
        foreach (var line in world.QuotationRequests)
        {
            var item = StarterCatalog.Find(line.CatalogCode)!;
            line.AssignedTechnicianId.Should().Be(item.Type == LineType.Labor ? technicianId : null, line.CatalogCode);
        }

        // ลำดับใหญ่: แคตตาล็อก → เทมเพลต → ช่าง → ลูกค้า → รถ → จ๊อบ
        var order = world.Calls.Select(c => c.Name).Distinct().ToList();
        order.IndexOf("catalog.create").Should().BeLessThan(order.IndexOf("template.create"));
        order.IndexOf("template.create").Should().BeLessThan(order.IndexOf("staff.create"));
        order.IndexOf("staff.create").Should().BeLessThan(order.IndexOf("customer.create"));
        order.IndexOf("customer.create").Should().BeLessThan(order.IndexOf("vehicle.create"));
        order.IndexOf("vehicle.create").Should().BeLessThan(order.IndexOf("job.create"));
    }

    [Fact]
    public async Task A_failing_step_does_not_stop_the_other_steps()
    {
        var (service, world) = Build(w => w.StaffFailure = PlanErrors.LimitReached(PlanLimitKind.Staff, 5));

        var result = await service.LoadAsync(WorkspaceId);

        result.Loaded.Should().BeTrue("ขั้นอื่นสร้างสำเร็จ");
        result.Technicians.Should().BeEmpty();
        result.Error.Should().Contain("ช่างตัวอย่าง").And.Contain("PLAN_LIMIT_REACHED");
        world.Store.Error.Should().Be(result.Error);

        world.Store.Ledger.Should().NotContain(e => e.EntityType == StarterDataService.EntityTypes.Staff);
        world.Store.Ledger.Count(e => e.EntityType == StarterDataService.EntityTypes.CatalogItem).Should().Be(42);
        world.Store.Ledger.Count(e => e.EntityType == StarterDataService.EntityTypes.Job).Should().Be(2);

        // ไม่มีช่างตัวอย่าง → บรรทัดค่าแรงระบุเจ้าของอู่แทน (พนักงาน active ของสาขา) ใบเสนอราคายังส่งได้
        world.QuotationRequests.Where(l => StarterCatalog.Find(l.CatalogCode)!.Type == LineType.Labor)
            .Should().OnlyContain(l => l.AssignedTechnicianId == OwnerStaffId);
    }

    [Fact]
    public async Task Missing_vehicle_reference_data_skips_vehicles_and_jobs_but_keeps_the_rest()
    {
        var (service, world) = Build(w => w.Repo.VehicleRefs = null);

        var result = await service.LoadAsync(WorkspaceId);

        result.Loaded.Should().BeTrue();
        result.Error.Should().Contain("ยี่ห้อ/รุ่น");
        world.Jobs.Should().BeEmpty();
        world.Store.Ledger.Should().NotContain(e =>
            e.EntityType == StarterDataService.EntityTypes.Car || e.EntityType == StarterDataService.EntityTypes.Job);
        world.Store.Ledger.Count(e => e.EntityType == StarterDataService.EntityTypes.Customer).Should().Be(3);
        world.Repo.Tags.Should().OnlyContain(t => t.Kind == "customer");
    }

    [Fact]
    public async Task Signature_upload_failure_stops_job_B_at_waiting_for_customer()
    {
        var (service, world) = Build(w => w.UploadThrows = true);

        var result = await service.LoadAsync(WorkspaceId);

        result.Loaded.Should().BeTrue();
        result.Error.Should().Contain("ลายเซ็น").And.Contain("รอลูกค้าอนุมัติ");
        var jobB = world.Jobs[1];
        world.TransitionsOf(jobB.Id).Should().Equal(("waitquote", "ข้อมูลตัวอย่าง"), ("waitapprove", null));
        world.QuotationOf(jobB.Id).Lines.Should().OnlyContain(l => l.ApprovalStatus == LineApprovalStatus.Pending,
            "ไม่มีการตัดสินใจค้างครึ่งทางเมื่อเซ็นไม่ได้");
        world.Signatures.Should().BeEmpty();
        world.Store.Ledger.Should().NotContain(e => e.EntityType == StarterDataService.EntityTypes.Attachment);
    }

    [Fact]
    public async Task Load_is_a_no_op_when_sample_data_is_already_loaded()
    {
        var (service, world) = Build(w => w.Store.Workspace = w.Store.Workspace! with { SampleDataLoadedAt = Now.UtcDateTime.AddDays(-1) });

        var result = await service.LoadAsync(WorkspaceId);

        result.Loaded.Should().BeTrue();
        result.Technicians.Should().BeEmpty("รหัสผ่านคืนได้ครั้งเดียวตอนสร้าง");
        result.Error.Should().BeNull();
        world.Calls.Should().BeEmpty();
        world.Store.Ledger.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_reports_when_the_workspace_is_not_ready()
    {
        var (service, world) = Build(w => w.Store.Workspace = null);

        var result = await service.LoadAsync(WorkspaceId);

        result.Loaded.Should().BeFalse();
        result.Error.Should().Be(StarterDataErrors.WorkspaceNotReady().MessageTh);
        world.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Load_clears_leftovers_of_an_interrupted_run_before_creating_a_fresh_set()
    {
        var (service, world) = Build();
        var leftoverItem = Guid.NewGuid();
        world.Store.Seed(StarterDataService.EntityTypes.CatalogItem, leftoverItem.ToString());

        var result = await service.LoadAsync(WorkspaceId);

        result.Loaded.Should().BeTrue();
        world.Repo.Deleted.Should().Contain(("catalog", leftoverItem.ToString()));
        world.Store.Ledger.Should().NotContain(e => e.EntityId == leftoverItem.ToString());
        world.Store.Ledger.Count(e => e.EntityType == StarterDataService.EntityTypes.CatalogItem).Should().Be(42);
    }

    // ───────────────────────────────────────────── Clear

    [Fact]
    public async Task Clear_deletes_in_reverse_order_and_is_idempotent()
    {
        var (service, world) = Build();
        await service.LoadAsync(WorkspaceId);
        var ledgerBefore = world.Store.Ledger.ToList();
        world.Calls.Clear();
        world.Repo.Deleted.Clear();

        var first = await service.ClearAsync(WorkspaceId);

        first.Success.Should().BeTrue();
        world.Store.Ledger.Should().BeEmpty();
        world.Store.LoadedAt.Should().BeNull("ล้างครบแล้วต้องโหลดใหม่ได้");
        world.Store.Error.Should().BeNull();

        // ลำดับย้อนการสร้าง: จ๊อบ → รถ → ลูกค้า → ช่าง → เทมเพลต → แคตตาล็อก (ภายในกลุ่มตัวที่สร้างทีหลังก่อน)
        var kinds = world.Repo.Deleted.Select(d => d.Kind).Distinct().ToList();
        kinds.Should().Equal("jobs", "car", "customer", "templates", "catalog");
        world.Repo.Deleted.Where(d => d.Kind == "car").Select(d => d.Id).Should().Equal("13", "12", "11");
        world.Repo.Deleted.Where(d => d.Kind == "customer").Select(d => d.Id).Should().Equal("3", "2", "1");
        world.Repo.Deleted.Single(d => d.Kind == "jobs").Id.Should().Be(string.Join(",", world.Jobs.Select(j => j.Id).OrderBy(id => id)));
        world.Repo.Deleted.Where(d => d.Kind == "catalog").Select(d => d.Id).Should().Equal(
            ledgerBefore.Where(e => e.EntityType == StarterDataService.EntityTypes.CatalogItem)
                .OrderByDescending(e => e.Id).Select(e => e.EntityId));

        // ช่างตัวอย่างปิดใช้งาน (ลบไม่ได้ — ประวัติอ้างถึง) หลังลบลูกค้า ก่อนลบเทมเพลต
        world.StaffStatusChanges.Should().Equal(
            (world.StaffIds[1], false, Now.UtcDateTime.AddHours(7).Date), (world.StaffIds[0], false, Now.UtcDateTime.AddHours(7).Date));
        world.Calls.Should().OnlyContain(c => c.Role == UserRole.Manager && c.Source == EventSource.Web && c.Shard == "sp1");

        // ครั้งที่สองไม่มีอะไรให้ลบ — สำเร็จเฉยๆ
        world.Repo.Deleted.Clear();
        world.StaffStatusChanges.Clear();
        var second = await service.ClearAsync(WorkspaceId);
        second.Success.Should().BeTrue();
        world.Repo.Deleted.Should().BeEmpty();
        world.StaffStatusChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task Clear_deactivates_catalog_items_used_by_real_quotations_instead_of_deleting_them()
    {
        var (service, world) = Build();
        await service.LoadAsync(WorkspaceId);
        var usedId = world.CatalogIds["P-FLT-OIL"];
        world.Repo.CatalogInUse.Add(usedId);

        var result = await service.ClearAsync(WorkspaceId);

        result.Success.Should().BeTrue();
        world.CatalogStatusChanges.Should().Equal((usedId, false));
        world.Store.Ledger.Should().BeEmpty("สินค้าที่ปิดใช้งานกลายเป็นข้อมูลจริงของอู่ ไม่ต้องติดตามต่อ");
    }

    [Fact]
    public async Task Clear_keeps_the_ledger_of_a_failed_group_so_a_retry_can_finish_it()
    {
        var (service, world) = Build();
        await service.LoadAsync(WorkspaceId);
        var loadedAt = world.Store.LoadedAt;
        world.Repo.DeleteJobsThrows = true;

        var result = await service.ClearAsync(WorkspaceId);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(StarterDataErrors.ClearIncompleteCode);
        world.Store.Ledger.Select(e => e.EntityType).Distinct().Should().BeEquivalentTo(
            [StarterDataService.EntityTypes.Job, StarterDataService.EntityTypes.Quotation, StarterDataService.EntityTypes.Attachment]);
        world.Store.LoadedAt.Should().Be(loadedAt, "ยังมีข้อมูลตัวอย่างเหลือ");
        world.Store.Error.Should().Contain("ลบจ๊อบตัวอย่าง");
        world.Repo.Deleted.Should().Contain(d => d.Kind == "catalog", "กลุ่มอื่นยังทำต่อ");

        world.Repo.DeleteJobsThrows = false;
        (await service.ClearAsync(WorkspaceId)).Success.Should().BeTrue();
        world.Store.Ledger.Should().BeEmpty();
        world.Store.LoadedAt.Should().BeNull();
    }

    [Fact]
    public async Task Clear_reports_when_the_workspace_is_not_ready()
    {
        var (service, _) = Build(w => w.Store.Workspace = null);

        var result = await service.ClearAsync(WorkspaceId);

        result.Success.Should().BeFalse();
        result.Error!.Code.Should().Be(StarterDataErrors.WorkspaceNotReadyCode);
    }

    // ───────────────────────────────────────────── DI

    [Fact]
    public void Starter_data_is_registered_only_when_the_self_serve_database_is_configured()
    {
        static ServiceProvider Provider(string? connectionString)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    [SelfServeInfrastructure.ConnectionStringKey] = connectionString
                })
                .Build();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSelfServeInfrastructure(configuration);
            return services.BuildServiceProvider();
        }

        using (var configured = Provider("Server=.;Database=GarageSelfServe_Dev"))
        using (var scope = configured.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IStarterDataService>().Should().BeOfType<StarterDataService>();
            scope.ServiceProvider.GetRequiredService<IStarterDataStore>().Should().BeOfType<StarterDataStore>();
        }

        using var legacyOnly = Provider(null);
        legacyOnly.CreateScope().ServiceProvider.GetService<IStarterDataService>()
            .Should().BeNull("deployment ที่ไม่มีฐาน self-serve ต้องสตาร์ทได้โดยไม่มี SelfServeDbContext");
    }

    // ───────────────────────────────────────────── plumbing

    private static (StarterDataService Service, World World) Build(Action<World>? configure = null)
    {
        var world = new World();
        configure?.Invoke(world);

        var services = new ServiceCollection();
        services.AddScoped<CurrentUserOverride>();
        services.AddSingleton<IShardScopeFactory, ShardScopeFactory>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserOverride>().User
            ?? throw new InvalidOperationException("ไม่มี HttpContext"));
        services.AddSingleton(world);
        services.AddSingleton<IStarterDataStore>(world.Store);
        services.AddSingleton<IStarterDataRepository>(world.Repo);
        services.AddScoped<IBranchDefaultsSeeder, FakeSeeder>();
        services.AddScoped<ICatalogService, FakeCatalogService>();
        services.AddScoped<IQuotationTemplateService, FakeTemplateService>();
        services.AddScoped<IStaffService, FakeStaffService>();
        services.AddScoped<ICustomerVehicleService, FakeCustomerVehicleService>();
        services.AddScoped<IJobService, FakeJobService>();
        services.AddScoped<IQuotationService, FakeQuotationService>();
        services.AddScoped<IAttachmentService, FakeAttachmentService>();

        var provider = services.BuildServiceProvider();
        var service = new StarterDataService(provider.GetRequiredService<IShardScopeFactory>(), new SelfServeOptions(),
            new FixedClock(Now));
        return (service, world);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record Call(string Name, string Shard, int Branch, UserRole Role, EventSource Source, long UserId, long? StaffId);

    private sealed record CreatedJob(Guid Id, CreateJobRequest Request);

    /// <summary>สถานะร่วมของ fake ทุกตัวในหนึ่งเทสต์</summary>
    private sealed class World
    {
        public FakeStore Store { get; } = new();
        public FakeRepository Repo { get; } = new();
        public List<Call> Calls { get; } = [];

        public ApiError? StaffFailure { get; set; }
        public bool UploadThrows { get; set; }

        public (string, int)? Seeded { get; set; }
        public List<CatalogUpsertRequest> CatalogRequests { get; } = [];
        public Dictionary<string, Guid> CatalogIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<(Guid Id, bool IsActive)> CatalogStatusChanges { get; } = [];
        public List<QuotationTemplateUpsertRequest> TemplateRequests { get; } = [];
        public List<long> StaffIds { get; } = [];
        public List<(long Id, bool IsActive, DateTime? EndJobDate)> StaffStatusChanges { get; } = [];
        public List<CreatedJob> Jobs { get; } = [];
        public List<(Guid JobId, string To, string? Reason)> Transitions { get; } = [];
        public Dictionary<Guid, Quotation> Quotations { get; } = [];
        public List<UpsertLineRequest> QuotationRequests { get; } = [];
        public List<UploadAttachmentRequest> Uploads { get; } = [];
        public List<(Guid QuotationId, string Path)> Signatures { get; } = [];

        private long _nextCustomer;
        private long _nextCar = 10;
        public long NextCustomerId() => ++_nextCustomer;
        public long NextCarId() => ++_nextCar;

        public void Log(string name, ICurrentUser user) =>
            Calls.Add(new Call(name, user.ShardKey, user.BranchId, user.Role, user.Source, user.UserId, user.StaffId));

        public IEnumerable<(string, string?)> TransitionsOf(Guid jobId) =>
            Transitions.Where(t => t.JobId == jobId).Select(t => (t.To, t.Reason));

        public Quotation QuotationOf(Guid jobId) => Quotations.Values.Single(q => q.JobId == jobId);
    }

    private sealed class FakeStore : IStarterDataStore
    {
        private long _nextId;

        public StarterWorkspace? Workspace { get; set; } = new(
            WorkspaceId, 1001, BranchId, 10, OwnerUserId, "SP1001.M01", OwnerName, OwnerStaffId, null);

        public List<StarterLedgerEntry> Ledger { get; } = [];
        public DateTime? LoadedAt { get; private set; }
        public string? Error { get; private set; }

        public void Seed(string type, string id) => Ledger.Add(new StarterLedgerEntry(++_nextId, type, id));

        public Task<StarterWorkspace?> GetWorkspaceAsync(Guid workspaceId, CancellationToken ct = default) =>
            Task.FromResult(workspaceId == WorkspaceId && Workspace is not null
                ? Workspace with { SampleDataLoadedAt = Workspace.SampleDataLoadedAt ?? LoadedAt }
                : null);

        public Task<IReadOnlyList<StarterLedgerEntry>> GetLedgerAsync(Guid workspaceId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<StarterLedgerEntry>>(Ledger.ToList());

        public Task RecordAsync(Guid workspaceId, string entityType, string entityId, CancellationToken ct = default)
        {
            if (!Ledger.Any(e => e.EntityType == entityType && e.EntityId == entityId)) Seed(entityType, entityId);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(Guid workspaceId, IReadOnlyCollection<long> ledgerIds, CancellationToken ct = default)
        {
            Ledger.RemoveAll(e => ledgerIds.Contains(e.Id));
            return Task.CompletedTask;
        }

        public Task SetSampleStateAsync(Guid workspaceId, DateTime? loadedAt, string? error, CancellationToken ct = default)
        {
            LoadedAt = loadedAt;
            Error = error;
            if (Workspace is not null) Workspace = Workspace with { SampleDataLoadedAt = loadedAt };
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRepository : IStarterDataRepository
    {
        public Dictionary<string, Guid> CategoryIds { get; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["SVC"] = Guid.NewGuid(), ["PART"] = Guid.NewGuid(), ["FLUID"] = Guid.NewGuid()
        };

        public Guid WarehouseId { get; } = Guid.NewGuid();

        public SampleVehicleRefs? VehicleRefs { get; set; } = new(
            [new SampleVehicleModel(2, "TOYOTA", 3, "VIOS", 2142, "ปี 17-ปัจจุบัน"),
             new SampleVehicleModel(4, "ISUZU", 381, "D-Max", 5688, "EV Double Cab")], 73, 10);

        public bool DeleteJobsThrows { get; set; }
        public HashSet<Guid> CatalogInUse { get; } = [];
        public List<(string Kind, long Id, string Code)> Tags { get; } = [];
        public List<(string Kind, string Id)> Deleted { get; } = [];

        public Task<StarterBranchDefaults> GetBranchDefaultsAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult(new StarterBranchDefaults(CategoryIds, WarehouseId));

        public Task<SampleVehicleRefs?> PickSampleVehicleRefsAsync(string shardKey, int? preferredProvinceId,
            int modelYearAd, int take, CancellationToken ct = default)
        {
            modelYearAd.Should().Be(2023, "ปีรถ = ปีปัจจุบัน (ไทย) − 3");
            return Task.FromResult(VehicleRefs);
        }

        public Task TagSampleRowsAsync(string shardKey, IReadOnlyList<SampleRowTag> customers,
            IReadOnlyList<SampleRowTag> vehicles, CancellationToken ct = default)
        {
            Tags.AddRange(customers.Select(c => ("customer", c.Id, c.Code)));
            Tags.AddRange(vehicles.Select(v => ("car", v.Id, v.Code)));
            return Task.CompletedTask;
        }

        public byte[] GetSampleSignaturePng() => [0x89, 0x50, 0x4E, 0x47];

        public Task<int> DeleteJobsAsync(string shardKey, int branchId, IReadOnlyCollection<Guid> jobIds,
            CancellationToken ct = default)
        {
            if (DeleteJobsThrows) throw new InvalidOperationException("ฐานข้อมูลไม่ตอบสนอง");
            Deleted.Add(("jobs", string.Join(",", jobIds.OrderBy(id => id))));
            return Task.FromResult(jobIds.Count);
        }

        public Task<int> DeleteQuotationTemplatesAsync(string shardKey, int branchId, IReadOnlyCollection<Guid> templateIds,
            CancellationToken ct = default)
        {
            Deleted.Add(("templates", string.Join(",", templateIds)));
            return Task.FromResult(templateIds.Count);
        }

        public Task<SampleDeleteOutcome> DeleteCatalogItemAsync(string shardKey, int branchId, Guid catalogItemId,
            CancellationToken ct = default)
        {
            if (CatalogInUse.Contains(catalogItemId)) return Task.FromResult(SampleDeleteOutcome.InUse);
            Deleted.Add(("catalog", catalogItemId.ToString()));
            return Task.FromResult(SampleDeleteOutcome.Deleted);
        }

        public Task<SampleDeleteOutcome> DeleteVehicleAsync(string shardKey, int branchId, long vehicleId,
            CancellationToken ct = default)
        {
            Deleted.Add(("car", vehicleId.ToString()));
            return Task.FromResult(SampleDeleteOutcome.Deleted);
        }

        public Task<SampleDeleteOutcome> DeleteCustomerAsync(string shardKey, int branchId, long customerId,
            CancellationToken ct = default)
        {
            Deleted.Add(("customer", customerId.ToString()));
            return Task.FromResult(SampleDeleteOutcome.Deleted);
        }
    }

    private sealed class FakeSeeder(World world, ICurrentUser user) : IBranchDefaultsSeeder
    {
        public Task EnsureAsync(string shardKey, int branchId, CancellationToken ct = default)
        {
            world.Log("seed", user);
            world.Seeded = (shardKey, branchId);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCatalogService(World world, ICurrentUser user) : ICatalogService
    {
        public Task<Result<CatalogManagementItemDto>> CreateAsync(CatalogUpsertRequest request, CancellationToken ct = default)
        {
            world.Log("catalog.create", user);
            world.CatalogRequests.Add(request);
            var id = Guid.NewGuid();
            world.CatalogIds[request.Code] = id;
            return Task.FromResult(Result<CatalogManagementItemDto>.Ok(new CatalogManagementItemDto(
                id, request.Code, request.Type.ToString().ToLowerInvariant(), "", request.Name, null, request.Unit,
                request.Price, request.Cost, request.StandardHours, request.OnHand, 0, 0, 0, request.OnHand, null, true)));
        }

        public Task<Result<bool>> SetStatusAsync(Guid id, CatalogStatusRequest request, CancellationToken ct = default)
        {
            world.Log("catalog.status", user);
            world.CatalogStatusChanges.Add((id, request.IsActive));
            return Task.FromResult(Result<bool>.Ok(true));
        }

        public Task<Result<PagedResult<CatalogManagementItemDto>>> SearchAsync(CatalogManagementQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CatalogManagementItemDto>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CatalogManagementItemDto>> CreatePurchasePartAsync(PurchasePartCreateRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CatalogManagementItemDto>> UpdateAsync(Guid id, CatalogUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeTemplateService(World world, ICurrentUser user) : IQuotationTemplateService
    {
        public Task<Result<QuotationTemplateDto>> CreateAsync(QuotationTemplateUpsertRequest request, CancellationToken ct = default)
        {
            world.Log("template.create", user);
            world.TemplateRequests.Add(request);
            return Task.FromResult(Result<QuotationTemplateDto>.Ok(new QuotationTemplateDto(
                Guid.NewGuid(), request.Code, request.Name, request.Description, true, request.Lines.Count, 0, 0, [],
                Now.UtcDateTime, Now.UtcDateTime)));
        }

        public Task<Result<IReadOnlyList<QuotationTemplateDto>>> SearchAsync(string? keyword, bool includeInactive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<QuotationTemplateDto>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<QuotationTemplateDto>> UpdateAsync(Guid id, QuotationTemplateUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeStaffService(World world, ICurrentUser user) : IStaffService
    {
        public Task<Result<StaffDetailDto>> CreateAsync(StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default)
        {
            world.Log("staff.create", user);
            request.Role.Should().Be(UserRole.Technician);
            if (world.StaffFailure is not null) return Task.FromResult(Result<StaffDetailDto>.Fail(world.StaffFailure));

            var no = world.StaffIds.Count + 1;
            var id = 500L + no;
            world.StaffIds.Add(id);
            var userName = $"SP1001.T{no:D2}";
            return Task.FromResult(Result<StaffDetailDto>.Ok(new StaffDetailDto(
                id, BranchId, "อู่ทดสอบ", $"T{no:D2}", request.FirstName, request.LastName, 3, null, null, null, null,
                null, null, null, null, null, null, null, request.PhoneNumber1, null, null, null, null, null, null, null,
                null, null, null, null, null, true,
                new StaffAccountDto(id + 1000, userName, false, true, $"pw-T{no:D2}"), [], null, null)));
        }

        public Task<Result<bool>> SetStatusAsync(long id, StaffStatusRequest request, CancellationToken ct = default)
        {
            world.Log("staff.status", user);
            world.StaffStatusChanges.Add((id, request.IsActive, request.EndJobDate));
            return Task.FromResult(Result<bool>.Ok(true));
        }

        public Task<Result<PagedResult<StaffSummaryDto>>> SearchAsync(StaffSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<StaffDetailDto>> GetAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<StaffDetailDto>> UpdateAsync(long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<StaffCodePreviewDto>> PreviewCodeAsync(int? branchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<StaffImageFile>> OpenImageAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<StaffReferenceDataDto>> GetReferenceDataAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<LookupItemDto>>> GetSectorsAsync(int? departmentId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeCustomerVehicleService(World world, ICurrentUser user) : ICustomerVehicleService
    {
        public Task<Result<CustomerDetailDto>> CreateCustomerAsync(CustomerUpsertRequest request, CancellationToken ct = default)
        {
            world.Log("customer.create", user);
            var id = world.NextCustomerId();
            return Task.FromResult(Result<CustomerDetailDto>.Ok(new CustomerDetailDto(
                id, $"CUST-{id}", request.FirstName, request.LastName, null, null, null, null, null, null, null, null,
                null, null, null, null, null, request.PhoneNumber1, null, null, null, false, null, false, null, null, [])));
        }

        public Task<Result<VehicleDetailDto>> CreateVehicleAsync(VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default)
        {
            world.Log("vehicle.create", user);
            request.YearId.Should().Be(73);
            var id = world.NextCarId();
            return Task.FromResult(Result<VehicleDetailDto>.Ok(new VehicleDetailDto(
                id, request.Registration, request.ProvinceId, null, request.BrandId, null, request.ModelId, null,
                request.NicknameId, null, null, null, request.YearId, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null, null, null, false, null, null, [])));
        }

        public Task<Result<PagedResult<CustomerSummaryDto>>> SearchCustomersAsync(CustomerSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<CustomerSummaryDto>>> ExportCustomersAsync(CustomerSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CustomerDetailDto>> GetCustomerAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<CustomerDetailDto>> UpdateCustomerAsync(long id, CustomerUpsertRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> DeleteCustomerAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<PagedResult<VehicleSummaryDto>>> SearchVehiclesAsync(VehicleSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<VehicleSummaryDto>>> ExportVehiclesAsync(VehicleSearchQuery query, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> GetVehicleAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> UpdateVehicleAsync(long id, VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> DeleteVehicleAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VehicleImageFile>> OpenVehicleImageAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<VehicleDetailDto>> UpdateVehicleImageAsync(long id, VehicleImageUpload image, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeJobService(World world, ICurrentUser user) : IJobService
    {
        public Task<Result<CreatedJobDto>> CreateAsync(CreateJobRequest request, CancellationToken ct = default)
        {
            world.Log("job.create", user);
            var job = new CreatedJob(Guid.NewGuid(), request);
            world.Jobs.Add(job);
            return Task.FromResult(Result<CreatedJobDto>.Ok(new CreatedJobDto(job.Id, $"JB{world.Jobs.Count:D3}")));
        }

        public Task<Result<JobTransitionResultDto>> TransitionAsync(Guid jobId, TransitionJobRequest request, CancellationToken ct = default)
        {
            world.Log("job.transition", user);
            world.Transitions.Add((jobId, request.ToStatus, request.Reason));
            return Task.FromResult(Result<JobTransitionResultDto>.Ok(new JobTransitionResultDto(request.ToStatus, request.ToStatus)));
        }

        public Task<Result<JobDto>> GetAsync(Guid jobId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobDto>>> SearchAsync(string? keyword, int take, DateTime? beforeCreatedAt, Guid? beforeJobId, int? jobTypeId, string? statusToken, CancellationToken ct = default) => throw new NotSupportedException();
        public IReadOnlyList<JobStatusOptionDto> GetStatusOptions() => throw new NotSupportedException();
        public Task<Result<int>> CountOpenAsync(int? jobTypeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<JobCountsDto>> CountsAsync(int? jobTypeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<JobDto>> UpdateAppointmentAsync(Guid jobId, UpdateJobAppointmentRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<JobDto>> UpdatePromiseAsync(Guid jobId, UpdateJobPromiseRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<JobDto>> UpdateMileageAsync(Guid jobId, UpdateJobMileageRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<JobScheduleChangeDto>>> GetScheduleHistoryAsync(Guid jobId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<JobDto>> ConvertToInShopAsync(Guid jobId, ConvertToInShopRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<JobCalendarDto>> GetCalendarAsync(DateTimeOffset from, DateTimeOffset to, string? keyword, string? statusToken, string? dateField = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeQuotationService(World world, ICurrentUser user) : IQuotationService
    {
        private QuotationDto Dto(Quotation q) => QuotationMapper.ToDto(q, user, Now.UtcDateTime);

        public Task<Result<QuotationDto>> CreateAsync(CreateQuotationRequest request, CancellationToken ct = default)
        {
            world.Log("quotation.create", user);
            var q = new Quotation { JobId = request.JobId, Code = $"QT-{world.Quotations.Count + 1:D2}", Version = 1 };
            world.Quotations[q.Id] = q;
            return Task.FromResult(Result<QuotationDto>.Ok(Dto(q)));
        }

        public Task<Result<QuotationDto>> AddLineAsync(Guid id, UpsertLineRequest request, CancellationToken ct = default)
        {
            world.Log("quotation.line", user);
            world.QuotationRequests.Add(request);
            var q = world.Quotations[id];
            var item = StarterCatalog.Find(request.CatalogCode)!;
            q.Lines.Add(new QuotationLine
            {
                QuotationId = id, Sequence = q.Lines.Count + 1, CatalogCode = item.Code, Name = item.Name,
                Type = item.Type, Source = request.Source, Quantity = request.Quantity, Unit = item.Unit,
                UnitPrice = item.Price, AssignedTechnicianId = request.AssignedTechnicianId
            });
            return Task.FromResult(Result<QuotationDto>.Ok(Dto(q)));
        }

        public Task<Result<QuotationDto>> SendAsync(Guid id, CancellationToken ct = default)
        {
            world.Log("quotation.send", user);
            var q = world.Quotations[id];
            q.Status = QuotationStatus.Sent;
            return Task.FromResult(Result<QuotationDto>.Ok(Dto(q)));
        }

        public Task<Result<QuotationDto>> DecideLineAsync(Guid id, Guid lineId, LineDecisionRequest request, CancellationToken ct = default)
        {
            world.Log("quotation.decide", user);
            var q = world.Quotations[id];
            q.Lines.Single(l => l.Id == lineId).ApprovalStatus = request.Decision;
            return Task.FromResult(Result<QuotationDto>.Ok(Dto(q)));
        }

        public Task<Result<QuotationDto>> SignAsync(Guid id, SignQuotationRequest request, CancellationToken ct = default)
        {
            world.Log("quotation.sign", user);
            request.WitnessEmployeeId.Should().Be(OwnerStaffId);
            request.WitnessEmployeeName.Should().Be(OwnerName);
            world.Signatures.Add((id, request.SignatureImagePath));
            var q = world.Quotations[id];
            q.Status = QuotationStatus.Approved;
            return Task.FromResult(Result<QuotationDto>.Ok(Dto(q)));
        }

        public Task<Result<IReadOnlyList<QuotationSummaryDto>>> GetQueueAsync(string? statusFilter, Guid? jobId = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<QuotationDto>> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<QuotationDto>> UpdateLineAsync(Guid id, Guid lineId, UpsertLineRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<QuotationDto>> RemoveLineAsync(Guid id, Guid lineId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<QuotationValidationDto>> ValidateAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<QuotationDto>> ReviseAsync(Guid id, ReviseQuotationRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<QuotationDto>> ApplyTemplateAsync(Guid id, ApplyTemplateRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class FakeAttachmentService(World world, ICurrentUser user) : IAttachmentService
    {
        public Task<Result<AttachmentDto>> UploadAsync(UploadAttachmentRequest request, CancellationToken ct = default)
        {
            world.Log("attachment.upload", user);
            if (world.UploadThrows) throw new IOException("FTP 501 Invalid number of parameters");
            world.Uploads.Add(request);
            return Task.FromResult(Result<AttachmentDto>.Ok(new AttachmentDto(
                Guid.NewGuid(), request.Kind, request.EntityId, request.FileName, request.ContentType, request.SizeBytes,
                $"sp1/{BranchId}/sig.png", "/api/v1/attachments/file", user.UserName, Now.UtcDateTime)));
        }

        public Task<Result<IReadOnlyList<AttachmentDto>>> GetForJobAsync(Guid jobId, string? kind, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<AttachmentFile>> OpenAsync(string relativePath, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
