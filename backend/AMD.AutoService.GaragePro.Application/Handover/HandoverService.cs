using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Handover;

public interface IHandoverService
{
    Task<Result<HandoverDto>> GetOrCreateAsync(Guid jobId, CancellationToken ct = default);

    Task<Result<HandoverChecklistItemDto>> SaveItemAsync(
        Guid jobId, Guid itemId, SaveHandoverItemRequest request, CancellationToken ct = default);

    /// <summary>บันทึกเลขไมล์ตอนส่งมอบ + นัดเข้ารับบริการครั้งถัดไป — แก้ได้จนกว่าจะเซ็นส่งมอบ</summary>
    Task<Result<HandoverDto>> SaveServiceInfoAsync(
        Guid jobId, SaveHandoverServiceInfoRequest request, CancellationToken ct = default);

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
        return Result<HandoverDto>.Ok(
            HandoverMapper.ToDto(record, await pos.GetReceiptByJobAsync(jobId, ct), jobResult.Data!));
    }

    /// <summary>[BIZ] เพิ่ม 2026-10-08 — เลขไมล์ส่งมอบ + นัดครั้งถัดไป บันทึกแยกก่อนเซ็นได้ (แบบผลทดลองขับของ QC)
    /// เพื่อให้ใบส่งมอบที่พิมพ์ก่อนเซ็นมีค่าครบ · วันนัดตอนนี้เป็นพรีวิวจากวันนี้ — SubmitAsync คำนวณใหม่จากวันส่งมอบจริง
    /// (2026-10-25: นัดได้หลายรายการ — ส่งชุดเต็มทุกครั้ง แทนที่ของเดิมทั้งชุด)</summary>
    public async Task<Result<HandoverDto>> SaveServiceInfoAsync(
        Guid jobId, SaveHandoverServiceInfoRequest request, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<HandoverDto>.Fail(jobResult.Error!);
        var job = jobResult.Data!;

        var normalized = NormalizeItems(request);
        if (!normalized.Success) return Result<HandoverDto>.Fail(normalized.Error!);

        var validation = ValidateServiceInfo(job, request.MileageAtHandover, normalized.Data!);
        if (!validation.Success) return Result<HandoverDto>.Fail(validation.Error!);

        var record = await repository.GetByJobAsync(jobId, ct);
        if (record is null)
            return Result<HandoverDto>.Fail(
                "HANDOVER_NOT_FOUND", "ยังไม่มีข้อมูลส่งมอบของงานนี้ เปิดหน้าส่งมอบก่อน");

        if (record.IsLocked)
            return Result<HandoverDto>.Fail("HANDOVER_LOCKED", "งานนี้ส่งมอบไปแล้ว แก้ไขไม่ได้");

        record.MileageAtHandover = request.MileageAtHandover;

        // แทนที่ทั้งชุด (ผู้ใช้เห็นเป็นรายการเดียวที่แก้/ลบแถวได้) — แถวเดิมที่หายไปถูก EF ลบเป็น orphan
        record.ServiceItems.Clear();
        for (var i = 0; i < normalized.Data!.Count; i++)
        {
            var item = normalized.Data[i];
            record.ServiceItems.Add(new HandoverServiceItem
            {
                HandoverRecordId = record.Id,
                TemplateId = item.TemplateId,
                Name = item.Name,
                NextServiceMileage = item.NextServiceMileage,
                NextServiceMonths = item.NextServiceMonths,
                NextServiceDueOn = ServiceSchedule.NextDueOn(Now, item.NextServiceMonths),
                SortOrder = i
            });
        }
        record.ServiceInfoUpdatedAt = Now;
        record.ServiceInfoUpdatedByUserName = user.UserName;

        await repository.SaveChangesAsync(ct);

        return Result<HandoverDto>.Ok(
            HandoverMapper.ToDto(record, await pos.GetReceiptByJobAsync(jobId, ct), job));
    }

    /// <summary>
    /// รวม body รูปใหม่ (Items) กับรูปเดิม (นัดเดียว) ให้เป็นรายการเดียว — รูปเดิมแปลงเป็น "เช็คระยะ" 1 รายการ
    /// ตัดช่องว่างชื่อที่นี่ที่เดียว เพื่อให้การตรวจชื่อซ้ำและสิ่งที่บันทึกเป็นค่าเดียวกัน
    /// </summary>
    private static Result<List<HandoverServiceItemRequest>> NormalizeItems(SaveHandoverServiceInfoRequest request)
    {
        if (request.Items is not null)
            return Result<List<HandoverServiceItemRequest>>.Ok(
                request.Items.Select(i => i with { Name = (i.Name ?? "").Trim() }).ToList());

        if (request.NextServiceMileage is null && request.NextServiceMonths is null)
            return Result<List<HandoverServiceItemRequest>>.Ok([]);
        if (request.NextServiceMileage is null)
            return Result<List<HandoverServiceItemRequest>>.Fail(
                "HANDOVER_VALIDATION", "กรุณาระบุเลขไมล์ที่นัดครั้งถัดไป", "nextServiceMileage");
        if (request.NextServiceMonths is null)
            return Result<List<HandoverServiceItemRequest>>.Fail(
                "HANDOVER_VALIDATION", "กรุณาระบุระยะเวลานัดครั้งถัดไป (เดือน)", "nextServiceMonths");

        return Result<List<HandoverServiceItemRequest>>.Ok(
        [
            new HandoverServiceItemRequest(
                null, ServiceSchedule.LegacySingleItemName,
                request.NextServiceMileage.Value, request.NextServiceMonths.Value)
        ]);
    }

    /// <summary>กติกาไมล์ส่งมอบ/นัดครั้งถัดไป — ใช้ทั้งตอนบันทึกและตรวจซ้ำตอนเซ็น (ไมล์รับรถอาจถูกแก้หลังบันทึก)</summary>
    private static Result<bool> ValidateServiceInfo(
        Job job, int mileageAtHandover, IReadOnlyList<HandoverServiceItemRequest> items)
    {
        if (job.MileageAtIntake is null)
            return Result<bool>.Fail(
                "HANDOVER_INTAKE_MILEAGE_REQUIRED",
                "งานนี้ยังไม่มีเลขไมล์ขณะรับรถ บันทึกที่ขั้นรับรถของการ์ดจ๊อบก่อน");

        if (!Odometer.IsValid(mileageAtHandover))
            return Result<bool>.Fail(
                "HANDOVER_VALIDATION", $"เลขไมล์ต้องอยู่ระหว่าง 0 ถึง {Odometer.MaxKm:N0} กม.", "mileageAtHandover");

        if (mileageAtHandover < job.MileageAtIntake)
            return Result<bool>.Fail(
                "HANDOVER_VALIDATION",
                $"เลขไมล์ตอนส่งมอบต้องไม่น้อยกว่าเลขไมล์ขณะรับรถ ({job.MileageAtIntake:N0} กม.)", "mileageAtHandover");

        if (items.Count > ServiceSchedule.MaxItemsPerHandover)
            return Result<bool>.Fail(
                "HANDOVER_VALIDATION",
                $"นัดครั้งถัดไปได้ไม่เกิน {ServiceSchedule.MaxItemsPerHandover} รายการต่อการส่งมอบ", "items");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var at = $"items[{i}]";

            if (string.IsNullOrWhiteSpace(item.Name))
                return Result<bool>.Fail("HANDOVER_VALIDATION", "กรุณาระบุชื่อรายการนัดบริการ", $"{at}.name");
            if (item.Name.Length > ServiceSchedule.ItemNameMaxLength)
                return Result<bool>.Fail(
                    "HANDOVER_VALIDATION",
                    $"ชื่อรายการนัดบริการต้องยาวไม่เกิน {ServiceSchedule.ItemNameMaxLength} ตัวอักษร", $"{at}.name");
            if (!seen.Add(item.Name))
                return Result<bool>.Fail(
                    "HANDOVER_VALIDATION", $"มีรายการ \"{item.Name}\" ซ้ำกัน", $"{at}.name");

            // field ของรูปเดิม (นัดเดียว) ต้องคงชื่อเดิม ไม่งั้นแอปรุ่นเก่าที่ผูกข้อความ error กับช่องกรอกจะหลงช่อง
            var legacyShape = items.Count == 1 && item.Name == ServiceSchedule.LegacySingleItemName;
            var mileageField = legacyShape ? "nextServiceMileage" : $"{at}.nextServiceMileage";
            var monthsField = legacyShape ? "nextServiceMonths" : $"{at}.nextServiceMonths";

            if (!Odometer.IsValid(item.NextServiceMileage) || item.NextServiceMileage <= mileageAtHandover)
                return Result<bool>.Fail(
                    "HANDOVER_VALIDATION",
                    $"เลขไมล์ที่นัด \"{item.Name}\" ต้องมากกว่าเลขไมล์ตอนส่งมอบ ({mileageAtHandover:N0} กม.)", mileageField);

            if (item.NextServiceMonths is < ServiceSchedule.MinMonths or > ServiceSchedule.MaxMonths)
                return Result<bool>.Fail(
                    "HANDOVER_VALIDATION",
                    $"ระยะเวลานัด \"{item.Name}\" ต้องอยู่ระหว่าง {ServiceSchedule.MinMonths}–{ServiceSchedule.MaxMonths} เดือน",
                    monthsField);
        }

        return Result<bool>.Ok(true);
    }

    public async Task<Result<HandoverChecklistItemDto>> SaveItemAsync(
        Guid jobId, Guid itemId, SaveHandoverItemRequest request, CancellationToken ct = default)
    {
        var jobResult = await ValidateAsync(jobId, ct);
        if (!jobResult.Success) return Result<HandoverChecklistItemDto>.Fail(jobResult.Error!);

        if (!request.IsReturned && string.IsNullOrWhiteSpace(request.Note))
            return Result<HandoverChecklistItemDto>.Fail(
                "HANDOVER_NOTE_REQUIRED", "กรุณาระบุรายละเอียดเมื่อของชิ้นนี้สูญหาย", nameof(request.Note));

        var record = await repository.GetByJobAsync(jobId, ct);
        if (record is null)
            return Result<HandoverChecklistItemDto>.Fail(
                "HANDOVER_NOT_FOUND", "ยังไม่มีข้อมูลส่งมอบของงานนี้ เปิดหน้าส่งมอบก่อน");

        if (record.IsLocked)
            return Result<HandoverChecklistItemDto>.Fail("HANDOVER_LOCKED", "งานนี้ส่งมอบไปแล้ว แก้ไขไม่ได้");

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
                "ยังออกใบเสร็จของงานนี้ไม่สำเร็จ ต้องรับชำระเงินให้ครบและออกใบเสร็จก่อนจึงจะส่งมอบรถได้");

        var record = await repository.GetByJobAsync(jobId, ct);
        if (record is null)
            return Result<HandoverDto>.Fail(
                "HANDOVER_NOT_FOUND", "ยังไม่มีข้อมูลส่งมอบของงานนี้ เปิดหน้าส่งมอบก่อน");

        if (record.IsLocked)
            return Result<HandoverDto>.Fail("HANDOVER_LOCKED", "งานนี้ส่งมอบไปแล้ว");

        if (record.Items.Any(i => i.UpdatedAt is null))
            return Result<HandoverDto>.Fail(
                "HANDOVER_INCOMPLETE", "กรุณาตรวจสอบของในรถให้ครบทุกรายการก่อนยืนยันส่งมอบ");

        // [BIZ] เพิ่ม 2026-10-08 — ต้องบันทึกไมล์ส่งมอบ + นัดครั้งถัดไปก่อนเซ็น
        // [BIZ] อย่างน้อย 1 รายการนัด (ยืนยัน 2026-10-25) — ไมล์ส่งมอบยังบังคับเหมือนเดิม
        if (record.MileageAtHandover is null || record.ServiceItems.Count == 0)
            return Result<HandoverDto>.Fail(
                "HANDOVER_SERVICE_INFO_REQUIRED",
                "กรุณาบันทึกเลขไมล์ตอนส่งมอบและนัดเข้ารับบริการครั้งถัดไปอย่างน้อย 1 รายการก่อนยืนยันส่งมอบ");

        var job = jobResult.Data!;
        var serviceInfo = ValidateServiceInfo(
            job, record.MileageAtHandover.Value,
            record.ServiceItems.OrderBy(i => i.SortOrder).Select(i => new HandoverServiceItemRequest(
                i.TemplateId, i.Name, i.NextServiceMileage, i.NextServiceMonths)).ToList());
        if (!serviceInfo.Success) return Result<HandoverDto>.Fail(serviceInfo.Error!);

        record.SignatureImagePath = request.SignatureAttachmentPath.Trim();
        record.SubmittedAt = Now;
        // วันนัดนับจากวันที่ส่งมอบจริง ไม่ใช่วันที่กดบันทึก (บันทึกไว้เมื่อวาน เซ็นวันนี้ วันนัดต้องเลื่อนตาม) — ทุกรายการ
        foreach (var item in record.ServiceItems)
            item.NextServiceDueOn = ServiceSchedule.NextDueOn(Now, item.NextServiceMonths);
        var earliest = HandoverMapper.Earliest(record)!;
        record.SubmittedByUserId = user.UserId;
        record.SubmittedByUserName = user.UserName;

        await repository.AddEventAsync(new ActivityEvent
        {
            JobId = jobId,
            EntityId = record.Id,
            EntityType = nameof(HandoverRecord),
            EventType = "job.handover.submitted",
            DescriptionTh = $"ยืนยันส่งมอบรถแล้ว (หลังออกใบเสร็จ {receipt.DocumentNo}) · เลขไมล์ {record.MileageAtHandover:N0} กม." +
                $" · นัดครั้งถัดไป {record.ServiceItems.Count} รายการ (เร็วสุด {earliest.Name} {earliest.NextServiceMileage:N0} กม." +
                $" หรือ {earliest.NextServiceDueOn.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)})",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                mileageAtHandover = record.MileageAtHandover,
                serviceItems = record.ServiceItems.OrderBy(i => i.SortOrder).Select(i => new
                {
                    name = i.Name,
                    nextServiceMileage = i.NextServiceMileage,
                    nextServiceMonths = i.NextServiceMonths,
                    nextServiceDueOn = i.NextServiceDueOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
                })
            })
        }, ct);
        await repository.SaveChangesAsync(ct);

        return Result<HandoverDto>.Ok(HandoverMapper.ToDto(record, receipt, job));
    }

    private async Task<Result<Job>> ValidateAsync(Guid jobId, CancellationToken ct)
    {
        // [BIZ] ทุกบทบาทปฏิบัติการส่งมอบรถได้ รวมช่าง/หัวหน้าช่าง (ยืนยันกับเจ้าของระบบ 2026-09-17):
        // คนที่ยืนอยู่กับลูกค้าข้างรถตอนเซ็นรับ คือช่างที่เข็นรถออกมา ไม่ใช่คนที่นั่งอยู่หลังเคาน์เตอร์
        // สิ่งที่กันไม่ให้ส่งมอบก่อนเวลาอันควรคือ "ต้องมีใบเสร็จก่อน" ใน SubmitAsync ไม่ใช่รายชื่อ role นี้
        // (ช่างยังแตะเงินไม่ได้อยู่ดี — PosService ยังจำกัด Cashier/Office/Manager ตาม docs/01-workflow.md §4)
        // คงรายการไว้แบบระบุครบทุกค่าเพื่อให้ role ใหม่ที่เพิ่มทีหลังต้องถูกพิจารณาก่อน ไม่ได้สิทธิ์เงียบๆ
        if (user.Role is not (UserRole.FrontDesk or UserRole.Technician or UserRole.Office
            or UserRole.Cashier or UserRole.Manager or UserRole.Lead))
            return Result<Job>.Fail(
                "HANDOVER_FORBIDDEN", "บทบาทนี้ยังไม่ได้รับสิทธิ์ใช้หน้าส่งมอบรถ");

        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return Result<Job>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<Job>.Fail("JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        return Result<Job>.Ok(job);
    }
}
