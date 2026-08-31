using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Intake;

public interface IIntakeChecklistService
{
    IReadOnlyList<IntakeChecklistTemplateItemDto> GetTemplate();

    Task<Result<IntakeChecklistDto>> GetOrCreateAsync(Guid jobId, CancellationToken ct = default);

    Task<Result<IntakeChecklistItemDto>> SaveItemAsync(
        Guid jobId, string itemCode, SaveIntakeChecklistItemRequest request, CancellationToken ct = default);

    Task<Result<SubmitIntakeChecklistResultDto>> SubmitAsync(Guid jobId, CancellationToken ct = default);
}

/// <summary>
/// Checklist สภาพรถขณะรับ (ขั้นที่ 3 ของ "รับรถ 6 ขั้น") — docs/01-workflow.md §3.1
/// [BIZ] ล็อกเมื่อส่งแล้ว · ทุกรายการต้องตอบก่อนส่ง · Issue/NA ต้องมีหมายเหตุ
/// </summary>
public sealed class IntakeChecklistService(
    IIntakeChecklistRepository repository,
    IJobRepository jobs,
    ICurrentUser user,
    TimeProvider clock) : IIntakeChecklistService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public IReadOnlyList<IntakeChecklistTemplateItemDto> GetTemplate() => IntakeMapper.ToTemplateDto();

    public async Task<Result<IntakeChecklistDto>> GetOrCreateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobResult = await ValidateJobAsync(jobId, ct);
        if (!jobResult.Success) return Result<IntakeChecklistDto>.Fail(jobResult.Error!);

        var checklist = await repository.GetByJobAsync(jobId, ct);
        checklist ??= await CreateDraftAsync(jobId, ct);

        return Result<IntakeChecklistDto>.Ok(IntakeMapper.ToDto(checklist));
    }

    public async Task<Result<IntakeChecklistItemDto>> SaveItemAsync(
        Guid jobId, string itemCode, SaveIntakeChecklistItemRequest request, CancellationToken ct = default)
    {
        if (!IntakeChecklistTemplate.IsValidItemCode(itemCode))
            return Result<IntakeChecklistItemDto>.Fail(
                "INTAKE_ITEM_UNKNOWN", $"ไม่รู้จักรายการตรวจ '{itemCode}'", nameof(itemCode));

        var result = IntakeMapper.ParseToken(request.Result);
        if (result is null || result == IntakeCheckResult.Pending)
            return Result<IntakeChecklistItemDto>.Fail(
                "INTAKE_RESULT_INVALID", "ผลตรวจไม่ถูกต้อง — ต้องเป็น ok / issue / na", nameof(request.Result));

        if (result is IntakeCheckResult.Issue or IntakeCheckResult.NotApplicable
            && string.IsNullOrWhiteSpace(request.Note))
            return Result<IntakeChecklistItemDto>.Fail(
                "INTAKE_NOTE_REQUIRED", "ต้องระบุหมายเหตุเมื่อผลตรวจเป็น \"พบปัญหา\" หรือ \"ไม่เกี่ยวข้อง\"", nameof(request.Note));

        var jobResult = await ValidateJobAsync(jobId, ct);
        if (!jobResult.Success) return Result<IntakeChecklistItemDto>.Fail(jobResult.Error!);

        var checklist = await repository.GetByJobAsync(jobId, ct);
        checklist ??= await CreateDraftAsync(jobId, ct);

        if (checklist.IsLocked)
            return Result<IntakeChecklistItemDto>.Fail(
                "INTAKE_LOCKED", "ส่ง checklist นี้ไปแล้ว — แก้ไขไม่ได้");

        var item = checklist.Items.First(i => i.ItemCode == itemCode);
        item.Result = result.Value;
        item.Note = request.Note?.Trim();
        item.UpdatedAt = Now;
        item.UpdatedByUserId = user.UserId;
        item.UpdatedByUserName = user.UserName;

        await repository.SaveChangesAsync(ct);

        var template = IntakeChecklistTemplate.Items.First(t => t.ItemCode == itemCode);
        return Result<IntakeChecklistItemDto>.Ok(new IntakeChecklistItemDto(
            item.Id, item.ItemCode, item.CategoryKey, template.LabelTh, template.HintTh,
            IntakeMapper.ToToken(item.Result), item.Note, item.UpdatedAt, item.UpdatedByUserName));
    }

    public async Task<Result<SubmitIntakeChecklistResultDto>> SubmitAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobResult = await ValidateJobAsync(jobId, ct);
        if (!jobResult.Success) return Result<SubmitIntakeChecklistResultDto>.Fail(jobResult.Error!);

        var checklist = await repository.GetByJobAsync(jobId, ct);
        checklist ??= await CreateDraftAsync(jobId, ct);

        if (checklist.IsLocked)
            return Result<SubmitIntakeChecklistResultDto>.Fail(
                "INTAKE_LOCKED", "ส่ง checklist นี้ไปแล้ว");

        var pending = checklist.Items.Where(i => i.Result == IntakeCheckResult.Pending).ToList();
        if (pending.Count > 0)
            return Result<SubmitIntakeChecklistResultDto>.Fail(
                "INTAKE_INCOMPLETE", $"ยังตรวจไม่ครบ — เหลืออีก {pending.Count} รายการ");

        checklist.SubmittedAt = Now;
        checklist.SubmittedByUserId = user.UserId;
        checklist.SubmittedByUserName = user.UserName;

        var issueCount = checklist.Items.Count(i => i.Result == IntakeCheckResult.Issue);

        await repository.AddEventAsync(new ActivityEvent
        {
            JobId = checklist.JobId,
            EntityId = checklist.Id,
            EntityType = nameof(IntakeChecklist),
            EventType = "intake_checklist.submitted",
            DescriptionTh = issueCount > 0
                ? $"ส่ง checklist สภาพรถขณะรับ — พบสภาพที่ต้องบันทึกไว้ {issueCount} รายการ"
                : "ส่ง checklist สภาพรถขณะรับ — ทุกรายการปกติ",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);

        await repository.SaveChangesAsync(ct);

        return Result<SubmitIntakeChecklistResultDto>.Ok(
            new SubmitIntakeChecklistResultDto(checklist.Id, checklist.SubmittedAt.Value));
    }

    private async Task<Result<bool>> ValidateJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return Result<bool>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<bool>.Fail("JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        return Result<bool>.Ok(true);
    }

    private async Task<IntakeChecklist> CreateDraftAsync(Guid jobId, CancellationToken ct)
    {
        var checklist = new IntakeChecklist
        {
            JobId = jobId,
            CreatedByUserId = user.UserId,
            CreatedByUserName = user.UserName,
            CreatedAt = Now
        };

        checklist.Items = IntakeChecklistTemplate.Items.Select(t => new IntakeChecklistItem
        {
            IntakeChecklistId = checklist.Id,
            CategoryKey = t.CategoryKey,
            ItemCode = t.ItemCode,
            Result = IntakeCheckResult.Pending
        }).ToList();

        await repository.AddAsync(checklist, ct);
        await repository.SaveChangesAsync(ct);

        return checklist;
    }
}
