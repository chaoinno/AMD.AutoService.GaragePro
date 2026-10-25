using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.MasterData;
using AMD.AutoService.GaragePro.Domain.Common;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.ServiceReminders;

public interface IServiceReminderTemplateService
{
    /// <summary>
    /// เปิดให้ทุก role อ่านได้ (หน้าส่งมอบรถต้องใช้เลือกรายการ) — สาขาที่ยังไม่เคยมีรายการ
    /// จะได้ชุดตั้งต้น 6 รายการให้เองตอนเรียกครั้งแรก
    /// </summary>
    Task<Result<IReadOnlyList<ServiceReminderTemplateDto>>> SearchAsync(bool includeInactive, CancellationToken ct = default);

    Task<Result<ServiceReminderTemplateDto>> CreateAsync(
        ServiceReminderTemplateUpsertRequest request, CancellationToken ct = default);

    Task<Result<ServiceReminderTemplateDto>> UpdateAsync(
        Guid id, ServiceReminderTemplateUpsertRequest request, CancellationToken ct = default);

    Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default);
}

/// <summary>
/// รายการนัดบริการตั้งต้นของสาขา — สร้าง/แก้ไข/เปิดปิด = ผู้จัดการเท่านั้น (<c>MASTER_DATA_MANAGE_FORBIDDEN</c>
/// แบบเทมเพลตใบเสนอราคา) · อ่าน = ทุก role · ไม่มีลบ (ปิดใช้งานแทน)
/// </summary>
public sealed class ServiceReminderTemplateService(
    IServiceReminderTemplateRepository repository, ICurrentUser user, TimeProvider clock)
    : IServiceReminderTemplateService
{
    /// <summary>ชุดตั้งต้น (ยืนยันกับเจ้าของระบบ 2026-10-25) — ชื่อ, ไมล์ที่นัดห่างจากไมล์ส่งมอบ (กม.), เดือน</summary>
    public static readonly IReadOnlyList<(string Name, int Mileage, int Months)> DefaultTemplates =
    [
        ("น้ำมันเครื่อง", 10_000, 6),
        ("กรองน้ำมันเครื่อง", 10_000, 6),
        ("น้ำมันเกียร์", 40_000, 24),
        ("น้ำมันเบรก", 40_000, 24),
        ("กรองอากาศ", 20_000, 12),
        ("เช็คระยะ", 10_000, 6)
    ];

    private const int NameMaxLength = ServiceSchedule.ItemNameMaxLength;
    private const int MaxSortOrder = 9_999;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<Result<IReadOnlyList<ServiceReminderTemplateDto>>> SearchAsync(
        bool includeInactive, CancellationToken ct = default)
    {
        await EnsureSeededAsync(ct);
        var items = await repository.SearchAsync(user.ShardKey, user.BranchId, includeInactive, ct);
        return Result<IReadOnlyList<ServiceReminderTemplateDto>>.Ok(items.Select(ToDto).ToList());
    }

    public async Task<Result<ServiceReminderTemplateDto>> CreateAsync(
        ServiceReminderTemplateUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<ServiceReminderTemplateDto>.Fail(forbidden);

        var invalid = Validate(request);
        if (invalid is not null) return Result<ServiceReminderTemplateDto>.Fail(invalid);

        // ผู้จัดการเพิ่มรายการแรกเองก่อนที่ใครจะเปิดรายการ → ต้อง seed ก่อน ไม่งั้นชุดตั้งต้นจะไม่มาเลย (AnyAsync เป็นจริงแล้ว)
        await EnsureSeededAsync(ct);

        var name = request.Name.Trim();
        if (await repository.NameExistsAsync(user.ShardKey, user.BranchId, name, null, ct))
            return Result<ServiceReminderTemplateDto>.Fail(
                "SERVICE_TEMPLATE_NAME_DUPLICATE", "มีรายการนัดบริการชื่อนี้อยู่แล้ว", "name");

        var entity = new ServiceReminderTemplate
        {
            LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId,
            Name = name, DefaultMileageInterval = request.DefaultMileageInterval, DefaultMonths = request.DefaultMonths,
            SortOrder = request.SortOrder, CreatedDate = Now, LastUpdated = Now
        };
        await repository.AddAsync(entity, ct);
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(ServiceReminderTemplate),
            "service_reminder_template.created", $"สร้างรายการนัดบริการ {entity.Name}", Now), ct);

        var saveError = await SaveAsync<ServiceReminderTemplateDto>(ct);
        return saveError ?? Result<ServiceReminderTemplateDto>.Ok(ToDto(entity));
    }

    public async Task<Result<ServiceReminderTemplateDto>> UpdateAsync(
        Guid id, ServiceReminderTemplateUpsertRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<ServiceReminderTemplateDto>.Fail(forbidden);

        var entity = await repository.GetAsync(user.ShardKey, user.BranchId, id, ct);
        if (entity is null)
            return Result<ServiceReminderTemplateDto>.Fail(
                "SERVICE_TEMPLATE_NOT_FOUND", "ไม่พบรายการนัดบริการนี้ในสาขาปัจจุบัน");

        var invalid = Validate(request);
        if (invalid is not null) return Result<ServiceReminderTemplateDto>.Fail(invalid);

        var name = request.Name.Trim();
        if (await repository.NameExistsAsync(user.ShardKey, user.BranchId, name, id, ct))
            return Result<ServiceReminderTemplateDto>.Fail(
                "SERVICE_TEMPLATE_NAME_DUPLICATE", "มีรายการนัดบริการชื่อนี้อยู่แล้ว", "name");

        entity.Name = name;
        entity.DefaultMileageInterval = request.DefaultMileageInterval;
        entity.DefaultMonths = request.DefaultMonths;
        entity.SortOrder = request.SortOrder;
        entity.LastUpdated = Now;

        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(ServiceReminderTemplate),
            "service_reminder_template.updated", $"แก้ไขรายการนัดบริการ {entity.Name}", Now), ct);

        var saveError = await SaveAsync<ServiceReminderTemplateDto>(ct);
        return saveError ?? Result<ServiceReminderTemplateDto>.Ok(ToDto(entity));
    }

    public async Task<Result<bool>> SetStatusAsync(Guid id, MasterDataStatusRequest request, CancellationToken ct = default)
    {
        var forbidden = MasterDataSupport.EnsureManager(user);
        if (forbidden is not null) return Result<bool>.Fail(forbidden);

        var entity = await repository.GetAsync(user.ShardKey, user.BranchId, id, ct);
        if (entity is null)
            return Result<bool>.Fail("SERVICE_TEMPLATE_NOT_FOUND", "ไม่พบรายการนัดบริการนี้ในสาขาปัจจุบัน");

        entity.IsActive = request.IsActive;
        entity.LastUpdated = Now;
        await repository.AddEventAsync(MasterDataSupport.Event(user, entity.Id, nameof(ServiceReminderTemplate),
            request.IsActive ? "service_reminder_template.activated" : "service_reminder_template.deactivated",
            $"{(request.IsActive ? "เปิด" : "ปิด")}ใช้งานรายการนัดบริการ {entity.Name}", Now), ct);

        var saveError = await SaveAsync<bool>(ct);
        return saveError ?? Result<bool>.Ok(true);
    }

    /// <summary>
    /// สร้างชุดตั้งต้นเมื่อสาขายังไม่มีรายการใดเลย (นับทั้งที่ปิดใช้งาน — สาขาที่ผู้จัดการปิดทุกรายการไว้ไม่ถูกเติมซ้ำ)
    /// เรียกพร้อมกันสองคำขอ: unique index ชื่อต่อสาขาเป็นด่านสุดท้าย — ฝ่ายที่แพ้ถือว่าอีกฝ่าย seed ให้แล้ว
    /// </summary>
    private async Task EnsureSeededAsync(CancellationToken ct)
    {
        if (await repository.AnyAsync(user.ShardKey, user.BranchId, ct)) return;

        var order = 1;
        foreach (var (name, mileage, months) in DefaultTemplates)
        {
            await repository.AddAsync(new ServiceReminderTemplate
            {
                LegacyShardKey = user.ShardKey, LegacyBranchId = user.BranchId,
                Name = name, DefaultMileageInterval = mileage, DefaultMonths = months,
                SortOrder = order++, CreatedDate = Now, LastUpdated = Now
            }, ct);
        }

        try { await repository.SaveChangesAsync(ct); }
        catch (MasterDataConflictException)
        {
            repository.DiscardPending();
        }
    }

    private static ApiError? Validate(ServiceReminderTemplateUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return new("SERVICE_TEMPLATE_VALIDATION", "กรุณากรอกชื่อรายการนัดบริการ", "name");
        if (request.Name.Trim().Length > NameMaxLength)
            return new("SERVICE_TEMPLATE_VALIDATION", $"ชื่อรายการต้องยาวไม่เกิน {NameMaxLength} ตัวอักษร", "name");
        if (request.DefaultMileageInterval < 1 || request.DefaultMileageInterval > Odometer.MaxKm)
            return new("SERVICE_TEMPLATE_VALIDATION",
                $"ระยะทางที่นัดต้องอยู่ระหว่าง 1 ถึง {Odometer.MaxKm:N0} กม.", "defaultMileageInterval");
        if (request.DefaultMonths is < ServiceSchedule.MinMonths or > ServiceSchedule.MaxMonths)
            return new("SERVICE_TEMPLATE_VALIDATION",
                $"ระยะเวลาที่นัดต้องอยู่ระหว่าง {ServiceSchedule.MinMonths}–{ServiceSchedule.MaxMonths} เดือน", "defaultMonths");
        if (request.SortOrder is < 0 or > MaxSortOrder)
            return new("SERVICE_TEMPLATE_VALIDATION", $"ลำดับต้องอยู่ระหว่าง 0 ถึง {MaxSortOrder:N0}", "sortOrder");
        return null;
    }

    private async Task<Result<T>?> SaveAsync<T>(CancellationToken ct)
    {
        try { await repository.SaveChangesAsync(ct); return null; }
        catch (MasterDataConflictException)
        {
            return Result<T>.Fail("SERVICE_TEMPLATE_NAME_DUPLICATE", "มีรายการนัดบริการชื่อนี้อยู่แล้ว", "name");
        }
    }

    private static ServiceReminderTemplateDto ToDto(ServiceReminderTemplate t) =>
        new(t.Id, t.Name, t.DefaultMileageInterval, t.DefaultMonths, t.SortOrder, t.IsActive);
}
