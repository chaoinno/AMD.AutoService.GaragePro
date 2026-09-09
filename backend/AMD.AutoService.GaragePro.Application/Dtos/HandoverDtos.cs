using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record HandoverChecklistItemDto(
    Guid Id,
    string ItemCode,
    string Name,
    bool IsReturned,
    string? Note,
    DateTime? UpdatedAt,
    string? UpdatedByUserName);

public sealed record HandoverDto(
    Guid Id,
    Guid JobId,
    bool IsLocked,
    string? SignatureImagePath,
    DateTime? SubmittedAt,
    string? SubmittedByUserName,
    IReadOnlyList<HandoverChecklistItemDto> Items);

/// <summary>[BIZ] Note บังคับเมื่อ IsReturned = false</summary>
public sealed record SaveHandoverItemRequest(bool IsReturned, string? Note);

public sealed record SubmitHandoverRequest(string SignatureAttachmentPath);

public static class HandoverMapper
{
    public static HandoverDto ToDto(HandoverRecord record) => new(
        record.Id,
        record.JobId,
        record.IsLocked,
        record.SignatureImagePath,
        record.SubmittedAt,
        record.SubmittedByUserName,
        record.Items.OrderBy(i => i.ItemCode).Select(ToItemDto).ToList());

    public static HandoverChecklistItemDto ToItemDto(HandoverChecklistItem item) => new(
        item.Id, item.ItemCode, item.Name, item.IsReturned, item.Note, item.UpdatedAt, item.UpdatedByUserName);
}
