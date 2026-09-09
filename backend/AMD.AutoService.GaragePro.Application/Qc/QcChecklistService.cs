using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Qc;

public interface IQcChecklistService
{
    Task<Result<QcChecklistDto>> GetOrCreateAsync(Guid jobId, CancellationToken ct = default);

    Task<Result<QcChecklistItemDto>> SaveItemAsync(
        Guid jobId, Guid itemId, SaveQcChecklistItemRequest request, CancellationToken ct = default);

    Task<Result<QcChecklistDto>> SaveTestDriveAsync(
        Guid jobId, SaveQcTestDriveRequest request, CancellationToken ct = default);
}

/// <summary>
/// เช็คลิสต์ตรวจสอบคุณภาพ (QC) — รายการมาจากบรรทัดที่ลูกค้าอนุมัติในใบเสนอราคาปัจจุบันของ job นี้
/// [BIZ] ไม่มีสถานะ "ไม่ผ่าน"/process ตีกลับ (คำขอผู้ใช้ 2026-09-09) — ผ่านอย่างเดียว ถ้ายังไม่ผ่านไปแจ้งช่างแก้
/// นอกระบบแล้วย้อนกลับมาติ๊กผ่านทีหลัง guard `QcPassed` ของ `JobService.ComputeGuardAsync` คำนวณจากรายการนี้จริง
/// (ไม่ใช่ manual-override อีกต่อไปสำหรับ Qc→Ready)
/// </summary>
public sealed class QcChecklistService(
    IQcChecklistRepository repository,
    IJobRepository jobs,
    IQuotationRepository quotations,
    ICurrentUser user,
    TimeProvider clock) : IQcChecklistService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<QcChecklistDto>> GetOrCreateAsync(Guid jobId, CancellationToken ct = default)
    {
        var jobResult = await ValidateJobAsync(jobId, ct);
        if (!jobResult.Success) return Result<QcChecklistDto>.Fail(jobResult.Error!);

        var checklist = await repository.GetByJobAsync(jobId, ct);
        if (checklist is null)
        {
            var draft = await CreateDraftAsync(jobId, ct);
            if (!draft.Success) return Result<QcChecklistDto>.Fail(draft.Error!);
            checklist = draft.Data!;
        }

        return Result<QcChecklistDto>.Ok(QcMapper.ToDto(checklist));
    }

    public async Task<Result<QcChecklistItemDto>> SaveItemAsync(
        Guid jobId, Guid itemId, SaveQcChecklistItemRequest request, CancellationToken ct = default)
    {
        var result = QcMapper.ParseResultToken(request.Result);
        if (result is null)
            return Result<QcChecklistItemDto>.Fail(
                "QC_RESULT_INVALID", "ผลตรวจไม่ถูกต้อง — ต้องเป็น pending หรือ pass", nameof(request.Result));

        var jobResult = await ValidateJobAsync(jobId, ct);
        if (!jobResult.Success) return Result<QcChecklistItemDto>.Fail(jobResult.Error!);

        var checklist = await repository.GetByJobAsync(jobId, ct);
        if (checklist is null)
            return Result<QcChecklistItemDto>.Fail(
                "QC_NOT_FOUND", "ยังไม่มีเช็คลิสต์ QC ของงานนี้ — เปิดหน้า QC ก่อน");

        if (checklist.IsLocked)
            return Result<QcChecklistItemDto>.Fail("QC_LOCKED", "งานนี้ผ่าน QC ไปแล้ว — แก้ไขไม่ได้");

        var item = checklist.Items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            return Result<QcChecklistItemDto>.Fail(
                "QC_ITEM_UNKNOWN", "ไม่พบรายการนี้ในเช็คลิสต์ QC ของงานนี้", nameof(itemId));

        item.Result = result.Value;
        item.Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        item.UpdatedAt = Now;
        item.UpdatedByUserId = user.UserId;
        item.UpdatedByUserName = user.UserName;

        await repository.SaveChangesAsync(ct);

        return Result<QcChecklistItemDto>.Ok(QcMapper.ToItemDto(item));
    }

    public async Task<Result<QcChecklistDto>> SaveTestDriveAsync(
        Guid jobId, SaveQcTestDriveRequest request, CancellationToken ct = default)
    {
        if (request.Km < 0)
            return Result<QcChecklistDto>.Fail("QC_VALIDATION", "กรุณาระบุระยะทางทดลองขับที่ถูกต้อง", nameof(request.Km));

        if (string.IsNullOrWhiteSpace(request.Note))
            return Result<QcChecklistDto>.Fail("QC_VALIDATION", "กรุณาระบุผลการทดลองขับ", nameof(request.Note));

        var jobResult = await ValidateJobAsync(jobId, ct);
        if (!jobResult.Success) return Result<QcChecklistDto>.Fail(jobResult.Error!);

        var checklist = await repository.GetByJobAsync(jobId, ct);
        if (checklist is null)
            return Result<QcChecklistDto>.Fail(
                "QC_NOT_FOUND", "ยังไม่มีเช็คลิสต์ QC ของงานนี้ — เปิดหน้า QC ก่อน");

        if (checklist.IsLocked)
            return Result<QcChecklistDto>.Fail("QC_LOCKED", "งานนี้ผ่าน QC ไปแล้ว — แก้ไขไม่ได้");

        checklist.TestDriveKm = request.Km;
        checklist.TestDriveNote = request.Note.Trim();
        checklist.TestDriveRecordedAt = Now;
        checklist.TestDriveRecordedByUserId = user.UserId;
        checklist.TestDriveRecordedByUserName = user.UserName;

        await repository.SaveChangesAsync(ct);

        return Result<QcChecklistDto>.Ok(QcMapper.ToDto(checklist));
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

    private async Task<Result<QcChecklist>> CreateDraftAsync(Guid jobId, CancellationToken ct)
    {
        var quotation = await quotations.GetLatestForJobAsync(jobId, ct);
        var approvedLines = quotation?.Lines.Where(l => l.ApprovalStatus == LineApprovalStatus.Approved).ToList()
            ?? [];

        if (approvedLines.Count == 0)
            return Result<QcChecklist>.Fail(
                "QC_NO_APPROVED_LINES",
                "งานนี้ยังไม่มีรายการที่อนุมัติในใบเสนอราคา — ตรวจสอบขั้นเสนอราคา/งานซ่อมก่อน");

        var checklist = new QcChecklist
        {
            JobId = jobId,
            CreatedByUserId = user.UserId,
            CreatedByUserName = user.UserName,
            CreatedAt = Now
        };

        checklist.Items = approvedLines.Select(line => new QcChecklistItem
        {
            QcChecklistId = checklist.Id,
            QuotationLineId = line.Id,
            CatalogCode = line.CatalogCode,
            Name = line.Name,
            Type = line.Type,
            Result = QcItemResult.Pending
        }).ToList();

        await repository.AddAsync(checklist, ct);
        await repository.SaveChangesAsync(ct);

        return Result<QcChecklist>.Ok(checklist);
    }
}
