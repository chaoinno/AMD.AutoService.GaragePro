using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.QuotationTemplates;

/// <summary>[BIZ] invariant #7 — ต้นทุน strip ที่ serializer ตาม role เท่านั้น ไม่ใช่ที่เก็บในฐาน
/// (ต่างจากบรรทัด ad-hoc ที่ไม่มีสิทธิ์เห็นต้นทุนบังคับเก็บเป็น 0 ทันที — ที่นี่ต้นทุนมาจากผู้จัดการ ไม่ใช่ client
/// ที่เชื่อไม่ได้ จึงเก็บค่าจริงเสมอแล้ว strip ตอนอ่านแทน — ดู docs/08-quotation-template.md)</summary>
public static class QuotationTemplateMapper
{
    public static QuotationTemplateDto ToDto(
        QuotationTemplate t, bool canSeeCost,
        IReadOnlyDictionary<string, CatalogItem>? catalogByCode = null, bool includeCatalogStatus = false)
    {
        var lines = t.Lines
            .OrderBy(l => l.Sequence)
            .Select(l => ToLineDto(l, canSeeCost, catalogByCode, includeCatalogStatus))
            .ToList();

        return new QuotationTemplateDto(
            Id: t.Id, Code: t.Code, Name: t.Name, Description: t.Description, IsActive: t.IsActive,
            LineCount: t.Lines.Count,
            PartCount: t.Lines.Count(l => l.Type == Domain.Enums.LineType.Part),
            LaborCount: t.Lines.Count(l => l.Type == Domain.Enums.LineType.Labor),
            Lines: lines,
            CreatedDate: t.CreatedDate, LastUpdated: t.LastUpdated);
    }

    private static QuotationTemplateLineDto ToLineDto(
        QuotationTemplateLine l, bool canSeeCost,
        IReadOnlyDictionary<string, CatalogItem>? catalogByCode, bool includeCatalogStatus)
    {
        var isAdHoc = string.IsNullOrWhiteSpace(l.CatalogCode);

        bool? resolved = null, active = null;
        decimal? catalogPrice = null;
        string? catalogName = null;

        if (includeCatalogStatus && !isAdHoc)
        {
            var found = catalogByCode?.GetValueOrDefault(l.CatalogCode);
            resolved = found is not null;
            active = found?.IsActive;
            catalogPrice = found?.Price;
            catalogName = found?.Name;
        }

        return new QuotationTemplateLineDto(
            Id: l.Id, Sequence: l.Sequence, CatalogCode: l.CatalogCode, IsAdHoc: isAdHoc,
            Name: l.Name, Type: l.Type.ToString().ToLowerInvariant(), Unit: l.Unit,
            Quantity: l.Quantity, UnitPrice: l.UnitPrice,
            UnitCost: canSeeCost ? l.UnitCost : null,
            StandardHours: l.StandardHours, DiscountPercent: l.DiscountPercent,
            Promotion: (int)l.Promotion, Source: l.Source.ToString().ToLowerInvariant(), Note: l.Note,
            CatalogResolved: resolved, CatalogActive: active, CatalogPrice: catalogPrice, CatalogName: catalogName);
    }
}
