using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class BranchDefaultsSeederTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 3, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Creates_three_categories_and_main_warehouse_for_an_empty_branch()
    {
        var repository = new MasterDataServiceTests.FakeMasterDataRepository();
        var seeder = new BranchDefaultsSeeder(repository, new FixedTime(Now));

        await seeder.EnsureAsync("sp1", 1001);

        Assert.Equal(["SVC", "PART", "FLUID"], repository.Categories.OrderBy(x => x.SortOrder).Select(x => x.Code));
        Assert.Equal(["บริการ/ค่าแรง", "อะไหล่", "น้ำมันและของเหลว"],
            repository.Categories.OrderBy(x => x.SortOrder).Select(x => x.Name));
        Assert.All(repository.Categories, x =>
        {
            Assert.Equal("sp1", x.LegacyShardKey);
            Assert.Equal(1001, x.LegacyBranchId);
            Assert.Null(x.ParentCategoryId);
            Assert.True(x.IsActive);
            Assert.Equal(Now.UtcDateTime, x.CreatedDate);
        });

        var warehouse = Assert.Single(repository.Warehouses);
        Assert.Equal(("MAIN", "คลังหลัก", "sp1", 1001), (warehouse.Code, warehouse.Name, warehouse.LegacyShardKey, warehouse.LegacyBranchId));
        Assert.True(warehouse.IsActive);

        // [BIZ] ทุกการสร้างข้อมูลหลักมี ActivityEvent พร้อม Source — ผู้กระทำคือ "ระบบ" ของสาขานั้น
        Assert.Equal(4, repository.Events.Count);
        Assert.All(repository.Events, x =>
        {
            Assert.Equal(EventSource.System, x.Source);
            Assert.Equal(0L, x.PerformedByUserId);
            Assert.Equal(BranchDefaultsSeeder.SystemActorName, x.PerformedByName);
            Assert.Contains("\"branchId\":1001", x.PayloadJson);
            Assert.Contains("\"shardKey\":\"sp1\"", x.PayloadJson);
        });
        Assert.Equal(3, repository.Events.Count(x => x.EventType == "category.created"));
        Assert.Single(repository.Events, x => x.EventType == "warehouse.created" && x.EntityId == warehouse.Id);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Second_call_is_a_no_op()
    {
        var repository = new MasterDataServiceTests.FakeMasterDataRepository();
        var seeder = new BranchDefaultsSeeder(repository, new FixedTime(Now));

        await seeder.EnsureAsync("sp1", 1001);
        await seeder.EnsureAsync("sp1", 1001);

        Assert.Equal(3, repository.Categories.Count);
        Assert.Single(repository.Warehouses);
        Assert.Equal(4, repository.Events.Count);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Respects_existing_rows_including_inactive_ones()
    {
        var repository = new MasterDataServiceTests.FakeMasterDataRepository();
        // ผู้จัดการปิดหมวดเดิมไปแล้ว — ห้ามเติมหมวดเริ่มต้นกลับมาเอง แต่คลังยังว่างจึงต้องได้คลังหลัก
        repository.Categories.Add(new CatalogCategory
        { Code = "OWN", Name = "หมวดของอู่", IsActive = false, LegacyShardKey = "db2", LegacyBranchId = 227 });
        var seeder = new BranchDefaultsSeeder(repository, new FixedTime(Now));

        await seeder.EnsureAsync("db2", 227);

        Assert.Equal("OWN", Assert.Single(repository.Categories).Code);
        Assert.Equal("MAIN", Assert.Single(repository.Warehouses).Code);
        Assert.Single(repository.Events);

        // สาขาที่มีครบทั้งสองกลุ่มแล้ว: ไม่มีอะไรเพิ่ม ไม่ save
        var saves = repository.SaveCount;
        await seeder.EnsureAsync("db2", 227);
        Assert.Equal(saves, repository.SaveCount);
        Assert.Single(repository.Warehouses);
    }

    [Fact]
    public async Task Does_not_look_at_or_touch_other_branches()
    {
        var repository = new MasterDataServiceTests.FakeMasterDataRepository();
        repository.Categories.Add(new CatalogCategory { Code = "SVC", Name = "บริการ", LegacyShardKey = "db2", LegacyBranchId = 227 });
        repository.Warehouses.Add(new Warehouse { Code = "MAIN", Name = "คลังหลัก", LegacyShardKey = "db2", LegacyBranchId = 227 });
        // สาขาเลขเดียวกันคนละชาร์ดเป็นคนละอู่ (Branch.Id ไม่ unique ข้าม shard)
        var seeder = new BranchDefaultsSeeder(repository, new FixedTime(Now));

        await seeder.EnsureAsync("sp1", 227);

        Assert.Equal(4, repository.Categories.Count);
        Assert.Equal(2, repository.Warehouses.Count);
        Assert.Equal(3, repository.Categories.Count(x => x.LegacyShardKey == "sp1"));
        Assert.Single(repository.Categories, x => x.LegacyShardKey == "db2");
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData(" ", 1)]
    [InlineData("sp1", 0)]
    [InlineData("sp1", -5)]
    public async Task Rejects_an_invalid_scope(string shardKey, int branchId)
    {
        var repository = new MasterDataServiceTests.FakeMasterDataRepository();
        var seeder = new BranchDefaultsSeeder(repository, new FixedTime(Now));

        await Assert.ThrowsAnyAsync<ArgumentException>(() => seeder.EnsureAsync(shardKey, branchId));
        Assert.Empty(repository.Categories);
        Assert.Empty(repository.Warehouses);
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
