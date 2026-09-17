using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.QuotationTemplates;

public interface IQuotationTemplateService
{
    /// <summary>เปิดให้ทุก role อ่านได้ — ตัวเลือกเทมเพลตในหน้าจ๊อบ/ใบเสนอราคาต้องใช้ (คนละเรื่องกับสิทธิ์แก้ไข)</summary>
    Task<Result<IReadOnlyList<QuotationTemplateDto>>> SearchAsync(
        string? keyword, bool includeInactive, CancellationToken ct = default);

    Task<Result<QuotationTemplateDto>> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<QuotationTemplateDto>> CreateAsync(QuotationTemplateUpsertRequest request, CancellationToken ct = default);
    Task<Result<QuotationTemplateDto>> UpdateAsync(Guid id, QuotationTemplateUpsertRequest request, CancellationToken ct = default);
    Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default);
}

/// <summary>
/// เทมเพลตใบเสนอราคา — สร้าง/แก้ไข/เปิดปิดใช้งาน = ผู้จัดการเท่านั้น (EnsureManager) · อ่าน = ทุก role
/// อ้างอิง: docs/08-quotation-template.md
/// </summary>
public sealed class QuotationTemplateService(
    IQuotationTemplateRepository repository, ICatalogRepository catalog, ICurrentUser user, TimeProvider clock)
    : IQuotationTemplateService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<IReadOnlyList<QuotationTemplateDto>>> SearchAsync(
        string? keyword, bool includeInactive, CancellationToken ct = default)
    {
        var items = await repository.SearchAsync(user.ShardKey, user.BranchId, MasterDataSupport.Clean(keyword), includeInactive, ct);
        // รายการ (list) ไม่คำนวณสถานะแคตตาล็อกสดต่อบรรทัด (จะกลายเป็น N query ต่อเทมเพลต) — มีแค่ตอนดูรายละเอียด
        return Result<IReadOnlyList<QuotationTemplateDto>>.Ok(
            items.Select(t => QuotationTemplateMapper.ToDto(t, user.CanSeeCost)).ToList());
    }

    public async Task<Result<QuotationTemplateDto>> GetAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await repository.GetWithLinesAsync(user.ShardKey, user.BranchId, id, ct);
        if (entity is null)
            return Result<QuotationTemplateDto>.Fail("QUOTE_TEMPLATE_NOT_FOUND", "ไม่พบเทมเพลตใบเสนอราคานี้ในสาขาปัจจุบัน");

        var codes = entity.Lines.Where(l => !string.IsNullOrWhiteSpace(l.CatalogCode)).Select(l => l.CatalogCode).Distinct().ToList();
        var catalogByCode = await ResolveCatalogAsync(codes, ct);

        return Result<QuotationTemplateDto>.Ok(
            QuotationTemplateMapper.ToDto(entity, user.CanSeeCost, catalogByCode, includeCatalogStatus: true));
    }

    public async Task<Result<QuotationTemplateDto>> CreateAsync(
        QuotationTemplateUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<QuotationTemplateDto>.Fail(forbidden);

        var normalized = Normalize(request);
        var headerError = ValidateHeader(normalized);
        if (headerError is not null) return Result<QuotationTemplateDto>.Fail(headerError);

        var linesResult = await ValidateAndBuildLinesAsync(normalized.Lines, ct);
        if (!linesResult.Success) return Result<QuotationTemplateDto>.Fail(linesResult.Error!);

        if (await repository.CodeExistsAsync(user.ShardKey, user.BranchId, normalized.Code, null, ct))
            return Result<QuotationTemplateDto>.Fail("QUOTE_TEMPLATE_CODE_DUPLICATE", "รหัสเทมเพลตนี้มีอยู่แล้ว", "code");

        var entity = new QuotationTemplate
        {
            Code = normalized.Code, Name = normalized.Name, Description = normalized.Description,
            LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId,
            CreatedDate = Now, LastUpdated = Now, Lines = linesResult.Data!
        };
        foreach (var line in entity.Lines) line.QuotationTemplateId = entity.Id;

        await repository.AddAsync(entity, ct);
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(QuotationTemplate),
            "quotation_template.created", $"สร้างเทมเพลตใบเสนอราคา {entity.Code} {entity.Name}", Now), ct);

        var saveError = await SaveAsync<QuotationTemplateDto>(ct);
        return saveError ?? Result<QuotationTemplateDto>.Ok(QuotationTemplateMapper.ToDto(entity, user.CanSeeCost));
    }

    public async Task<Result<QuotationTemplateDto>> UpdateAsync(
        Guid id, QuotationTemplateUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<QuotationTemplateDto>.Fail(forbidden);

        var entity = await repository.GetWithLinesAsync(user.ShardKey, user.BranchId, id, ct);
        if (entity is null)
            return Result<QuotationTemplateDto>.Fail("QUOTE_TEMPLATE_NOT_FOUND", "ไม่พบเทมเพลตใบเสนอราคานี้ในสาขาปัจจุบัน");

        var normalized = Normalize(request);
        var headerError = ValidateHeader(normalized);
        if (headerError is not null) return Result<QuotationTemplateDto>.Fail(headerError);

        var linesResult = await ValidateAndBuildLinesAsync(normalized.Lines, ct);
        if (!linesResult.Success) return Result<QuotationTemplateDto>.Fail(linesResult.Error!);

        if (await repository.CodeExistsAsync(user.ShardKey, user.BranchId, normalized.Code, id, ct))
            return Result<QuotationTemplateDto>.Fail("QUOTE_TEMPLATE_CODE_DUPLICATE", "รหัสเทมเพลตนี้มีอยู่แล้ว", "code");

        // แทนที่บรรทัดทั้งชุด — ไม่มีอะไรอ้างอิงบรรทัดเทมเพลตจากภายนอก (snapshot ตอนนำไปใช้แล้ว) diff จึงไม่คุ้ม
        repository.RemoveLines(entity.Lines);
        entity.Lines.Clear();
        foreach (var line in linesResult.Data!)
        {
            line.QuotationTemplateId = entity.Id;
            entity.Lines.Add(line);
        }

        entity.Code = normalized.Code;
        entity.Name = normalized.Name;
        entity.Description = normalized.Description;
        entity.LastUpdated = Now;

        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(QuotationTemplate),
            "quotation_template.updated", $"แก้ไขเทมเพลตใบเสนอราคา {entity.Code} {entity.Name}", Now), ct);

        var saveError = await SaveAsync<QuotationTemplateDto>(ct);
        return saveError ?? Result<QuotationTemplateDto>.Ok(QuotationTemplateMapper.ToDto(entity, user.CanSeeCost));
    }

    public async Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<bool>.Fail(forbidden);

        var entity = await repository.GetWithLinesAsync(user.ShardKey, user.BranchId, id, ct);
        if (entity is null)
            return Result<bool>.Fail("QUOTE_TEMPLATE_NOT_FOUND", "ไม่พบเทมเพลตใบเสนอราคานี้ในสาขาปัจจุบัน");

        entity.IsActive = request.IsActive;
        entity.LastUpdated = Now;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(QuotationTemplate),
            request.IsActive ? "quotation_template.activated" : "quotation_template.deactivated",
            $"{(request.IsActive ? "เปิด" : "ปิด")}ใช้งานเทมเพลต {entity.Code} {entity.Name}", Now), ct);
        await repository.SaveChangesAsync(ct);
        return Result<bool>.Ok(true);
    }

    private async Task<Result<List<QuotationTemplateLine>>> ValidateAndBuildLinesAsync(
        IReadOnlyList<QuotationTemplateLineInput> inputs, CancellationToken ct)
    {
        if (inputs.Count == 0)
            return Result<List<QuotationTemplateLine>>.Fail("QUOTE_TEMPLATE_NO_LINES", "เทมเพลตต้องมีอย่างน้อย 1 รายการ");

        var lines = new List<QuotationTemplateLine>();
        var n = 0;
        foreach (var input in inputs)
        {
            n++;
            var code = input.CatalogCode?.Trim() ?? string.Empty;
            var isAdHoc = string.IsNullOrWhiteSpace(code);

            if (input.Quantity <= 0m)
                return Result<List<QuotationTemplateLine>>.Fail(
                    "QUOTE_TEMPLATE_LINE_QTY_INVALID", $"รายการลำดับที่ {n} จำนวนต้องมากกว่า 0");

            string name;
            LineType type;
            string? unit = MasterDataSupport.Clean(input.Unit);

            if (isAdHoc)
            {
                if (string.IsNullOrWhiteSpace(input.Name))
                    return Result<List<QuotationTemplateLine>>.Fail(
                        "QUOTE_TEMPLATE_LINE_NAME_REQUIRED", $"รายการนอกแคตตาล็อกลำดับที่ {n} ยังไม่ได้ระบุชื่อ");
                if (input.Type is null)
                    return Result<List<QuotationTemplateLine>>.Fail(
                        "QUOTE_TEMPLATE_LINE_TYPE_REQUIRED",
                        $"รายการนอกแคตตาล็อกลำดับที่ {n} ยังไม่ได้เลือกประเภท (อะไหล่/ค่าแรง)");
                if (input.UnitPrice is null or <= 0m)
                    return Result<List<QuotationTemplateLine>>.Fail(
                        "QUOTE_TEMPLATE_LINE_PRICE_REQUIRED", $"รายการนอกแคตตาล็อกลำดับที่ {n} ยังไม่ได้ระบุราคา/หน่วย");

                name = input.Name.Trim();
                type = input.Type.Value;
                unit ??= type == LineType.Labor ? "งาน" : "ชิ้น";
            }
            else
            {
                // บรรทัดแคตตาล็อก: Name/Type เป็นแค่ cache แสดงผล ใช้ค่าที่ส่งมา (จาก client ที่เพิ่งค้นหาเจอ) หรือ
                // fallback เป็นรหัสเฉยๆ ถ้าไม่ได้ส่งมา — ไม่กระทบตอนนำไปใช้จริงเพราะอ่านสดจากแคตตาล็อกเสมอ
                name = string.IsNullOrWhiteSpace(input.Name) ? code : input.Name.Trim();
                type = input.Type ?? LineType.Part;
            }

            lines.Add(new QuotationTemplateLine
            {
                Sequence = n,
                CatalogCode = code,
                Name = name,
                Type = type,
                Unit = unit,
                Quantity = input.Quantity,
                UnitPrice = input.UnitPrice,
                UnitCost = isAdHoc ? input.UnitCost : null,
                StandardHours = isAdHoc && type == LineType.Labor ? input.StandardHours : null,
                DiscountPercent = input.DiscountPercent,
                Promotion = input.Promotion,
                Source = input.Source,
                Note = MasterDataSupport.Clean(input.Note)
            });
        }

        // [BIZ] ห้ามมีรายการซ้ำในเทมเพลตเดียวกัน — กฎเดียวกับ QuotationValidator.ValidateForSend
        var duplicates = QuotationValidator.FindDuplicateGroups(lines.Select(l => (l.CatalogCode, l.Name)));
        if (duplicates.HasAny)
        {
            var key = duplicates.CodeDuplicates.FirstOrDefault().Key ?? duplicates.NameDuplicates.First().Key;
            return Result<List<QuotationTemplateLine>>.Fail(
                "QUOTE_TEMPLATE_DUPLICATE_LINE", $"เทมเพลตมีรายการซ้ำกัน: {key} — รวมเป็นบรรทัดเดียวก่อนบันทึก");
        }

        // รหัสแคตตาล็อกทุกตัวต้องมีอยู่จริงในสาขานี้ ณ ตอนบันทึก — กันเทมเพลตพังตั้งแต่ต้น
        var codesToCheck = lines.Where(l => !string.IsNullOrWhiteSpace(l.CatalogCode))
            .Select(l => l.CatalogCode).Distinct().ToList();
        if (codesToCheck.Count > 0)
        {
            var found = await catalog.GetByCodesAsync(user.ShardKey, user.BranchId, codesToCheck, ct);
            var foundCodes = found.Select(c => c.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = codesToCheck.Where(c => !foundCodes.Contains(c)).ToList();
            if (missing.Count > 0)
                return Result<List<QuotationTemplateLine>>.Fail(
                    "QUOTE_TEMPLATE_ITEM_MISSING",
                    $"รหัส {string.Join(", ", missing)} ไม่มีในแคตตาล็อกของสาขานี้ — แก้ไขหรือลบรายการนั้นก่อนบันทึก");
        }

        return Result<List<QuotationTemplateLine>>.Ok(lines);
    }

    private async Task<IReadOnlyDictionary<string, CatalogItem>> ResolveCatalogAsync(
        IReadOnlyList<string> codes, CancellationToken ct)
    {
        if (codes.Count == 0) return new Dictionary<string, CatalogItem>();
        var items = await catalog.GetByCodesAsync(user.ShardKey, user.BranchId, codes, ct);
        return items.ToDictionary(c => c.Code, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<Result<T>?> SaveAsync<T>(CancellationToken ct)
    {
        try { await repository.SaveChangesAsync(ct); return null; }
        catch (MasterDataConflictException ex)
        {
            return Result<T>.Fail(MasterDataSupport.ConflictCode(ex, "QUOTE_TEMPLATE_CODE_DUPLICATE"), "รหัสเทมเพลตนี้มีอยู่แล้ว");
        }
    }

    private static QuotationTemplateUpsertRequest Normalize(QuotationTemplateUpsertRequest x) => x with
    {
        Code = MasterDataSupport.Code(x.Code),
        Name = x.Name.Trim(),
        Description = MasterDataSupport.Clean(x.Description)
    };

    private static ApiError? ValidateHeader(QuotationTemplateUpsertRequest x) =>
        MasterDataSupport.Required(x.Code, 30, "QUOTE_TEMPLATE", "รหัสเทมเพลต", "code") ??
        MasterDataSupport.Required(x.Name, 200, "QUOTE_TEMPLATE", "ชื่อเทมเพลต", "name") ??
        MasterDataSupport.Optional(x.Description, 500, "QUOTE_TEMPLATE", "คำอธิบาย", "description");
}
