namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record CustomerSearchQuery(
    string? Keyword = null,
    int? BrandId = null,
    int? ModelId = null,
    int? ProvinceId = null,
    int? AmphureId = null,
    int? DistrictId = null,
    bool IncludeDeleted = false,
    int Page = 1,
    int PageSize = 25,
    string? SortBy = null);

public sealed record CustomerSummaryDto(
    long Id,
    string Code,
    string FirstName,
    string LastName,
    string FullName,
    string? IdCard,
    string? PhoneNumber1,
    string? PhoneNumber2,
    string? Email,
    string? ProvinceName,
    bool IsBlacklist,
    string? BlacklistRemark,
    bool IsDeleted,
    int VehicleCount,
    DateTime? LastUpdated);

public sealed record CustomerVehicleSummaryDto(
    long Id,
    string Registration,
    string? ProvinceName,
    string? BrandName,
    string? ModelName,
    string? Nickname,
    string? Year,
    string? ImageUrl,
    bool IsDeleted);

public sealed record CustomerDetailDto(
    long Id,
    string Code,
    string FirstName,
    string LastName,
    string? IdCard,
    string? DriverLicense,
    int? GenderId,
    DateTime? DateOfBirth,
    string? Address1,
    string? Address2,
    int? ProvinceId,
    string? ProvinceName,
    int? AmphureId,
    string? AmphureName,
    int? DistrictId,
    string? DistrictName,
    string? ZipCode,
    string? PhoneNumber1,
    string? PhoneNumber2,
    string? Email,
    string? LineId,
    bool IsBlacklist,
    string? BlacklistRemark,
    bool IsDeleted,
    DateTime? CreatedDate,
    DateTime? LastUpdated,
    IReadOnlyList<CustomerVehicleSummaryDto> Vehicles);

public sealed record CustomerUpsertRequest(
    string FirstName,
    string LastName,
    string PhoneNumber1,
    string? PhoneNumber2 = null,
    string? IdCard = null,
    string? DriverLicense = null,
    int? GenderId = null,
    DateTime? DateOfBirth = null,
    string? Address1 = null,
    string? Address2 = null,
    int? ProvinceId = null,
    int? AmphureId = null,
    int? DistrictId = null,
    string? ZipCode = null,
    string? Email = null,
    string? LineId = null,
    bool IsBlacklist = false,
    string? BlacklistRemark = null);

public sealed record CustomerDuplicateDto(
    long Id,
    string Code,
    string FullName,
    string PhoneNumber1,
    string? Email,
    bool IsDeleted);

public sealed record VehicleSearchQuery(
    string? Keyword = null,
    IReadOnlyList<string>? SearchFields = null,
    int? BrandId = null,
    int? ModelId = null,
    int? NicknameId = null,
    int? CarTypeId = null,
    int? YearId = null,
    int? InsuranceId = null,
    bool IncludeDeleted = false,
    int Page = 1,
    int PageSize = 25,
    string? SortBy = null);

public sealed record VehicleOwnerDto(
    long Id,
    string Code,
    string FullName,
    string? PhoneNumber1,
    string? IdCard);

public sealed record VehicleSummaryDto(
    long Id,
    string Registration,
    string? ProvinceName,
    string? BrandName,
    string? ModelName,
    string? Nickname,
    string? CarTypeName,
    string? Year,
    string? PrimaryColorName,
    string? OwnerName,
    string? OwnerPhone,
    string? ImageUrl,
    bool IsDeleted,
    DateTime? LastUpdated);

public sealed record VehicleDetailDto(
    long Id,
    string Registration,
    int? ProvinceId,
    string? ProvinceName,
    int? BrandId,
    string? BrandName,
    int? ModelId,
    string? ModelName,
    int? NicknameId,
    string? Nickname,
    int? CarTypeId,
    string? CarTypeName,
    int? YearId,
    string? Year,
    int? PrimaryColorId,
    string? PrimaryColorName,
    int? ColorMixId,
    string? ColorMixName,
    int? GearId,
    string? GearName,
    int? MachineId,
    string? MachineName,
    int? DriveSystemId,
    string? DriveSystemName,
    string? Vin,
    string? EngineNumber,
    int? InsuranceId,
    string? InsuranceName,
    DateTime? InsuranceExpiredDate,
    string? ImageUrl,
    bool IsDeleted,
    DateTime? CreatedDate,
    DateTime? LastUpdated,
    IReadOnlyList<VehicleOwnerDto> Owners);

public sealed record VehicleUpsertRequest(
    long CustomerId,
    string Registration,
    int ProvinceId,
    int BrandId,
    int ModelId,
    int NicknameId,
    int YearId,
    int? PrimaryColorId = null,
    int? ColorMixId = null,
    int? GearId = null,
    int? MachineId = null,
    int? DriveSystemId = null,
    string? Vin = null,
    string? EngineNumber = null,
    int? InsuranceId = null,
    DateTime? InsuranceExpiredDate = null);

public sealed record VehicleImageUpload(
    string FileName,
    string ContentType,
    byte[] Data);

public sealed class LookupItemDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Secondary { get; set; }
}

public sealed record VehicleReferenceDataDto(
    IReadOnlyList<LookupItemDto> Brands,
    IReadOnlyList<LookupItemDto> Years,
    IReadOnlyList<LookupItemDto> CarTypes,
    IReadOnlyList<LookupItemDto> Insurances,
    IReadOnlyList<LookupItemDto> Gears,
    IReadOnlyList<LookupItemDto> Machines,
    IReadOnlyList<LookupItemDto> DriveSystems,
    IReadOnlyList<LookupItemDto> PrimaryColors,
    IReadOnlyList<LookupItemDto> ColorMixes);

public sealed record VehicleImageFile(string FullPath, string ContentType, string FileName);
