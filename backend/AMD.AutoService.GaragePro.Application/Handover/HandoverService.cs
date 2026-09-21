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
/// ส่งมอบรถ — เช็คลิสต์ของในรถ + ลายเซ็นลูกค้ารับรถคืน (มีทั้งบนเว็บและบนมือถือแล้ว)
///
/// [BIZ] ลำดับที่ร้านใช้จริง (ยืนยันกับเจ้าของระบบ 2026-09-17): **ลูกค้าจ่ายเงินที่เคาน์เตอร์ → ออกใบเสร็จ →
/// ค่อยส่งมอบรถ** — `SubmitAsync` จึงบังคับว่าต้องมี `Receipt` ของงานนี้ก่อนเสมอ (`HANDOVER_RECEIPT_REQUIRED`)
/// ก่อนหน้านี้ระบบตรวจทั้งสามเงื่อนไข (ชำระครบ/ออกใบเสร็จ/ส่งมอบ) พร้อมกัน **ตอนปิดงานเท่านั้น** ทำให้เซ็นรับรถ
/// ก่อนจ่ายเงินได้จริง แล้วรถออกไปโดยงานค้างปิดไม่ได้ — ปิดช่องนั้นที่นี่
///
/// การตรวจใบเสร็จ (ไม่ใช่ยอดคงเหลือ) เพียงพอเพราะ `PosService.IssueReceiptAsync` ปฏิเสธด้วย
/// `POS_BALANCE_NOT_SETTLED` ถ้ายอดยังไม่เป็นศูนย์ — มีใบเสร็จ ⟹ จ่ายครบแล้วเสมอ
/// รายการเช็คลิสต์เป็น const list คงที่ ไม่ใช่ template ([ASSUME] รอยืนยันรายการจริงจากฝ่ายปฏิบัติการ)
/// </summary>
public sealed class HandoverService(
    IHandoverRepository repository,
    IJobRepository jobs,
    IPosRepository pos,
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

        // เปิด/ติ๊กเช็คลิสต์ได้ก่อนออกใบเสร็จโดยตั้งใจ — คนเตรียมของในรถทำงานคู่ขนานกับแคชเชียร์ที่กำลังเก็บเงินอยู่
        // ด่านใบเสร็จอยู่ที่ SubmitAsync (ขั้นที่ลูกค้าเซ็นและล็อก) ไม่ใช่ที่นี่
        return Result<HandoverDto>.Ok(HandoverMapper.ToDto(record, await pos.GetReceiptByJobAsync(jobId, ct)));
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

        // [BIZ] จ่ายเงินก่อน ค่อยส่งมอบรถ — ดูเหตุผลเต็มที่หัวคลาส
        var receipt = await pos.GetReceiptByJobAsync(jobId, ct);
        if (receipt is null)
            return Result<HandoverDto>.Fail(
                "HANDOVER_RECEIPT_REQUIRED",
                "ยังออกใบเสร็จของงานนี้ไม่สำเร็จ — ต้องรับชำระเงินให้ครบและออกใบเสร็จก่อนจึงจะส่งมอบรถได้");

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
            DescriptionTh = $"ยืนยันส่งมอบรถแล้ว (หลังออกใบเสร็จ {receipt.DocumentNo})",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await repository.SaveChangesAsync(ct);

        return Result<HandoverDto>.Ok(HandoverMapper.ToDto(record, receipt));
    }

    private async Task<Result<bool>> ValidateAsync(Guid jobId, CancellationToken ct)
    {
        // [BIZ] ทุกบทบาทปฏิบัติการส่งมอบรถได้ รวมช่าง/หัวหน้าช่าง (ยืนยันกับเจ้าของระบบ 2026-09-17):
        // คนที่ยืนอยู่กับลูกค้าข้างรถตอนเซ็นรับ คือช่างที่เข็นรถออกมา ไม่ใช่คนที่นั่งอยู่หลังเคาน์เตอร์
        // สิ่งที่กันไม่ให้ส่งมอบก่อนเวลาอันควรคือ "ต้องมีใบเสร็จก่อน" ใน SubmitAsync ไม่ใช่รายชื่อ role นี้
        // (ช่างยังแตะเงินไม่ได้อยู่ดี — PosService ยังจำกัด Cashier/Office/Manager ตาม docs/01-workflow.md §4)
        // คงรายการไว้แบบระบุครบทุกค่าเพื่อให้ role ใหม่ที่เพิ่มทีหลังต้องถูกพิจารณาก่อน ไม่ได้สิทธิ์เงียบๆ
        if (user.Role is not (UserRole.FrontDesk or UserRole.Technician or UserRole.Office
            or UserRole.Cashier or UserRole.Manager or UserRole.Lead))
            return Result<bool>.Fail(
                "HANDOVER_FORBIDDEN", "บทบาทนี้ยังไม่ได้รับสิทธิ์ใช้หน้าส่งมอบรถ");

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return Result<bool>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<bool>.Fail("JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        return Result<bool>.Ok(true);
    }
}
