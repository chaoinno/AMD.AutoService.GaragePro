using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Quotations;

public interface IQuotationService
{
    Task<Result<IReadOnlyList<QuotationSummaryDto>>> GetQueueAsync(
        string? statusFilter, Guid? jobId = null, CancellationToken ct = default);
    Task<Result<QuotationDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<QuotationDto>> CreateAsync(CreateQuotationRequest request, CancellationToken ct = default);
    Task<Result<QuotationDto>> AddLineAsync(Guid id, UpsertLineRequest request, CancellationToken ct = default);
    Task<Result<QuotationDto>> UpdateLineAsync(Guid id, Guid lineId, UpsertLineRequest request, CancellationToken ct = default);
    Task<Result<QuotationDto>> RemoveLineAsync(Guid id, Guid lineId, CancellationToken ct = default);
    Task<Result<QuotationValidationDto>> ValidateAsync(Guid id, CancellationToken ct = default);
    Task<Result<QuotationDto>> SendAsync(Guid id, CancellationToken ct = default);
    Task<Result<QuotationDto>> ReviseAsync(Guid id, ReviseQuotationRequest request, CancellationToken ct = default);
    Task<Result<QuotationDto>> DecideLineAsync(Guid id, Guid lineId, LineDecisionRequest request, CancellationToken ct = default);
    Task<Result<QuotationDto>> SignAsync(Guid id, SignQuotationRequest request, CancellationToken ct = default);

    /// <summary>เพิ่มหลายบรรทัดจากเทมเพลตในครั้งเดียว — คำนวณยอด/บันทึก/เขียน ActivityEvent ครั้งเดียวทั้งชุด
    /// (ดู docs/08-quotation-template.md)</summary>
    Task<Result<QuotationDto>> ApplyTemplateAsync(Guid id, ApplyTemplateRequest request, CancellationToken ct = default);
}

