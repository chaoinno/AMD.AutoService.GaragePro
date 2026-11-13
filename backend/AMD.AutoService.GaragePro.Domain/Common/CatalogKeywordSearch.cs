using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Domain.Common;

/// <summary>
/// ค้นหาสินค้าแบบหลายคำ — แหล่งเดียวของหน้าจัดการสินค้าและหน้าสต็อก FIFO
/// [BIZ] (คำขอผู้ใช้ 2026-11-13) พิมพ์ "กรอง ท่อ เครื่อง" ต้องเจอสินค้าที่มี<b>ทุกคำ</b> ไม่จำเป็นต้องติดกันหรือเรียงตามที่พิมพ์
/// · แต่ละคำเจอในช่องไหนก็ได้ (รหัส/ชื่อ/รุ่นรถ) · ไม่สนตัวพิมพ์เล็กใหญ่ตาม collation ของฐาน (Thai_CI_AS)
/// · ใช้ string.Contains ให้ EF แปลเป็น SQL ที่ escape % และ _ ให้เอง (ของเดิมต่อ LIKE ตรงๆ พิมพ์ % แล้วเจอทุกแถว)
/// </summary>
public static class CatalogKeywordSearch
{
    /// <summary>
    /// เพดานจำนวนคำ — กัน SQL ยาวผิดปกติ (แต่ละคำเพิ่มเงื่อนไข 2–3 ช่อง) · คำที่เกินถูกตัดทิ้ง ผลจึงกว้างขึ้นเท่านั้น ไม่หายไป
    /// </summary>
    public const int MaxTerms = 10;

    /// <summary>แยกคำด้วยช่องว่างทุกชนิด ตัดคำว่างและคำซ้ำ (ไม่สนตัวพิมพ์) · ไม่มีคำ = ไม่กรอง</summary>
    public static IReadOnlyList<string> Terms(string? keyword) =>
        string.IsNullOrWhiteSpace(keyword)
            ? []
            : keyword.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxTerms)
                .ToArray();

    /// <summary>
    /// ทุกคำต้องเจอ (AND) · แต่ละคำเจอในรหัสหรือชื่อ (และรุ่นรถเมื่อ <paramref name="includeCompatibility"/>) ก็พอ (OR)
    /// </summary>
    public static IQueryable<CatalogItem> WhereMatchesKeywords(
        this IQueryable<CatalogItem> source, string? keyword, bool includeCompatibility)
    {
        foreach (var term in Terms(keyword))
        {
            source = includeCompatibility
                ? source.Where(c => c.Code.Contains(term) || c.Name.Contains(term)
                    || (c.Compatibility != null && c.Compatibility.Contains(term)))
                : source.Where(c => c.Code.Contains(term) || c.Name.Contains(term));
        }
        return source;
    }
}
