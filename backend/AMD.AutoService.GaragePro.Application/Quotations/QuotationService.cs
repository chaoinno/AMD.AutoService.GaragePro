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
        string? statusFilter, long? jobId = null, CancellationToken ct = default);
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
}

public sealed class QuotationService(
    IQuotationRepository repository,
    ICatalogRepository catalog,
    ILegacyReader legacy,
    ICurrentUser user,
    TimeProvider clock) : IQuotationService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<IReadOnlyList<QuotationSummaryDto>>> GetQueueAsync(
        string? statusFilter, long? jobId = null, CancellationToken ct = default)
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
        var job = await legacy.GetJobAsync(user.ShardKey, request.JobId, ct);
        if (job is null)
            return Result<QuotationDto>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {request.JobId} ในสาขานี้");

        if (job.BranchId != user.BranchId)
            return Result<QuotationDto>.Fail("JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        // [BIZ] มีใบที่ยังไม่ถูกแทนที่อยู่แล้ว ต้องใช้ "ออกฉบับแก้ไข" ไม่ใช่สร้างใหม่
        var existing = await repository.GetLatestForJobAsync(user.ShardKey, request.JobId, ct);
        if (existing is not null && existing.Status is not (QuotationStatus.Rejected or QuotationStatus.Superseded))
            return Result<QuotationDto>.Fail(
                "QUOTE_ALREADY_EXISTS",
                $"งานนี้มีใบเสนอราคา {existing.Code} อยู่แล้ว — ถ้าต้องการแก้ราคาให้ออกฉบับแก้ไขแทน");

        var branch = await legacy.GetBranchAsync(user.ShardKey, user.BranchId, ct);
        var version = await repository.GetNextVersionAsync(user.ShardKey, request.JobId, ct);

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

        var items = await catalog.GetByCodesAsync(user.ShardKey, user.BranchId, [request.CatalogCode], ct);
        var item = items.FirstOrDefault();
        if (item is null)
            return Result<QuotationDto>.Fail("CATALOG_ITEM_NOT_FOUND",
                $"ไม่พบรหัส {request.CatalogCode} ในแคตตาล็อกของสาขานี้");

        var line = new QuotationLine
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

        await AssignTechnicianNameAsync(line, ct);

        q.Lines.Add(line);
        return await PersistAsync(q, "quotation.line.added", $"เพิ่มรายการ {item.Name}", ct);
    }

    public async Task<Result<QuotationDto>> UpdateLineAsync(
        Guid id, Guid lineId, UpsertLineRequest request, CancellationToken ct = default)
    {
        var (q, error) = await LoadEditableAsync(id, ct);
        if (error is not null) return error;

        var line = q!.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
            return Result<QuotationDto>.Fail("QUOTE_LINE_NOT_FOUND", "ไม่พบรายการนี้ในใบเสนอราคา");

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

        var version = await repository.GetNextVersionAsync(previous.LegacyShardKey, previous.LegacyJobId, ct);

        var revision = new Quotation
        {
            Code = FormatCode(previous.JobNo, version),
            Version = version,
            Status = QuotationStatus.Draft,
            LegacyShardKey = previous.LegacyShardKey,
            LegacyBranchId = previous.LegacyBranchId,
            LegacyJobId = previous.LegacyJobId,
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
        LegacyJobDto job, LegacyBranchDto? branch, int version, DateTime? validUntil, decimal deposit) => new()
    {
        Code = FormatCode(job.JobNo, version),
        Version = version,
        Status = QuotationStatus.Draft,
        LegacyShardKey = user.ShardKey,
        LegacyBranchId = job.BranchId,
        LegacyJobId = job.JobId,
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
            LegacyShardKey = q.LegacyShardKey,
            LegacyBranchId = q.LegacyBranchId,
            LegacyJobId = q.LegacyJobId,
            EntityId = q.Id,
            EntityType = nameof(Quotation),
            EventType = eventType,
            DescriptionTh = description,
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,        // [BIZ] มือถือ/เว็บ/ระบบ — บังคับทุก event
            OccurredAt = Now
        }, ct);

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
        q.LegacyShardKey == user.ShardKey && q.LegacyBranchId == user.BranchId;

    private static Result<QuotationDto> NotFound() =>
        Result<QuotationDto>.Fail("QUOTE_NOT_FOUND", "ไม่พบใบเสนอราคาที่ระบุ");

    private static Result<QuotationDto> Forbidden() =>
        Result<QuotationDto>.Fail("QUOTE_OTHER_SCOPE", "ใบเสนอราคานี้อยู่คนละสาขาหรือคนละระบบกับที่คุณเข้าใช้งานอยู่");
}
