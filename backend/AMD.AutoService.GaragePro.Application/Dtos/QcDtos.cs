using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record QcChecklistItemDto(
    Guid Id,
    Guid QuotationLineId,
    string CatalogCode,
    string Name,
    string Type,
    string Result,
    string? Note,
    DateTime? UpdatedAt,
    string? UpdatedByUserName);

public sealed record QcChecklistDto(
    Guid Id,
    Guid JobId,
    bool IsLocked,
    decimal? TestDriveKm,
    string? TestDriveNote,
    DateTime? TestDriveRecordedAt,
    string? TestDriveRecordedByUserName,
    DateTime? SubmittedAt,
    string? SubmittedByUserName,
    IReadOnlyList<QcChecklistItemDto> Items);

/// <summary>Result: "pending" | "pass" — ไม่มี "ไม่ผ่าน" ตามคำขอผู้ใช้ 2026-09-09</summary>
public sealed record SaveQcChecklistItemRequest(string Result, string? Note);

public sealed record SaveQcTestDriveRequest(decimal Km, string Note);

public static class QcMapper
{
    public static QcChecklistDto ToDto(QcChecklist checklist) => new(
        checklist.Id,
        checklist.JobId,
        checklist.IsLocked,
        checklist.TestDriveKm,
        checklist.TestDriveNote,
        checklist.TestDriveRecordedAt,
        checklist.TestDriveRecordedByUserName,
        checklist.SubmittedAt,
        checklist.SubmittedByUserName,
        checklist.Items.OrderBy(i => i.CatalogCode).Select(ToItemDto).ToList());

    public static QcChecklistItemDto ToItemDto(QcChecklistItem item) => new(
        item.Id, item.QuotationLineId, item.CatalogCode, item.Name, ToTypeToken(item.Type),
        ToResultToken(item.Result), item.Note, item.UpdatedAt, item.UpdatedByUserName);

    public static string ToResultToken(QcItemResult result) => result == QcItemResult.Pass ? "pass" : "pending";

    public static QcItemResult? ParseResultToken(string token) => token.Trim().ToLowerInvariant() switch
    {
        "pending" => QcItemResult.Pending,
        "pass" => QcItemResult.Pass,
        _ => null
    };

    public static string ToTypeToken(LineType type) => type == LineType.Labor ? "labor" : "part";
}
