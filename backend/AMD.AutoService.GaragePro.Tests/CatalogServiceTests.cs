using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Catalog;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class CatalogServiceTests
{
    [Fact]
    public void Labor_requires_standard_hours()
    {
        var error = CatalogValidator.Validate(ValidInput() with { Type = LineType.Labor, StandardHours = null });
        Assert.Equal("CATALOG_HOURS_REQUIRED", error?.Code);
    }

    [Fact]
    public void Negative_stock_is_rejected()
    {
        var error = CatalogValidator.Validate(ValidInput() with { OnHand = -1 });
        Assert.Equal("CATALOG_STOCK_INVALID", error?.Code);
    }

    [Fact]
    public async Task Manager_create_normalizes_code_and_tenant_scope()
    {
        var repository = new StubRepository();
        var service = new CatalogService(repository, new StubUser(UserRole.Manager));

        var result = await service.CreateAsync(ValidInput() with { Code = " p-oil-1 " });

        Assert.True(result.Success);
        Assert.Equal("P-OIL-1", repository.Added?.Code);
        Assert.Equal("db2", repository.Added?.LegacyShardKey);
        Assert.Equal(105, repository.Added?.LegacyBranchId);
        Assert.Single(repository.Events);
    }

    [Fact]
    public async Task Non_manager_cannot_change_catalog()
    {
        var repository = new StubRepository();
        var service = new CatalogService(repository, new StubUser(UserRole.Office));

        var result = await service.CreateAsync(ValidInput());

        Assert.False(result.Success);
        Assert.Equal("CATALOG_MANAGE_FORBIDDEN", result.Error?.Code);
        Assert.Null(repository.Added);
    }

    [Fact]
    public async Task Cost_is_removed_for_non_manager()
    {
        var repository = new StubRepository { Existing = Entity() };
        var service = new CatalogService(repository, new StubUser(UserRole.Office));

        var result = await service.GetAsync(repository.Existing.Id);

        Assert.True(result.Success);
        Assert.Null(result.Data?.Cost);
    }

    private static CatalogUpsertRequest ValidInput() => new(
        "P-OIL-1", LineType.Part, "น้ำมันเครื่อง", "ใช้ได้ทั่วไป", "ขวด",
        100m, 150m, null, 10, 2, 3, 0, "รับของวันพรุ่งนี้");

    private static CatalogItem Entity() => new()
    {
        Code = "P-OIL-1", Type = LineType.Part, Name = "น้ำมันเครื่อง", Unit = "ขวด",
        Cost = 100m, Price = 150m, OnHand = 10, Reserved = 2,
        LegacyShardKey = "db2", LegacyBranchId = 105
    };

    private sealed class StubUser(UserRole role) : ICurrentUser
    {
        public long UserId => 7;
        public string UserName => "ผู้ทดสอบ";
        public UserRole Role => role;
        public string ShardKey => "db2";
        public int BranchId => 105;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => role == UserRole.Manager;
        public Guid? SessionId => null;
    }

    private sealed class StubRepository : ICatalogRepository
    {
        public CatalogItem? Existing { get; set; }
        public CatalogItem? Added { get; private set; }
        public List<ActivityEvent> Events { get; } = [];

        public Task<(IReadOnlyList<CatalogItem> Items, int Total)> SearchManagementAsync(
            string shardKey, int branchId, CatalogManagementQuery query, CancellationToken ct = default)
        {
            IReadOnlyList<CatalogItem> items = Existing is null ? [] : [Existing];
            return Task.FromResult((items, items.Count));
        }

        public Task<CatalogItem?> GetAsync(string shardKey, int branchId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(Existing?.Id == id ? Existing : null);

        public Task<bool> CodeExistsAsync(string shardKey, int branchId, string code, Guid? excludingId,
            CancellationToken ct = default) => Task.FromResult(false);

        public Task AddAsync(CatalogItem item, CancellationToken ct = default)
        {
            Added = item;
            Existing = item;
            return Task.CompletedTask;
        }

        public Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default)
        {
            Events.Add(activityEvent);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
        public Task<IReadOnlyList<CatalogItem>> SearchAsync(string shardKey, int branchId, string? keyword,
            CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CatalogItem>> GetByCodesAsync(string shardKey, int branchId,
            IEnumerable<string> codes, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
