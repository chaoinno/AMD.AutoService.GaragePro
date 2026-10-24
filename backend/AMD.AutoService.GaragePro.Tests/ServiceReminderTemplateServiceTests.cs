using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.ServiceReminders;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>รายการนัดบริการตั้งต้นของสาขา (2026-10-25) — ผู้จัดการจัดการ · ทุกบทบาทอ่าน · seed ชุดตั้งต้นครั้งเดียว</summary>
public sealed class ServiceReminderTemplateServiceTests
{
    [Fact]
    public async Task First_read_seeds_the_six_confirmed_defaults_in_order()
    {
        var (service, _) = Build(UserRole.FrontDesk); // ทุกบทบาทอ่านได้ และคนอ่านครั้งแรกคือผู้ที่ทำให้ seed เกิด

        var result = await service.SearchAsync(includeInactive: false);

        result.Success.Should().BeTrue();
        result.Data!.Select(t => (t.Name, t.DefaultMileageInterval, t.DefaultMonths)).Should().Equal(
            ("น้ำมันเครื่อง", 10_000, 6),
            ("กรองน้ำมันเครื่อง", 10_000, 6),
            ("น้ำมันเกียร์", 40_000, 24),
            ("น้ำมันเบรก", 40_000, 24),
            ("กรองอากาศ", 20_000, 12),
            ("เช็คระยะ", 10_000, 6));
        result.Data.Select(t => t.SortOrder).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Seeding_happens_once_even_after_every_template_is_deactivated()
    {
        var (service, repo) = Build(UserRole.Manager);
        var first = (await service.SearchAsync(false)).Data!;
        foreach (var t in first) await service.SetStatusAsync(t.Id, new MasterDataStatusRequest(false));

        var again = await service.SearchAsync(includeInactive: false);

        again.Data.Should().BeEmpty(); // ผู้จัดการปิดไว้เองทั้งหมด — ไม่ถูกเติมชุดตั้งต้นซ้ำ
        repo.Templates.Should().HaveCount(6);
    }

    [Fact]
    public async Task Seeding_does_not_copy_across_branches()
    {
        var (service, repo) = Build(UserRole.Manager);
        repo.Templates.Add(new ServiceReminderTemplate
        {
            LegacyShardKey = "db2", LegacyBranchId = 999, Name = "ของสาขาอื่น", DefaultMileageInterval = 1, DefaultMonths = 1
        });

        var result = await service.SearchAsync(false);

        result.Data!.Should().HaveCount(6).And.NotContain(t => t.Name == "ของสาขาอื่น");
    }

    [Fact]
    public async Task Losing_a_concurrent_seed_race_is_not_an_error()
    {
        var (service, repo) = Build(UserRole.Office);
        repo.ConflictOnNextSave = true; // อีกคำขอหนึ่ง seed ชนะไปแล้ว

        var result = await service.SearchAsync(false);

        result.Success.Should().BeTrue();
        repo.DiscardCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData(UserRole.Office)]
    [InlineData(UserRole.FrontDesk)]
    [InlineData(UserRole.Technician)]
    [InlineData(UserRole.Lead)]
    [InlineData(UserRole.Cashier)]
    public async Task Only_the_manager_may_create_update_or_change_status(UserRole role)
    {
        var (service, repo) = Build(role);
        repo.Templates.Add(Existing("น้ำมันเครื่อง"));
        var id = repo.Templates[0].Id;
        var request = new ServiceReminderTemplateUpsertRequest("ใหม่", 5_000, 3);

        (await service.CreateAsync(request)).Error!.Code.Should().Be("MASTER_DATA_MANAGE_FORBIDDEN");
        (await service.UpdateAsync(id, request)).Error!.Code.Should().Be("MASTER_DATA_MANAGE_FORBIDDEN");
        (await service.SetStatusAsync(id, new MasterDataStatusRequest(false))).Error!.Code
            .Should().Be("MASTER_DATA_MANAGE_FORBIDDEN");
        repo.Templates.Should().ContainSingle();
    }

    [Fact]
    public async Task Manager_creates_with_trimmed_name_and_it_appears_in_the_list()
    {
        var (service, _) = Build(UserRole.Manager);

        var created = await service.CreateAsync(new ServiceReminderTemplateUpsertRequest("  ลูกปืนล้อ  ", 60_000, 24, 50));

        created.Success.Should().BeTrue();
        created.Data!.Name.Should().Be("ลูกปืนล้อ");
        var list = (await service.SearchAsync(false)).Data!;
        list.Should().HaveCount(7); // ชุดตั้งต้น 6 + ที่เพิ่ม — สร้างก่อนใครเปิดรายการก็ต้องได้ชุดตั้งต้นด้วย
        list.Last().Name.Should().Be("ลูกปืนล้อ");
    }

    [Fact]
    public async Task Duplicate_name_is_rejected_on_create_and_update()
    {
        var (service, repo) = Build(UserRole.Manager);
        var seeded = (await service.SearchAsync(false)).Data!;

        var create = await service.CreateAsync(new ServiceReminderTemplateUpsertRequest("น้ำมันเบรก", 1_000, 1));
        create.Error!.Code.Should().Be("SERVICE_TEMPLATE_NAME_DUPLICATE");

        var update = await service.UpdateAsync(
            seeded.First(t => t.Name == "กรองอากาศ").Id, new ServiceReminderTemplateUpsertRequest("น้ำมันเบรก", 1_000, 1));
        update.Error!.Code.Should().Be("SERVICE_TEMPLATE_NAME_DUPLICATE");

        // ใช้ชื่อเดิมของตัวเองได้
        var self = seeded.First(t => t.Name == "กรองอากาศ");
        (await service.UpdateAsync(self.Id, new ServiceReminderTemplateUpsertRequest("กรองอากาศ", 25_000, 12)))
            .Success.Should().BeTrue();
        repo.Templates.First(t => t.Id == self.Id).DefaultMileageInterval.Should().Be(25_000);
    }

    [Theory]
    [InlineData("", 10_000, 6, "name")]
    [InlineData("   ", 10_000, 6, "name")]
    [InlineData("ok", 0, 6, "defaultMileageInterval")]
    [InlineData("ok", 10_000_000, 6, "defaultMileageInterval")]
    [InlineData("ok", 10_000, 0, "defaultMonths")]
    [InlineData("ok", 10_000, 25, "defaultMonths")]
    public async Task Create_validates_name_mileage_and_months(string name, int mileage, int months, string field)
    {
        var (service, _) = Build(UserRole.Manager);

        var result = await service.CreateAsync(new ServiceReminderTemplateUpsertRequest(name, mileage, months));

        result.Error!.Code.Should().Be("SERVICE_TEMPLATE_VALIDATION");
        result.Error.Field.Should().Be(field);
    }

    [Fact]
    public async Task Inactive_templates_are_hidden_unless_asked_for()
    {
        var (service, _) = Build(UserRole.Manager);
        var seeded = (await service.SearchAsync(false)).Data!;
        await service.SetStatusAsync(seeded[0].Id, new MasterDataStatusRequest(false));

        (await service.SearchAsync(false)).Data.Should().HaveCount(5);
        (await service.SearchAsync(true)).Data.Should().HaveCount(6);
    }

    [Fact]
    public async Task Update_of_an_unknown_id_is_not_found()
    {
        var (service, _) = Build(UserRole.Manager);

        var result = await service.UpdateAsync(Guid.NewGuid(), new ServiceReminderTemplateUpsertRequest("x", 1, 1));

        result.Error!.Code.Should().Be("SERVICE_TEMPLATE_NOT_FOUND");
    }

    // ---------- helpers ----------

    private static ServiceReminderTemplate Existing(string name) => new()
    {
        LegacyShardKey = "db2", LegacyBranchId = 105, Name = name, DefaultMileageInterval = 10_000, DefaultMonths = 6
    };

    private static (ServiceReminderTemplateService Service, FakeRepository Repo) Build(UserRole role)
    {
        var repo = new FakeRepository();
        return (new ServiceReminderTemplateService(repo, new StubCurrentUser(role), TimeProvider.System), repo);
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

    private sealed class FakeRepository : IServiceReminderTemplateRepository
    {
        public List<ServiceReminderTemplate> Templates { get; } = [];
        public bool ConflictOnNextSave { get; set; }
        public bool DiscardCalled { get; private set; }
        private readonly List<ServiceReminderTemplate> _pending = [];

        private IEnumerable<ServiceReminderTemplate> Scope(string shard, int branch) =>
            Templates.Where(t => t.LegacyShardKey == shard && t.LegacyBranchId == branch);

        public Task<bool> AnyAsync(string shardKey, int branchId, CancellationToken ct = default) =>
            Task.FromResult(Scope(shardKey, branchId).Any());

        public Task<IReadOnlyList<ServiceReminderTemplate>> SearchAsync(
            string shardKey, int branchId, bool includeInactive, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ServiceReminderTemplate>>(Scope(shardKey, branchId)
                .Where(t => includeInactive || t.IsActive)
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToList());

        public Task<ServiceReminderTemplate?> GetAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Scope(shardKey, branchId).FirstOrDefault(t => t.Id == id));

        public Task<bool> NameExistsAsync(
            string shardKey, int branchId, string name, Guid? excludingId, CancellationToken ct = default) =>
            Task.FromResult(Scope(shardKey, branchId).Any(t =>
                string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase) && (excludingId == null || t.Id != excludingId)));

        public Task AddAsync(ServiceReminderTemplate template, CancellationToken ct = default)
        {
            _pending.Add(template);
            return Task.CompletedTask;
        }

        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            if (ConflictOnNextSave)
            {
                ConflictOnNextSave = false;
                throw new MasterDataConflictException("UX_svc_ServiceReminderTemplate_Name", new Exception("dup"));
            }
            var n = _pending.Count;
            Templates.AddRange(_pending);
            _pending.Clear();
            return Task.FromResult(n);
        }

        public void DiscardPending()
        {
            DiscardCalled = true;
            _pending.Clear();
        }
    }
}
