using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record QuotationTemplateDto(
    Guid Id, string Code, string Name, string? Description, bool IsActive,
    int LineCount, int PartCount, int LaborCount,
    IReadOnlyList<QuotationTemplateLineDto> Lines,
    DateTime CreatedDate, DateTime LastUpdated);

public sealed record QuotationTemplateLineDto(
    Guid Id, int Sequence, string CatalogCode, bool IsAdHoc,
    string Name, string Type, string? Unit,
    decimal Quantity, decimal? UnitPrice, decimal? UnitCost, decimal? StandardHours,
    decimal DiscountPercent, int Promotion, string Source, string? Note,
    // ---- สถานะของรหัสแคตตาล็อก ณ ตอนนี้ (คำนวณสด ไม่เก็บลงฐาน) — ใช้เฉพาะตอนอ่านรายละเอียดเทมเพลต ----
    bool? CatalogResolved, bool? CatalogActive, decimal? CatalogPrice, string? CatalogName);

public sealed record QuotationTemplateUpsertRequest(
    string Code, string Name, string? Description,
    IReadOnlyList<QuotationTemplateLineInput> Lines);

public sealed record QuotationTemplateLineInput(
    string CatalogCode, string? Name, LineType? Type, string? Unit,
    decimal Quantity, decimal? UnitPrice, decimal? UnitCost, decimal? StandardHours,
    decimal DiscountPercent, PromotionKind Promotion, LineSource Source, string? Note);

/// <summary>ใช้กับ POST /api/v1/quotations/{id}/lines/from-template
/// source = null → แต่ละบรรทัดใช้ Source ที่เก็บไว้ในเทมเพลตเอง (ผสม ลูกค้าขอ/ช่างแนะนำ ได้) · มีค่า → override ทุกบรรทัด</summary>
public sealed record ApplyTemplateRequest(Guid TemplateId, LineSource? Source);
