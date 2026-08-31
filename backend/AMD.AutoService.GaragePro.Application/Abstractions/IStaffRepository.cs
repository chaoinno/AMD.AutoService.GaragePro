using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IStaffRepository
{
    Task<PagedResult<StaffSummaryDto>> SearchAsync(LegacyRequestScope scope, bool isAdministrator,
        StaffSearchQuery query, CancellationToken ct = default);
    Task<StaffDetailDto?> GetAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        CancellationToken ct = default);
    Task<StaffDetailDto?> CreateAsync(LegacyRequestScope scope, bool isAdministrator,
        StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default);
    Task<StaffDetailDto?> UpdateAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        StaffUpsertRequest request, StaffImageUpload? image, CancellationToken ct = default);
    Task<bool> SetStatusAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        StaffStatusRequest request, CancellationToken ct = default);
    Task<StaffCodePreviewDto> PreviewCodeAsync(LegacyRequestScope scope, int branchId,
        CancellationToken ct = default);
    Task<string?> GetImagePathAsync(LegacyRequestScope scope, bool isAdministrator, long id,
        CancellationToken ct = default);
    Task<StaffReferenceDataDto> GetReferenceDataAsync(LegacyRequestScope scope, bool isAdministrator,
        CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetSectorsAsync(LegacyRequestScope scope, int? departmentId,
        CancellationToken ct = default);
}

public interface IStaffImageStorage
{
    StaffImageValidation Validate(StaffImageUpload upload);
    Task<string> SaveAsync(long staffId, StaffImageUpload upload, CancellationToken ct = default);
    bool TryResolve(string relativePath, out string fullPath);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
    string GetContentType(string relativePath);
}
