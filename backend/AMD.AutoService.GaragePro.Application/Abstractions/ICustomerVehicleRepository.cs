using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public sealed record LegacyRequestScope(string ShardKey, int BranchId, long UserId, string? UserName = null);

public interface ICustomerVehicleRepository
{
    Task<PagedResult<CustomerSummaryDto>> SearchCustomersAsync(
        LegacyRequestScope scope, CustomerSearchQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<CustomerSummaryDto>> ExportCustomersAsync(
        LegacyRequestScope scope, CustomerSearchQuery query, int take, CancellationToken ct = default);
    Task<CustomerDetailDto?> GetCustomerAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default);
    Task<CustomerDuplicateDto?> FindDuplicateCustomerAsync(
        LegacyRequestScope scope, CustomerUpsertRequest request, long? excludingId,
        CancellationToken ct = default);
    Task<long> CreateCustomerAsync(
        LegacyRequestScope scope, CustomerUpsertRequest request, CancellationToken ct = default);
    Task<bool> UpdateCustomerAsync(
        LegacyRequestScope scope, long id, CustomerUpsertRequest request, CancellationToken ct = default);
    Task<bool> SoftDeleteCustomerAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default);

    Task<PagedResult<VehicleSummaryDto>> SearchVehiclesAsync(
        LegacyRequestScope scope, VehicleSearchQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<VehicleSummaryDto>> ExportVehiclesAsync(
        LegacyRequestScope scope, VehicleSearchQuery query, int take, CancellationToken ct = default);
    Task<VehicleDetailDto?> GetVehicleAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default);
    Task<long> CreateVehicleAsync(
        LegacyRequestScope scope, VehicleUpsertRequest request, VehicleImageUpload? image,
        CancellationToken ct = default);
    Task<bool> UpdateVehicleAsync(
        LegacyRequestScope scope, long id, VehicleUpsertRequest request, VehicleImageUpload? image,
        CancellationToken ct = default);
    Task<bool> SoftDeleteVehicleAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default);
    Task<string?> GetVehicleImagePathAsync(
        LegacyRequestScope scope, long id, CancellationToken ct = default);
    /// <summary>เปลี่ยนเฉพาะรูปรถ ไม่แตะข้อมูลอื่น (ทะเบียน/ยี่ห้อ/เจ้าของ ฯลฯ) — ใช้จากหน้าที่ไม่มีฟอร์มรถเต็มให้กรอกซ้ำ</summary>
    Task<bool> UpdateVehicleImageAsync(
        LegacyRequestScope scope, long id, VehicleImageUpload image, CancellationToken ct = default);

    Task<IReadOnlyList<LookupItemDto>> GetProvincesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetAmphuresAsync(int provinceId, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetDistrictsAsync(int amphureId, CancellationToken ct = default);
    Task<string?> GetZipCodeAsync(int districtId, CancellationToken ct = default);
    Task<VehicleReferenceDataDto> GetVehicleReferenceDataAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetModelsAsync(int brandId, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItemDto>> GetNicknamesAsync(int modelId, CancellationToken ct = default);
}

public sealed record VehicleImageValidation(bool IsValid, string? Code, string? MessageTh);

public interface IVehicleImageStorage
{
    VehicleImageValidation Validate(VehicleImageUpload upload);
    Task<string> SaveAsync(long vehicleId, VehicleImageUpload upload, CancellationToken ct = default);
    bool TryResolve(string relativePath, out string fullPath);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
    string GetContentType(string relativePath);
}
