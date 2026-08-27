using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Customers;

public interface ICustomerVehicleService
{
    Task<Result<PagedResult<CustomerSummaryDto>>> SearchCustomersAsync(CustomerSearchQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<CustomerSummaryDto>>> ExportCustomersAsync(CustomerSearchQuery query, CancellationToken ct = default);
    Task<Result<CustomerDetailDto>> GetCustomerAsync(long id, CancellationToken ct = default);
    Task<Result<CustomerDetailDto>> CreateCustomerAsync(CustomerUpsertRequest request, CancellationToken ct = default);
    Task<Result<CustomerDetailDto>> UpdateCustomerAsync(long id, CustomerUpsertRequest request, CancellationToken ct = default);
    Task<Result<bool>> DeleteCustomerAsync(long id, CancellationToken ct = default);

    Task<Result<PagedResult<VehicleSummaryDto>>> SearchVehiclesAsync(VehicleSearchQuery query, CancellationToken ct = default);
    Task<Result<IReadOnlyList<VehicleSummaryDto>>> ExportVehiclesAsync(VehicleSearchQuery query, CancellationToken ct = default);
    Task<Result<VehicleDetailDto>> GetVehicleAsync(long id, CancellationToken ct = default);
    Task<Result<VehicleDetailDto>> CreateVehicleAsync(VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default);
    Task<Result<VehicleDetailDto>> UpdateVehicleAsync(long id, VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default);
    Task<Result<bool>> DeleteVehicleAsync(long id, CancellationToken ct = default);
    Task<Result<VehicleImageFile>> OpenVehicleImageAsync(long id, CancellationToken ct = default);
}

public sealed class CustomerVehicleService(
    ICustomerVehicleRepository repository,
    IVehicleImageStorage imageStorage,
    ICurrentUser currentUser) : ICustomerVehicleService
{
    private LegacyRequestScope Scope => new(currentUser.ShardKey, currentUser.BranchId, currentUser.UserId);

    public async Task<Result<PagedResult<CustomerSummaryDto>>> SearchCustomersAsync(
        CustomerSearchQuery query, CancellationToken ct = default)
    {
        var normalized = query with { Page = Math.Max(1, query.Page), PageSize = Math.Clamp(query.PageSize, 1, 100) };
        return Result<PagedResult<CustomerSummaryDto>>.Ok(await repository.SearchCustomersAsync(Scope, normalized, ct));
    }

    public async Task<Result<IReadOnlyList<CustomerSummaryDto>>> ExportCustomersAsync(
        CustomerSearchQuery query, CancellationToken ct = default) =>
        Result<IReadOnlyList<CustomerSummaryDto>>.Ok(await repository.ExportCustomersAsync(Scope, query, 10_000, ct));

    public async Task<Result<CustomerDetailDto>> GetCustomerAsync(long id, CancellationToken ct = default)
    {
        var customer = await repository.GetCustomerAsync(Scope, id, ct);
        return customer is null
            ? Result<CustomerDetailDto>.Fail("CUSTOMER_NOT_FOUND", "ไม่พบลูกค้าหรือคุณไม่มีสิทธิ์ดูข้อมูลนี้")
            : Result<CustomerDetailDto>.Ok(customer);
    }

    public async Task<Result<CustomerDetailDto>> CreateCustomerAsync(CustomerUpsertRequest request, CancellationToken ct = default)
    {
        var normalized = Normalize(request);
        var error = CustomerVehicleValidator.ValidateCustomer(normalized);
        if (error is not null) return Result<CustomerDetailDto>.Fail(error);

        var duplicate = await repository.FindDuplicateCustomerAsync(Scope, normalized, null, ct);
        if (duplicate is not null)
            return Result<CustomerDetailDto>.Fail("CUSTOMER_DUPLICATE",
                "พบลูกค้าที่มีชื่อ นามสกุล และเบอร์โทรนี้อยู่แล้ว", details: duplicate);

        var id = await repository.CreateCustomerAsync(Scope, normalized, ct);
        return Result<CustomerDetailDto>.Ok((await repository.GetCustomerAsync(Scope, id, ct))!);
    }

    public async Task<Result<CustomerDetailDto>> UpdateCustomerAsync(long id, CustomerUpsertRequest request, CancellationToken ct = default)
    {
        var normalized = Normalize(request);
        var error = CustomerVehicleValidator.ValidateCustomer(normalized);
        if (error is not null) return Result<CustomerDetailDto>.Fail(error);

        var duplicate = await repository.FindDuplicateCustomerAsync(Scope, normalized, id, ct);
        if (duplicate is not null)
            return Result<CustomerDetailDto>.Fail("CUSTOMER_DUPLICATE",
                "ข้อมูลนี้ซ้ำกับลูกค้ารายอื่น", details: duplicate);

        if (!await repository.UpdateCustomerAsync(Scope, id, normalized, ct))
            return Result<CustomerDetailDto>.Fail("CUSTOMER_NOT_FOUND", "ไม่พบลูกค้าหรือคุณไม่มีสิทธิ์แก้ไขข้อมูลนี้");

        return Result<CustomerDetailDto>.Ok((await repository.GetCustomerAsync(Scope, id, ct))!);
    }

    public async Task<Result<bool>> DeleteCustomerAsync(long id, CancellationToken ct = default) =>
        await repository.SoftDeleteCustomerAsync(Scope, id, ct)
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail("CUSTOMER_NOT_FOUND", "ไม่พบลูกค้าหรือคุณไม่มีสิทธิ์ลบข้อมูลนี้");

    public async Task<Result<PagedResult<VehicleSummaryDto>>> SearchVehiclesAsync(
        VehicleSearchQuery query, CancellationToken ct = default)
    {
        var normalized = query with { Page = Math.Max(1, query.Page), PageSize = Math.Clamp(query.PageSize, 1, 100) };
        return Result<PagedResult<VehicleSummaryDto>>.Ok(await repository.SearchVehiclesAsync(Scope, normalized, ct));
    }

    public async Task<Result<IReadOnlyList<VehicleSummaryDto>>> ExportVehiclesAsync(
        VehicleSearchQuery query, CancellationToken ct = default) =>
        Result<IReadOnlyList<VehicleSummaryDto>>.Ok(await repository.ExportVehiclesAsync(Scope, query, 10_000, ct));

    public async Task<Result<VehicleDetailDto>> GetVehicleAsync(long id, CancellationToken ct = default)
    {
        var vehicle = await repository.GetVehicleAsync(Scope, id, ct);
        return vehicle is null
            ? Result<VehicleDetailDto>.Fail("VEHICLE_NOT_FOUND", "ไม่พบรถหรือคุณไม่มีสิทธิ์ดูข้อมูลนี้")
            : Result<VehicleDetailDto>.Ok(vehicle);
    }

    public async Task<Result<VehicleDetailDto>> CreateVehicleAsync(
        VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default)
    {
        var normalized = Normalize(request);
        var error = CustomerVehicleValidator.ValidateVehicle(normalized);
        if (error is not null) return Result<VehicleDetailDto>.Fail(error);
        if (image is not null)
        {
            var validation = imageStorage.Validate(image);
            if (!validation.IsValid) return Result<VehicleDetailDto>.Fail(validation.Code!, validation.MessageTh!, "image");
        }
        if (await repository.GetCustomerAsync(Scope, normalized.CustomerId, ct) is null)
            return Result<VehicleDetailDto>.Fail("VEHICLE_CUSTOMER_NOT_FOUND", "ไม่พบลูกค้าที่เลือกหรืออยู่นอกขอบเขตสาขา", "customerId");

        var id = await repository.CreateVehicleAsync(Scope, normalized, image, ct);
        return Result<VehicleDetailDto>.Ok((await repository.GetVehicleAsync(Scope, id, ct))!);
    }

    public async Task<Result<VehicleDetailDto>> UpdateVehicleAsync(
        long id, VehicleUpsertRequest request, VehicleImageUpload? image, CancellationToken ct = default)
    {
        var normalized = Normalize(request);
        var error = CustomerVehicleValidator.ValidateVehicle(normalized);
        if (error is not null) return Result<VehicleDetailDto>.Fail(error);
        if (image is not null)
        {
            var validation = imageStorage.Validate(image);
            if (!validation.IsValid) return Result<VehicleDetailDto>.Fail(validation.Code!, validation.MessageTh!, "image");
        }
        if (await repository.GetCustomerAsync(Scope, normalized.CustomerId, ct) is null)
            return Result<VehicleDetailDto>.Fail("VEHICLE_CUSTOMER_NOT_FOUND", "ไม่พบลูกค้าที่เลือกหรืออยู่นอกขอบเขตสาขา", "customerId");
        if (!await repository.UpdateVehicleAsync(Scope, id, normalized, image, ct))
            return Result<VehicleDetailDto>.Fail("VEHICLE_NOT_FOUND", "ไม่พบรถหรือคุณไม่มีสิทธิ์แก้ไขข้อมูลนี้");

        return Result<VehicleDetailDto>.Ok((await repository.GetVehicleAsync(Scope, id, ct))!);
    }

    public async Task<Result<bool>> DeleteVehicleAsync(long id, CancellationToken ct = default) =>
        await repository.SoftDeleteVehicleAsync(Scope, id, ct)
            ? Result<bool>.Ok(true)
            : Result<bool>.Fail("VEHICLE_NOT_FOUND", "ไม่พบรถหรือคุณไม่มีสิทธิ์ลบข้อมูลนี้");

    public async Task<Result<VehicleImageFile>> OpenVehicleImageAsync(long id, CancellationToken ct = default)
    {
        var path = await repository.GetVehicleImagePathAsync(Scope, id, ct);
        if (string.IsNullOrWhiteSpace(path) || !imageStorage.TryResolve(path, out var fullPath))
            return Result<VehicleImageFile>.Fail("VEHICLE_IMAGE_NOT_FOUND", "ไม่พบรูปของรถคันนี้");
        return Result<VehicleImageFile>.Ok(new(fullPath, imageStorage.GetContentType(path), Path.GetFileName(path)));
    }

    private static CustomerUpsertRequest Normalize(CustomerUpsertRequest r) => r with
    {
        FirstName = r.FirstName.Trim(), LastName = r.LastName.Trim(), PhoneNumber1 = r.PhoneNumber1.Trim(),
        PhoneNumber2 = Clean(r.PhoneNumber2), IdCard = Clean(r.IdCard), DriverLicense = Clean(r.DriverLicense),
        Address1 = Clean(r.Address1), Address2 = Clean(r.Address2), ZipCode = Clean(r.ZipCode),
        Email = Clean(r.Email), LineId = Clean(r.LineId), BlacklistRemark = Clean(r.BlacklistRemark)
    };

    private static VehicleUpsertRequest Normalize(VehicleUpsertRequest r) => r with
    {
        Registration = r.Registration.Trim(), Vin = Clean(r.Vin)?.ToUpperInvariant(),
        EngineNumber = Clean(r.EngineNumber)
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
