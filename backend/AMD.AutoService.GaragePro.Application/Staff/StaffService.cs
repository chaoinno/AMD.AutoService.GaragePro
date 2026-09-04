using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Staff;

public interface IStaffService
{
    Task<Result<PagedResult<StaffSummaryDto>>> SearchAsync(StaffSearchQuery query, CancellationToken ct = default);
    Task<Result<StaffDetailDto>> GetAsync(long id, CancellationToken ct = default);
    Task<Result<StaffDetailDto>> CreateAsync(StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default);
    Task<Result<StaffDetailDto>> UpdateAsync(long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default);
    Task<Result<bool>> SetStatusAsync(long id, StaffStatusRequest request, CancellationToken ct = default);
    Task<Result<StaffCodePreviewDto>> PreviewCodeAsync(int? branchId, CancellationToken ct = default);
    Task<Result<StaffImageFile>> OpenImageAsync(long id, CancellationToken ct = default);
    Task<Result<StaffReferenceDataDto>> GetReferenceDataAsync(CancellationToken ct = default);
    Task<Result<IReadOnlyList<LookupItemDto>>> GetSectorsAsync(int? departmentId, CancellationToken ct = default);
}

public sealed class StaffService(IStaffRepository repository, IStaffImageStorage imageStorage,
    ICurrentUser currentUser) : IStaffService
{
    private LegacyRequestScope Scope => new(currentUser.ShardKey, currentUser.BranchId, currentUser.UserId, currentUser.UserName);

    public async Task<Result<PagedResult<StaffSummaryDto>>> SearchAsync(StaffSearchQuery query, CancellationToken ct = default)
    {
        var normalized = query with { Page = Math.Max(1, query.Page), PageSize = Math.Clamp(query.PageSize, 1, 100) };
        return Result<PagedResult<StaffSummaryDto>>.Ok(await repository.SearchAsync(Scope, currentUser.IsAdministrator, normalized, ct));
    }

    public async Task<Result<StaffDetailDto>> GetAsync(long id, CancellationToken ct = default)
    {
        var staff = await repository.GetAsync(Scope, currentUser.IsAdministrator, id, ct);
        return staff is null || staff.BranchId != currentUser.BranchId
            ? Result<StaffDetailDto>.Fail("STAFF_NOT_FOUND", "ไม่พบพนักงานหรือคุณไม่มีสิทธิ์ดูข้อมูลนี้")
            : Result<StaffDetailDto>.Ok(staff);
    }

    public async Task<Result<StaffDetailDto>> CreateAsync(StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default)
    {
        if (request.BranchId != 0 && request.BranchId != currentUser.BranchId)
            return Result<StaffDetailDto>.Fail("STAFF_BRANCH_FORBIDDEN", "เพิ่มพนักงานได้เฉพาะสาขาที่เข้าสู่ระบบ", "branchId");
        var normalized = Normalize(request with { BranchId = currentUser.BranchId }, isCreate: true);
        var error = StaffValidator.Validate(normalized, isCreate: true);
        if (error is not null) return Result<StaffDetailDto>.Fail(error);
        var imageError = ValidateImage(image);
        if (imageError is not null) return Result<StaffDetailDto>.Fail(imageError);
        var result = await repository.CreateAsync(Scope, currentUser.IsAdministrator, normalized, image, ct);
        return result is null
            ? Result<StaffDetailDto>.Fail("STAFF_REFERENCE_INVALID", "ข้อมูลสาขา แผนก ตำแหน่ง หรือระดับฝีมือไม่ถูกต้อง")
            : Result<StaffDetailDto>.Ok(result);
    }

    public async Task<Result<StaffDetailDto>> UpdateAsync(long id, StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default)
    {
        if (request.BranchId != 0 && request.BranchId != currentUser.BranchId)
            return Result<StaffDetailDto>.Fail("STAFF_BRANCH_FORBIDDEN", "แก้ไขพนักงานได้เฉพาะสาขาที่เข้าสู่ระบบ ไม่สามารถย้ายสาขาผ่าน API นี้", "branchId");
        var normalized = Normalize(request with { BranchId = currentUser.BranchId }, isCreate: false);
        var error = StaffValidator.Validate(normalized, isCreate: false);
        if (error is not null) return Result<StaffDetailDto>.Fail(error);
        var existing = await repository.GetAsync(Scope, currentUser.IsAdministrator, id, ct);
        if (existing is null || existing.BranchId != currentUser.BranchId) return Result<StaffDetailDto>.Fail("STAFF_NOT_FOUND", "ไม่พบพนักงานในสาขาที่เข้าสู่ระบบ");
        if (!currentUser.IsAdministrator &&
            (normalized.BranchId != existing.BranchId || normalized.MainSectorId != existing.SectorPositions.FirstOrDefault(x => x.IsMain)?.SectorId ||
             normalized.PositionId != existing.SectorPositions.FirstOrDefault(x => x.IsMain)?.PositionId ||
             !SetEquals(normalized.AdditionalSectorIds, existing.SectorPositions.Where(x => !x.IsMain).Select(x => x.SectorId))))
            return Result<StaffDetailDto>.Fail("STAFF_ORGANIZATION_FORBIDDEN", "เฉพาะผู้ดูแลระบบเท่านั้นที่แก้ไขแผนกหรือตำแหน่งได้");
        var imageError = ValidateImage(image);
        if (imageError is not null) return Result<StaffDetailDto>.Fail(imageError);
        var result = await repository.UpdateAsync(Scope, currentUser.IsAdministrator, id, normalized, image, ct);
        return result is null
            ? Result<StaffDetailDto>.Fail("STAFF_UPDATE_CONFLICT", "ชื่อผู้ใช้ซ้ำหรือข้อมูลอ้างอิงไม่ถูกต้อง")
            : Result<StaffDetailDto>.Ok(result);
    }

    public async Task<Result<bool>> SetStatusAsync(long id, StaffStatusRequest request, CancellationToken ct = default)
    {
        var error = StaffValidator.ValidateStatus(request);
        if (error is not null) return Result<bool>.Fail(error);
        return await repository.SetStatusAsync(Scope, currentUser.IsAdministrator, id, request, ct)
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail("STAFF_NOT_FOUND", "ไม่พบพนักงานหรือคุณไม่มีสิทธิ์แก้ไขข้อมูลนี้");
    }

    public async Task<Result<StaffCodePreviewDto>> PreviewCodeAsync(int? branchId, CancellationToken ct = default)
    {
        var target = branchId.GetValueOrDefault(currentUser.BranchId);
        if (target != currentUser.BranchId)
            return Result<StaffCodePreviewDto>.Fail("STAFF_BRANCH_FORBIDDEN", "สร้างรหัสพนักงานได้เฉพาะสาขาที่เข้าสู่ระบบ");
        return Result<StaffCodePreviewDto>.Ok(await repository.PreviewCodeAsync(Scope, target, ct));
    }

    public async Task<Result<StaffImageFile>> OpenImageAsync(long id, CancellationToken ct = default)
    {
        var path = await repository.GetImagePathAsync(Scope, currentUser.IsAdministrator, id, ct);
        if (string.IsNullOrWhiteSpace(path) || !imageStorage.TryResolve(path, out var fullPath))
            return Result<StaffImageFile>.Fail("STAFF_IMAGE_NOT_FOUND", "ไม่พบรูปพนักงาน");
        return Result<StaffImageFile>.Ok(new(fullPath, imageStorage.GetContentType(path), Path.GetFileName(path)));
    }

    public async Task<Result<StaffReferenceDataDto>> GetReferenceDataAsync(CancellationToken ct = default) =>
        Result<StaffReferenceDataDto>.Ok(await repository.GetReferenceDataAsync(Scope, currentUser.IsAdministrator, ct));

    public async Task<Result<IReadOnlyList<LookupItemDto>>> GetSectorsAsync(int? departmentId, CancellationToken ct = default) =>
        Result<IReadOnlyList<LookupItemDto>>.Ok(await repository.GetSectorsAsync(Scope, departmentId, ct));

    private ApiError? ValidateImage(StaffImageUpload? image)
    {
        if (image is null) return null;
        var validation = imageStorage.Validate(image);
        return validation.IsValid ? null : new(validation.Code!, validation.MessageTh!, "image");
    }

    private static StaffUpsertRequest Normalize(StaffUpsertRequest r, bool isCreate) => r with
    {
        FirstName = r.FirstName.Trim(), LastName = r.LastName.Trim(), PhoneNumber1 = r.PhoneNumber1.Trim(),
        PhoneNumber2 = Clean(r.PhoneNumber2), IdCard = Clean(r.IdCard), Address1 = Clean(r.Address1),
        Address2 = Clean(r.Address2), ZipCode = Clean(r.ZipCode), Email = Clean(r.Email), LineId = Clean(r.LineId),
        Note = Clean(r.Note), UserName = isCreate ? null : Clean(r.UserName), Password = isCreate ? null : Clean(r.Password),
        AdditionalSectorIds = (r.AdditionalSectorIds ?? []).Where(x => x > 0 && x != r.MainSectorId).Distinct().ToArray()
    };

    private static bool SetEquals(IEnumerable<int>? left, IEnumerable<int> right) =>
        new HashSet<int>(left ?? []).SetEquals(right);
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