public sealed class QuotationService(
    IQuotationRepository repository,
    ICatalogRepository catalog,
    IJobRepository jobs,
    IQuotationTemplateRepository templates,
    ILegacyReader legacy,
    ICurrentUser user,
    TimeProvider clock) : IQuotationService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<IReadOnlyList<QuotationSummaryDto>>> GetQueueAsync(
        string? statusFilter, Guid? jobId = null, CancellationToken ct = default)
    {
        var items = await repository.GetQueueAsync(user.ShardKey, user.BranchId, statusFilter, jobId, ct);
        var dto = items.Select(q => QuotationMapper.ToSummary(q, Now)).ToList();
        return Result<IReadOnlyList<QuotationSummaryDto>>.Ok(dto);
    }

    public async Task<Result<QuotationDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var q = await repository.GetWithLinesAsync(id, ct);
        if (q is null) return NotFound();
        if (!BelongsToCurrentScope(q)) return Forbidden();

        return Result<QuotationDto>.Ok(QuotationMapper.ToDto(q, user, Now));
    }

    public async Task<Result<QuotationDto>> CreateAsync(
        CreateQuotationRequest request, CancellationToken ct = default)
    {
        var job = await jobs.GetAsync(request.JobId, ct);
        if (job is null)
            return Result<QuotationDto>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {request.JobId}");

        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<QuotationDto>.Fail("JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        // [BIZ] มีใบที่ยังไม่ถูกแทนที่อยู่แล้ว ต้องใช้ "ออกฉบับแก้ไข" ไม่ใช่สร้างใหม่
        var existing = await repository.GetLatestForJobAsync(request.JobId, ct);
        if (existing is not null && existing.Status is not (QuotationStatus.Rejected or QuotationStatus.Superseded))
            return Result<QuotationDto>.Fail(
                "QUOTE_ALREADY_EXISTS",
                $"งานนี้มีใบเสนอราคา {existing.Code} อยู่แล้ว — ถ้าต้องการแก้ราคาให้ออกฉบับแก้ไขแทน");

        var branch = await legacy.GetBranchAsync(user.ShardKey, user.BranchId, ct);
        var version = await repository.GetNextVersionAsync(request.JobId, ct);

        var quotation = NewQuotation(job, branch, version, request.ValidUntil, request.DepositAmount);
        QuotationCalculator.ApplyQuotationTotals(quotation);

        await repository.AddAsync(quotation, ct);
        await LogAsync(quotation, "quotation.created", $"สร้างใบเสนอราคา {quotation.Code}", ct);
        await repository.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(QuotationMapper.ToDto(quotation, user, Now));
    }

    public async Task<Result<QuotationDto>> AddLineAsync(
        Guid id, UpsertLineRequest request, CancellationToken ct = default)
    {
        var (q, error) = await LoadEditableAsync(id, ct);
        if (error is not null) return error;

        QuotationLine line;

        if (string.IsNullOrWhiteSpace(request.CatalogCode))
        {
            // [BIZ] รายการนอกแคตตาล็อก — docs/07-quotation-adhoc-line.md
            var adHocError = ValidateAdHocRequest(request);
            if (adHocError is not null) return adHocError;

            line = new QuotationLine
            {
                QuotationId = q!.Id,
                Sequence = q.Lines.Count == 0 ? 1 : q.Lines.Max(l => l.Sequence) + 1,
                CatalogCode = string.Empty,
                Name = request.Name!.Trim(),
                Type = request.Type!.Value,
                Source = request.Source,
                Quantity = request.Quantity,
                Unit = ResolveAdHocUnit(request),
                UnitPrice = request.UnitPrice!.Value,
                UnitCost = user.CanSeeCost ? request.UnitCost ?? 0m : 0m,
                DiscountPercent = request.DiscountPercent,
                Promotion = request.Promotion,
                AssignedTechnicianId = request.AssignedTechnicianId,
                Note = request.Note,
                StandardHours = request.Type == LineType.Labor ? request.StandardHours : null
            };
        }
        else
        {
            var items = await catalog.GetByCodesAsync(user.ShardKey, user.BranchId, [request.CatalogCode], ct);
            var item = items.FirstOrDefault();
            if (item is null)
                return Result<QuotationDto>.Fail("CATALOG_ITEM_NOT_FOUND",
                    $"ไม่พบรหัส {request.CatalogCode} ในแคตตาล็อกของสาขานี้");

            line = new QuotationLine
            {
                QuotationId = q!.Id,
                Sequence = q.Lines.Count == 0 ? 1 : q.Lines.Max(l => l.Sequence) + 1,
                CatalogCode = item.Code,
                Name = item.Name,
                Type = item.Type,
                Source = request.Source,
                Quantity = request.Quantity,
                Unit = item.Unit,
                UnitPrice = request.UnitPrice ?? item.Price,
                UnitCost = item.Cost,
                DiscountPercent = request.DiscountPercent,
                Promotion = request.Promotion,
                AssignedTechnicianId = request.AssignedTechnicianId,
                Note = request.Note,
                StandardHours = item.StandardHours
            };
        }

        await AssignTechnicianNameAsync(line, ct);

        q!.Lines.Add(line);
        return await PersistAsync(q, "quotation.line.added", $"เพิ่มรายการ {line.Name}", ct);
    }

    /// <summary>[BIZ] ไม่ตัด AssignedTechnicianId — เทมเพลตไม่เก็บช่างไว้เลย (อาจลาออกไปแล้ว) บรรทัดค่าแรงที่ได้
    /// จึงยังไม่มีช่างจนกว่าจะมีคนระบุเอง แล้ว QuotationValidator.ValidateForSend จะปฏิเสธถ้ายังไม่ระบุตอนส่งจริง —
    /// เป็นความล้มเหลวที่มองเห็นได้ ดีกว่าฝังช่างเก่าแบบเงียบๆ</summary>
    public async Task<Result<QuotationDto>> ApplyTemplateAsync(
        Guid id, ApplyTemplateRequest request, CancellationToken ct = default)
    {
        var (q, error) = await LoadEditableAsync(id, ct);
        if (error is not null) return error;

        var template = await templates.GetWithLinesAsync(user.ShardKey, user.BranchId, request.TemplateId, ct);
        if (template is null)
            return Result<QuotationDto>.Fail("QUOTE_TEMPLATE_NOT_FOUND", "ไม่พบเทมเพลตใบเสนอราคานี้ในสาขาปัจจุบัน");

        if (!template.IsActive)
            return Result<QuotationDto>.Fail("QUOTE_TEMPLATE_INACTIVE",
                $"เทมเพลต {template.Code} {template.Name} ถูกปิดใช้งานอยู่ — เปิดใช้งานที่เมนูข้อมูลหลักก่อน หรือเลือกเทมเพลตอื่น");

        if (template.Lines.Count == 0)
            return Result<QuotationDto>.Fail("QUOTE_TEMPLATE_EMPTY",
                $"เทมเพลต {template.Code} {template.Name} ยังไม่มีรายการ — เพิ่มรายการในเทมเพลตก่อนนำมาใช้");

        // อ่านราคา/ชื่อ/ต้นทุนสดจากแคตตาล็อกครั้งเดียวทั้งชุด (batch) — ชื่อ/ราคาที่ cache ไว้ในเทมเพลตใช้แสดงผล
        // เท่านั้น ห้ามอ่านตรงนี้ (กันชื่อ/ราคาเก่าค้าง — เหมือนเส้นทางแคตตาล็อกของ AddLineAsync ทุกประการ)
        var codes = template.Lines.Where(l => !string.IsNullOrWhiteSpace(l.CatalogCode))
            .Select(l => l.CatalogCode).Distinct().ToList();
        var catalogByCode = codes.Count == 0
            ? new Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase)
            : (await catalog.GetByCodesAsync(user.ShardKey, user.BranchId, codes, ct))
                .ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);

        var missing = codes.Where(c => !catalogByCode.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            return Result<QuotationDto>.Fail("QUOTE_TEMPLATE_ITEM_MISSING",
                $"เทมเพลต {template.Code} อ้างถึงรหัส {string.Join(", ", missing)} ที่ไม่มีในแคตตาล็อกของสาขานี้แล้ว " +
                $"({missing.Count} รหัส) — ยังไม่ได้เพิ่มรายการใดเลย แก้เทมเพลตที่เมนูข้อมูลหลักก่อน");

        // [BIZ] รายการซ้ำ (กับที่มีอยู่แล้วในใบนี้) → หยุดทั้งหมด ไม่เพิ่มสักบรรทัด ไม่ auto-merge/ไม่ข้ามเงียบๆ
        // (ไม่มี endpoint ลบใบเสนอราคา การเพิ่มครึ่งๆ จะทำให้ผู้ใช้ไม่รู้ว่าอะไรเข้าไปแล้วบ้าง)
        var combined = q!.Lines.Select(l => (l.CatalogCode, l.Name))
            .Concat(template.Lines.Select(l => (l.CatalogCode, l.Name)))
            .ToList();
        var duplicates = QuotationValidator.FindDuplicateGroups(combined);
        if (duplicates.HasAny)
        {
            var keys = duplicates.CodeDuplicates.Select(g => g.Key)
                .Concat(duplicates.NameDuplicates.Select(g => g.Key)).ToList();
            return Result<QuotationDto>.Fail("QUOTE_TEMPLATE_DUPLICATE_LINE",
                $"เทมเพลต {template.Code} มี {keys.Count} รายการที่ซ้ำกับใบเสนอราคานี้อยู่แล้ว ({string.Join(", ", keys)}) " +
                "— ยังไม่ได้เพิ่มรายการใดเลย ลบรายการเดิมออกก่อนหรือเลือกเทมเพลตอื่น");
        }

        var nextSequence = q.Lines.Count == 0 ? 1 : q.Lines.Max(l => l.Sequence) + 1;
        var addedCount = 0;

        foreach (var templateLine in template.Lines.OrderBy(l => l.Sequence))
        {
            var isAdHoc = string.IsNullOrWhiteSpace(templateLine.CatalogCode);
            QuotationLine newLine;

            if (isAdHoc)
            {
                newLine = new QuotationLine
                {
                    QuotationId = q.Id,
                    Sequence = nextSequence++,
                    CatalogCode = string.Empty,
                    Name = templateLine.Name,
                    Type = templateLine.Type,
                    Source = request.Source ?? templateLine.Source,
                    Quantity = templateLine.Quantity,
                    Unit = templateLine.Unit ?? (templateLine.Type == LineType.Labor ? "งาน" : "ชิ้น"),
                    UnitPrice = templateLine.UnitPrice ?? 0m,
                    // ต้นทุนมาจากแถวที่ผู้จัดการเขียนไว้ฝั่ง server (ไม่ใช่ client ที่เชื่อไม่ได้แบบ ad-hoc line ปกติ)
                    // เก็บค่าจริงเสมอแม้ผู้ที่กด apply จะไม่มีสิทธิ์เห็นต้นทุน — strip ที่ QuotationMapper ตาม role แทน
                    UnitCost = templateLine.UnitCost ?? 0m,
                    DiscountPercent = templateLine.DiscountPercent,
                    Promotion = templateLine.Promotion,
                    AssignedTechnicianId = null,
                    Note = templateLine.Note,
                    StandardHours = templateLine.Type == LineType.Labor ? templateLine.StandardHours : null
                };
            }
            else
            {
                var item = catalogByCode[templateLine.CatalogCode];
                newLine = new QuotationLine
                {
                    QuotationId = q.Id,
                    Sequence = nextSequence++,
                    CatalogCode = item.Code,
                    Name = item.Name,
                    Type = item.Type,
                    Source = request.Source ?? templateLine.Source,
                    Quantity = templateLine.Quantity,
                    Unit = item.Unit,
                    UnitPrice = templateLine.UnitPrice ?? item.Price,
                    UnitCost = item.Cost,
                    DiscountPercent = templateLine.DiscountPercent,
                    Promotion = templateLine.Promotion,
                    AssignedTechnicianId = null,
                    Note = templateLine.Note,
                    StandardHours = item.StandardHours
                };
            }

            q.Lines.Add(newLine);
            addedCount++;
        }

        // [SECURITY] ห้ามใส่ต้นทุน/กำไรในข้อความ event — timeline แสดงให้ทุก role เห็น (invariant #7)
        return await PersistAsync(q, "quotation.lines.from_template",
            $"เพิ่ม {addedCount} รายการจากเทมเพลต {template.Code} {template.Name}", ct);
    }

    public async Task<Result<QuotationDto>> UpdateLineAsync(
        Guid id, Guid lineId, UpsertLineRequest request, CancellationToken ct = default)
    {
        var (q, error) = await LoadEditableAsync(id, ct);
        if (error is not null) return error;

        var line = q!.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result<QuotationDto>.Fail("QUOTE_LINE_NOT_FOUND", "ไม่พบรายการนี้ในใบเสนอราคา");

        var isAdHoc = string.IsNullOrWhiteSpace(line.CatalogCode);

        // [BIZ] ชื่อ/หน่วย/ต้นทุน แก้ได้เฉพาะรายการนอกแคตตาล็อกเท่านั้น — บรรทัดที่มาจากแคตตาล็อกเป็น snapshot ที่ล็อกไว้
        if (isAdHoc)
        {
            if (!string.IsNullOrWhiteSpace(request.Name)) line.Name = request.Name.Trim();
            if (!string.IsNullOrWhiteSpace(request.Unit)) line.Unit = request.Unit.Trim();
            if (user.CanSeeCost && request.UnitCost.HasValue) line.UnitCost = request.UnitCost.Value;
            if (line.Type == LineType.Labor && request.StandardHours.HasValue)
                line.StandardHours = request.StandardHours;
        }

        line.Quantity = request.Quantity;
        if (request.UnitPrice.HasValue) line.UnitPrice = request.UnitPrice.Value;
        line.DiscountPercent = request.DiscountPercent;
        line.Promotion = request.Promotion;
        line.Source = request.Source;
        line.AssignedTechnicianId = request.AssignedTechnicianId;
        line.Note = request.Note;

        await AssignTechnicianNameAsync(line, ct);

        return await PersistAsync(q, "quotation.line.updated", $"แก้ไขรายการ {line.Name}", ct);
    }

    public async Task<Result<QuotationDto>> RemoveLineAsync(Guid id, Guid lineId, CancellationToken ct = default)
    {
        var (q, error) = await LoadEditableAsync(id, ct);
        if (error is not null) return error;

        var line = q!.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result<QuotationDto>.Fail("QUOTE_LINE_NOT_FOUND", "ไม่พบรายการนี้ในใบเสนอราคา");

        q.Lines.Remove(line);
        return await PersistAsync(q, "quotation.line.removed", $"ลบรายการ {line.Name}", ct);
    }

    public async Task<Result<QuotationValidationDto>> ValidateAsync(Guid id, CancellationToken ct = default)
    {
        var q = await repository.GetWithLinesAsync(id, ct);
        if (q is null) return Result<QuotationValidationDto>.Fail("QUOTE_NOT_FOUND", "ไม่พบใบเสนอราคา");

        QuotationCalculator.ApplyQuotationTotals(q);
        var result = QuotationValidator.ValidateForSend(q, user.Role);
        return Result<QuotationValidationDto>.Ok(QuotationMapper.ToDto(result));
    }

    public async Task<Result<QuotationDto>> SendAsync(Guid id, CancellationToken ct = default)
    {
        var (q, error) = await LoadEditableAsync(id, ct);
        if (error is not null) return error;

        QuotationCalculator.ApplyQuotationTotals(q!);

        var validation = QuotationValidator.ValidateForSend(q!, user.Role);
        if (!validation.IsValid)
            return Result<QuotationDto>.Fail(new ApiError(
                "QUOTE_VALIDATION_FAILED",
                "ใบเสนอราคายังส่งไม่ได้ — มี " + validation.Errors.Count + " ข้อที่ต้องแก้",
                Details: QuotationMapper.ToDto(validation)));

        q!.Status = QuotationStatus.Sent;
        q.SentAt = Now;
        q.ValidUntil ??= Now.AddDays(7);
        ReleaseLock(q);

        return await PersistAsync(q, "quotation.sent",
            $"ส่งใบเสนอราคา {q.Code} ให้ลูกค้า · {q.Lines.Count} รายการ · {q.TotalAmount:N2} บาท", ct);
    }

    /// <summary>
    /// ออกฉบับแก้ไข — [BIZ] เวอร์ชันเดิมเป็น Superseded, ลายเซ็นเดิมเป็นโมฆะ,
    /// ทุกบรรทัดในเวอร์ชันใหม่กลับเป็น Pending
    /// </summary>
    public async Task<Result<QuotationDto>> ReviseAsync(
        Guid id, ReviseQuotationRequest request, CancellationToken ct = default)
    {
        var previous = await repository.GetWithLinesAsync(id, ct);
        if (previous is null) return NotFound();
        if (!BelongsToCurrentScope(previous)) return Forbidden();

        if (previous.Status is QuotationStatus.Superseded)
            return Result<QuotationDto>.Fail("QUOTE_ALREADY_SUPERSEDED",
                $"{previous.Code} ถูกแทนที่ไปแล้ว — ให้ออกฉบับแก้ไขจากเวอร์ชันล่าสุดแทน");

        if (string.IsNullOrWhiteSpace(request.RevisionReason))
            return Result<QuotationDto>.Fail("QUOTE_REVISION_NO_REASON",
                "ต้องระบุเหตุผลที่ออกฉบับแก้ไข", nameof(request.RevisionReason));

        var version = await repository.GetNextVersionAsync(previous.JobId, ct);

        var revision = new Quotation
        {
            Code = FormatCode(previous.JobNo, version),
            Version = version,
            Status = QuotationStatus.Draft,
            JobId = previous.JobId,
            JobNo = previous.JobNo,
            CustomerName = previous.CustomerName,
            CustomerPhone = previous.CustomerPhone,
            CustomerTaxId = previous.CustomerTaxId,
            CustomerAddress = previous.CustomerAddress,
            VehicleRegistration = previous.VehicleRegistration,
            VehicleModel = previous.VehicleModel,
            VehicleVin = previous.VehicleVin,
            VehicleMileage = previous.VehicleMileage,
            BranchName = previous.BranchName,
            BranchAddress = previous.BranchAddress,
            BranchTaxId = previous.BranchTaxId,
            BranchPhone = previous.BranchPhone,
            DepositAmount = previous.DepositAmount,
            VatRate = previous.VatRate,
            SupersedesQuotationId = previous.Id,
            RevisionReason = request.RevisionReason,
            CreatedByUserId = user.UserId,
            CreatedByUserName = user.UserName,
            CreatedAt = Now
        };

        // คัดลอกบรรทัด — [BIZ] การอนุมัติเดิมใช้ไม่ได้ ทุกบรรทัดกลับเป็นรออนุมัติ
        foreach (var source in previous.Lines.OrderBy(l => l.Sequence))
        {
            revision.Lines.Add(new QuotationLine
            {
                QuotationId = revision.Id,
                Sequence = source.Sequence,
                CatalogCode = source.CatalogCode,
                Name = source.Name,
                Type = source.Type,
                Source = source.Source,
                InspectionItemId = source.InspectionItemId,
                Quantity = source.Quantity,
                Unit = source.Unit,
                UnitPrice = source.UnitPrice,
                UnitCost = source.UnitCost,
                DiscountPercent = source.DiscountPercent,
                Promotion = source.Promotion,
                AssignedTechnicianId = source.AssignedTechnicianId,
                AssignedTechnicianName = source.AssignedTechnicianName,
                Note = source.Note,
                StandardHours = source.StandardHours,
                ApprovalStatus = LineApprovalStatus.Pending,
                RejectReason = null,
                DecidedAt = null
            });
        }

        previous.Status = QuotationStatus.Superseded;
        previous.SupersededByQuotationId = revision.Id;
        previous.LastUpdatedByUserId = user.UserId;
        previous.LastUpdatedAt = Now;

        QuotationCalculator.ApplyQuotationTotals(revision);

        await repository.AddAsync(revision, ct);
        await LogAsync(revision, "quotation.revised",
            $"ออกฉบับแก้ไข {revision.Code} แทน {previous.Code} · เหตุผล: {request.RevisionReason} " +
            "· การอนุมัติเดิมเป็นโมฆะ", ct);
        await repository.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(QuotationMapper.ToDto(revision, user, Now));
    }

    public async Task<Result<QuotationDto>> DecideLineAsync(
        Guid id, Guid lineId, LineDecisionRequest request, CancellationToken ct = default)
    {
        var q = await repository.GetWithLinesAsync(id, ct);
        if (q is null) return NotFound();
        if (!BelongsToCurrentScope(q)) return Forbidden();

        if (q.Status is not (QuotationStatus.Sent or QuotationStatus.Partial))
            return Result<QuotationDto>.Fail("QUOTE_NOT_OPEN_FOR_DECISION",
                $"ใบเสนอราคาอยู่ในสถานะ {QuotationMapper.StatusLabel(q.Status)} — รับการตัดสินใจไม่ได้");

        var line = q.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result<QuotationDto>.Fail("QUOTE_LINE_NOT_FOUND", "ไม่พบรายการนี้ในใบเสนอราคา");

        // [BIZ] ไม่อนุมัติต้องมีเหตุผลเสมอ
        if (request.Decision == LineApprovalStatus.Rejected && string.IsNullOrWhiteSpace(request.RejectReason))
            return Result<QuotationDto>.Fail("QUOTE_REJECT_NO_REASON",
                "กรุณาระบุเหตุผลที่ไม่อนุมัติรายการนี้", nameof(request.RejectReason));

        line.ApprovalStatus = request.Decision;
        line.RejectReason = request.Decision == LineApprovalStatus.Rejected ? request.RejectReason : null;
        line.DecidedAt = Now;

        return await PersistAsync(q, "quotation.line.decided",
            $"ลูกค้า{(request.Decision == LineApprovalStatus.Approved ? "อนุมัติ" : "ไม่อนุมัติ")} “{line.Name}”", ct);
    }

    public async Task<Result<QuotationDto>> SignAsync(
        Guid id, SignQuotationRequest request, CancellationToken ct = default)
    {
        var q = await repository.GetWithLinesAsync(id, ct);
        if (q is null) return NotFound();
        if (!BelongsToCurrentScope(q)) return Forbidden();

        if (q.Status is not (QuotationStatus.Sent or QuotationStatus.Partial))
            return Result<QuotationDto>.Fail("QUOTE_NOT_OPEN_FOR_DECISION",
                $"ใบเสนอราคาอยู่ในสถานะ {QuotationMapper.StatusLabel(q.Status)} — เซ็นยืนยันไม่ได้");

        QuotationCalculator.ApplyQuotationTotals(q);

        var validation = QuotationValidator.ValidateForSign(q);
        if (!validation.IsValid)
            return Result<QuotationDto>.Fail(new ApiError(
                "QUOTE_SIGN_VALIDATION_FAILED",
                "ยังเซ็นยืนยันไม่ได้",
                Details: QuotationMapper.ToDto(validation)));

        var totals = QuotationCalculator.CalculateApprovedTotals(q);

        q.Approval = new QuotationApproval
        {
            QuotationId = q.Id,
            QuotationVersion = q.Version,   // [BIZ] ลายเซ็นผูกกับเวอร์ชันนี้เท่านั้น
            SignatureImagePath = request.SignatureImagePath,
            SignedAt = Now,
            ConsentText = request.ConsentText,
            DeviceInfo = request.DeviceInfo,
            WitnessEmployeeId = request.WitnessEmployeeId,
            WitnessEmployeeName = request.WitnessEmployeeName,
            ApprovedLineCount = totals.ApprovedCount,
            RejectedLineCount = totals.RejectedCount,
            ApprovedNetAmount = totals.NetAmount
        };

        q.Status = totals.RejectedCount == 0 ? QuotationStatus.Approved : QuotationStatus.Partial;

        return await PersistAsync(q, "quotation.signed",
            $"ลูกค้าเซ็นยืนยัน {q.Code} · อนุมัติ {totals.ApprovedCount} ไม่อนุมัติ {totals.RejectedCount} " +
            $"· {totals.TotalAmount:N2} บาท", ct);
    }

    // ---------- helper ----------

    private Quotation NewQuotation(
        Job job, LegacyBranchDto? branch, int version, DateTime? validUntil, decimal deposit) => new()
    {
        Code = FormatCode(job.JobNo, version),
        Version = version,
        Status = QuotationStatus.Draft,
        JobId = job.Id,
        JobNo = job.JobNo,
        CustomerName = job.CustomerName,
        CustomerPhone = job.CustomerPhone,
        VehicleRegistration = job.VehicleRegistration,
        VehicleModel = job.VehicleModel,
        VehicleVin = job.VehicleVin,
        BranchName = branch?.Name ?? job.BranchName,
        BranchAddress = branch?.Address,
        BranchTaxId = branch?.TaxId,
        BranchPhone = branch?.Phone,
        DepositAmount = deposit,
        ValidUntil = validUntil,
        CreatedByUserId = user.UserId,
        CreatedByUserName = user.UserName,
        CreatedAt = Now
    };

    /// <summary>QT-{เลขงาน}-{เวอร์ชัน 2 หลัก} เช่น QT-7042-01</summary>
    private static string FormatCode(string jobNo, int version)
    {
        var suffix = jobNo.Contains('-') ? jobNo[(jobNo.LastIndexOf('-') + 1)..] : jobNo;
        return $"QT-{suffix}-{version:D2}";
    }

    private async Task<(Quotation? Quotation, Result<QuotationDto>? Error)> LoadEditableAsync(
        Guid id, CancellationToken ct)
    {
        var q = await repository.GetWithLinesAsync(id, ct);
        if (q is null) return (null, NotFound());
        if (!BelongsToCurrentScope(q)) return (null, Forbidden());

        if (!q.IsEditable)
            return (null, Result<QuotationDto>.Fail("QUOTE_NOT_EDITABLE",
                $"ใบเสนอราคาอยู่ในสถานะ {QuotationMapper.StatusLabel(q.Status)} — แก้ไขไม่ได้ " +
                "ถ้าต้องการเปลี่ยนราคาให้ออกฉบับแก้ไข"));

        // [UI] แก้พร้อมกัน — คนที่ไม่ได้ถือสิทธิ์เห็นเป็น read-only
        if (q.LockedByUserId is not null && q.LockedByUserId != user.UserId)
            return (null, Result<QuotationDto>.Fail("QUOTE_LOCKED_BY_OTHER",
                $"{q.LockedByUserName} กำลังแก้ไขใบนี้อยู่ — รอสักครู่หรือขอสิทธิ์แก้ไข"));

        return (q, null);
    }

    private async Task<Result<QuotationDto>> PersistAsync(
        Quotation q, string eventType, string description, CancellationToken ct)
    {
        QuotationCalculator.ApplyQuotationTotals(q);
        q.LastUpdatedByUserId = user.UserId;
        q.LastUpdatedAt = Now;

        await LogAsync(q, eventType, description, ct);
        await repository.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(QuotationMapper.ToDto(q, user, Now));
    }

    private Task LogAsync(Quotation q, string eventType, string description, CancellationToken ct) =>
        repository.AddEventAsync(new ActivityEvent
        {
            JobId = q.JobId,
            EntityId = q.Id,
            EntityType = nameof(Quotation),
            EventType = eventType,
            DescriptionTh = description,
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,        // [BIZ] มือถือ/เว็บ/ระบบ — บังคับทุก event
            OccurredAt = Now
        }, ct);

    /// <summary>
    /// ตรวจฟิลด์บังคับขั้นต่ำของรายการนอกแคตตาล็อก — docs/07-quotation-adhoc-line.md
    /// ประเภท + ชื่อ + ราคา/หน่วย เท่านั้นที่บังคับ (ต่างจากแคตตาล็อกที่ fallback ไปราคา/หน่วยของสินค้าได้)
    /// </summary>
    private static Result<QuotationDto>? ValidateAdHocRequest(UpsertLineRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return Result<QuotationDto>.Fail("QUOTE_LINE_NAME_REQUIRED",
                "กรุณาระบุชื่อรายการนอกแคตตาล็อก", nameof(request.Name));

        if (request.Name.Trim().Length > 300)
            return Result<QuotationDto>.Fail("QUOTE_LINE_NAME_TOO_LONG",
                "ชื่อรายการยาวเกินไป — ไม่เกิน 300 ตัวอักษร", nameof(request.Name));

        if (request.Type is null)
            return Result<QuotationDto>.Fail("QUOTE_LINE_TYPE_REQUIRED",
                "กรุณาเลือกประเภทรายการนอกแคตตาล็อก (อะไหล่/ค่าแรง)", nameof(request.Type));

        if (request.UnitPrice is null or <= 0m)
            return Result<QuotationDto>.Fail("QUOTE_LINE_PRICE_REQUIRED",
                "กรุณาระบุราคา/หน่วยของรายการนอกแคตตาล็อก", nameof(request.UnitPrice));

        return null;
    }

    /// <summary>หน่วยเริ่มต้นของรายการนอกแคตตาล็อกเมื่อไม่ได้ระบุ — "ชิ้น" สำหรับอะไหล่ "งาน" สำหรับค่าแรง</summary>
    private static string ResolveAdHocUnit(UpsertLineRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Unit))
        {
            var trimmed = request.Unit.Trim();
            return trimmed.Length > 40 ? trimmed[..40] : trimmed;
        }

        return request.Type == LineType.Labor ? "งาน" : "ชิ้น";
    }

    private async Task AssignTechnicianNameAsync(QuotationLine line, CancellationToken ct)
    {
        if (line.AssignedTechnicianId is null)
        {
            line.AssignedTechnicianName = null;
            return;
        }

        var technicians = await legacy.GetTechniciansAsync(user.ShardKey, user.BranchId, ct);
        line.AssignedTechnicianName = technicians
            .FirstOrDefault(t => t.StaffId == line.AssignedTechnicianId)?.Name;
    }

    private static void ReleaseLock(Quotation q)
    {
        q.LockedByUserId = null;
        q.LockedByUserName = null;
        q.LockedAt = null;
    }

    private bool BelongsToCurrentScope(Quotation q) =>
        q.Job is not null && q.Job.LegacyShardKey == user.ShardKey && q.Job.BranchId == user.BranchId;

    private static Result<QuotationDto> NotFound() =>
        Result<QuotationDto>.Fail("QUOTE_NOT_FOUND", "ไม่พบใบเสนอราคาที่ระบุ");

    private static Result<QuotationDto> Forbidden() =>
        Result<QuotationDto>.Fail("QUOTE_OTHER_SCOPE", "ใบเสนอราคานี้อยู่คนละสาขาหรือคนละระบบกับที่คุณเข้าใช้งานอยู่");
}
