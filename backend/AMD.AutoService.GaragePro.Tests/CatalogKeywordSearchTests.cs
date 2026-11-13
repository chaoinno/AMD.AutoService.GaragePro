using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>ค้นหาสินค้าหลายคำ (หน้าจัดการสินค้า + สต็อก FIFO) — ทุกคำต้องเจอ ไม่ต้องติดกัน</summary>
public class CatalogKeywordSearchTests
{
    private static readonly CatalogItem[] Items =
    [
        Item("P-001", "ไส้กรองน้ำมันเครื่อง ท่อสั้น", "Toyota Vios"),
        Item("P-002", "ไส้กรองอากาศ", "Honda City"),
        Item("P-003", "ท่อน้ำมันเครื่อง", null),
        Item("FLT-9", "กรองแอร์ 50%", "Toyota Yaris"),
    ];

    private static CatalogItem Item(string code, string name, string? compatibility) => new()
    {
        Code = code, Name = name, Compatibility = compatibility, Unit = "ชิ้น", Type = LineType.Part,
        LegacyShardKey = "db2", LegacyBranchId = 105,
    };

    private static string[] Search(string? keyword, bool includeCompatibility = true) =>
        Items.AsQueryable().WhereMatchesKeywords(keyword, includeCompatibility).Select(x => x.Code).ToArray();

    [Fact]
    public void Every_word_must_match_but_not_next_to_each_other_or_in_order()
    {
        Assert.Equal(["P-001"], Search("กรอง ท่อ เครื่อง"));
        Assert.Equal(["P-001"], Search("เครื่อง กรอง ท่อ"));
        // คำเดียวยังเหมือนเดิม
        Assert.Equal(["P-001", "P-002", "FLT-9"], Search("กรอง"));
    }

    [Fact]
    public void Each_word_may_match_a_different_field()
    {
        // รหัส + ชื่อ
        Assert.Equal(["P-003"], Search("P-003 ท่อ"));
        // ชื่อ + รุ่นรถ — เฉพาะหน้าที่ค้นรุ่นรถด้วย (หน้าจัดการสินค้า)
        Assert.Equal(["P-001", "FLT-9"], Search("กรอง Toyota"));
        Assert.Empty(Search("กรอง Toyota", includeCompatibility: false));
    }

    [Fact]
    public void Extra_spaces_duplicates_and_blank_keyword_are_ignored()
    {
        Assert.Equal(["P-001"], Search("  กรอง\tท่อ   กรอง  "));
        Assert.Equal(Items.Length, Search("   ").Length);
        Assert.Equal(Items.Length, Search(null).Length);
        Assert.Equal(["กรอง", "ท่อ"], CatalogKeywordSearch.Terms(" กรอง  ท่อ กรอง "));
    }

    [Fact]
    public void Word_count_is_capped()
    {
        var keyword = string.Join(' ', Enumerable.Range(1, 15).Select(i => $"w{i}"));
        Assert.Equal(CatalogKeywordSearch.MaxTerms, CatalogKeywordSearch.Terms(keyword).Count);
    }

    [Fact]
    public void Translates_to_sql_server_with_one_condition_per_word()
    {
        using var db = new ServiceDbContext(new DbContextOptionsBuilder<ServiceDbContext>()
            .UseSqlServer("Server=offline;Database=offline").Options);

        var sql = db.CatalogItems.WhereMatchesKeywords("กรอง ท่อ 50%", includeCompatibility: true).ToQueryString();

        // คำละ 1 วงเล็บ (รหัส OR ชื่อ OR รุ่นรถ) ต่อกันด้วย AND · % ที่ผู้ใช้พิมพ์ถูก escape ไม่กลายเป็น wildcard
        Assert.Equal(3, sql.Split(") AND (", StringSplitOptions.None).Length);
        Assert.Contains("[Compatibility] LIKE", sql);
        Assert.Contains(@"N'%50\%%'", sql);
        Assert.Contains(@"ESCAPE N'\'", sql);
    }
}
