using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Handover;

public interface IHandoverService
{
    Task<Result<HandoverDto>> GetOrCreateAsync(Guid jobId, CancellationToken ct = default);

    Task<Result<HandoverChecklistItemDto>> SaveItemAsync(
        Guid jobId, Guid itemId, SaveHandoverItemRequest request, CancellationToken ct = default);

    Task<Result<HandoverDto>> SubmitAsync(
        Guid jobId, SubmitHandoverRequest request, CancellationToken ct = default);
}

/// <summary>
/// [ASSUME] ยืนยันส่งมอบรถชั่วคราวบนเว็บ (Cashier/Office/Manager ยืนยันแทนลูกค้า) — เพราะ /handover/:jobId
/// บนมือถือจริงยังไม่ได้ออกแบบ (docs/01-workflow.md §11 [GAP·สูง], Phase 8) รายการเช็คลิสต์เป็น const list คงที่
/// ไม่ใช่ template — ต้องแทนที่ด้วย flow มือถือจริงในอนาคต
/// </summary>
public sealed class HandoverService(
    IHandoverRepository repository,
    IJobRepository jobs,
    ICurrentUser user,
    TimeProvider clock) : IHandoverService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static readonly (string Code, string Name)[] DefaultItems =
    [
        ("key", "กุญแจรถ (รวมกุญแจสำรองถ้ามี)"),
        ("manual", "คู่มือ/เอกสารประจำรถ"),
        ("spare-tire", "ยางอะไหล่และแม่แรง"),
        ("belongings", "ของใช้ส่วนตัวของลูกค้า"),
        ("accessory", "อุปกรณ์เสริมที่ฝากไว้ (ถ้ามี)")
    ];

    public async Task<Result<HandoverDto>> GetOrCreateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<HandoverDto>.Fail(jobResult.Error!);

        var record = await repository.GetByJobAsync(jobId, ct);
        if (record is null)
        {
            record = new HandoverRecord
            {
                JobId = jobId,
                CreatedByUserId = user.UserId,
                CreatedByUserName = user.UserName,
                CreatedAt = Now,
                Items = DefaultItems.Select(i => new HandoverChecklistItem
                {
                    ItemCode = i.Code,
                    Name = i.Name,
                    IsReturned = false
                }).ToList()
            };

            await repository.AddAsync(record, ct);
            await repository.SaveChangesAsync(ct);
        }

        return Result<HandoverDto>.Ok(HandoverMapper.ToDto(record));
    }

    public async Task<Result<HandoverChecklistItemDto>> SaveItemAsync(
        Guid jobId, Guid itemId, SaveHandoverItemRequest request, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<HandoverChecklistItemDto>.Fail(jobResult.Error!);

        if (!request.IsReturned && string.IsNullOrWhiteSpace(request.Note))
            return Result<HandoverChecklistItemDto>.Fail(
                "HANDOVER_NOTE_REQUIRED", "กรุณาระบุเหตุผลเมื่อของชิ้นนี้ไม่ได้คืน", nameof(request.Note));

        var record = await repository.GetByJobAsync(jobId, ct);
        if (record is null)
            return Result<HandoverChecklistItemDto>.Fail(
                "HANDOVER_NOT_FOUND", "ยังไม่มีข้อมูลส่งมอบของงานนี้ — เปิดหน้าส่งมอบก่อน");

        if (record.IsLocked)
            return Result<HandoverChecklistItemDto>.Fail("HANDOVER_LOCKED", "งานนี้ส่งมอบไปแล้ว — แก้ไขไม่ได้");

        var item = record.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            return Result<HandoverChecklistItemDto>.Fail(
                "HANDOVER_ITEM_UNKNOWN", "ไม่พบรายการนี้ในเช็คลิสต์ส่งมอบของงานนี้", nameof(itemId));

        item.IsReturned = request.IsReturned;
        item.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        item.UpdatedAt = Now;
        item.UpdatedByUserId = user.UserId;
        item.UpdatedByUserName = user.UserName;

        await repository.SaveChangesAsync(ct);

        return Result<HandoverChecklistItemDto>.Ok(HandoverMapper.ToItemDto(item));
    }

    public async Task<Result<HandoverDto>> SubmitAsync(
        Guid jobId, SubmitHandoverRequest request, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<HandoverDto>.Fail(jobResult.Error!);

        if (string.IsNullOrWhiteSpace(request.SignatureAttachmentPath))
            return Result<HandoverDto>.Fail(
                "HANDOVER_SIGNATURE_REQUIRED", "กรุณาเซ็นยืนยันการส่งมอบก่อน", nameof(request.SignatureAttachmentPath));

        var record = await repository.GetByJobAsync(jobId, ct);
        if (record is null)
            return Result<HandoverDto>.Fail(
                "HANDOVER_NOT_FOUND", "ยังไม่มีข้อมูลส่งมอบของงานนี้ — เปิดหน้าส่งมอบก่อน");

        if (record.IsLocked)
            return Result<HandoverDto>.Fail("HANDOVER_LOCKED", "งานนี้ส่งมอบไปแล้ว");

        if (record.Items.Any(i => i.UpdatedAt is null))
            return Result<HandoverDto>.Fail(
                "HANDOVER_INCOMPLETE", "กรุณาตรวจสอบของในรถให้ครบทุกรายการก่อนยืนยันส่งมอบ");

        record.SignatureImagePath = request.SignatureAttachmentPath.Trim();
        record.SubmittedAt = Now;
        record.SubmittedByUserId = user.UserId;
        record.SubmittedByUserName = user.UserName;

        await repository.AddEventAsync(new ActivityEvent
        {
            JobId = jobId,
            EntityId = record.Id,
            EntityType = nameof(HandoverRecord),
            EventType = "job.handover.submitted",
            DescriptionTh = "ยืนยันส่งมอบรถแล้ว",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await repository.SaveChangesAsync(ct);

        return Result<HandoverDto>.Ok(HandoverMapper.ToDto(record));
    }

    private async Task<Result<bool>> ValidateAsync(Guid jobId, CancellationToken ct)
    {
        if (user.Role is not (UserRole.Cashier or UserRole.Office or UserRole.Manager))
            return Result<bool>.Fail("HANDOVER_FORBIDDEN", "เฉพาะแคชเชียร์ ธุรการ หรือผู้จัดการเท่านั้นที่ใช้หน้านี้ได้");

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return Result<bool>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<bool>.Fail("JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        return Result<bool>.Ok(true);
    }
}
