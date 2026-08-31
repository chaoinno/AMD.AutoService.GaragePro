using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record IntakeChecklistTemplateItemDto(
    string CategoryKey,
    string CategoryLabelTh,
    string ItemCode,
    string LabelTh,
    string? HintTh);

public sealed record IntakeChecklistItemDto(
    Guid Id,
    string ItemCode,
    string CategoryKey,
    string LabelTh,
    string? HintTh,
    string Result,
    string? Note,
    DateTime? UpdatedAt,
    string? UpdatedByUserName);

public sealed record IntakeChecklistDto(
    Guid Id,
    Guid JobId,
    bool IsLocked,
    DateTime? SubmittedAt,
    string? SubmittedByUserName,
    IReadOnlyList<IntakeChecklistItemDto> Items);

public sealed record SaveIntakeChecklistItemRequest(string Result, string? Note);

public sealed record SubmitIntakeChecklistResultDto(Guid Id, DateTime SubmittedAt);

public static class IntakeMapper
{
    public static IReadOnlyList<IntakeChecklistTemplateItemDto> ToTemplateDto() =>
        IntakeChecklistTemplate.Items
            .Select(i => new IntakeChecklistTemplateItemDto(i.CategoryKey, i.CategoryLabelTh, i.ItemCode, i.LabelTh, i.HintTh))
            .ToList();

    public static IntakeChecklistDto ToDto(IntakeChecklist checklist)
    {
        var byCode = checklist.Items.ToDictionary(i => i.ItemCode);

        var items = IntakeChecklistTemplate.Items.Select(template =>
        {
            byCode.TryGetValue(template.ItemCode, out var item);
            return new IntakeChecklistItemDto(
                item?.Id ?? Guid.Empty,
                template.ItemCode,
                template.CategoryKey,
                template.LabelTh,
                template.HintTh,
                ToToken(item?.Result ?? IntakeCheckResult.Pending),
                item?.Note,
                item?.UpdatedAt,
                item?.UpdatedByUserName);
        }).ToList();

        return new IntakeChecklistDto(
            checklist.Id,
            checklist.JobId,
            checklist.IsLocked,
            checklist.SubmittedAt,
            checklist.SubmittedByUserName,
            items);
    }

    public static string ToToken(IntakeCheckResult result) => result switch
    {
        IntakeCheckResult.Ok => "ok",
        IntakeCheckResult.Issue => "issue",
        IntakeCheckResult.NotApplicable => "na",
        _ => "pending"
    };

    public static IntakeCheckResult? ParseToken(string token) => token switch
    {
        "pending" => IntakeCheckResult.Pending,
        "ok" => IntakeCheckResult.Ok,
        "issue" => IntakeCheckResult.Issue,
        "na" => IntakeCheckResult.NotApplicable,
        _ => null
    };
}
