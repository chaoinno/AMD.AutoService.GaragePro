namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record StaffSearchQuery(
    string? Keyword = null,
    int? DepartmentId = null,
    int? SectorId = null,
    int? PositionId = null,
    int? ProvinceId = null,
    int? AmphureId = null,
    int? DistrictId = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 25);

public sealed record StaffSummaryDto(
    long Id,
    string Code,
    string FullName,
    string? PhoneNumber1,
    string? Email,
    string? DepartmentName,
    string? SectorName,
    string? PositionName,
    string? PictureUrl,
    bool IsActive,
    bool IsAdministrator,
    DateTime? LastUpdated);

public sealed record StaffSectorPositionDto(
    long Id,
    int DepartmentId,
    string DepartmentName,
    int SectorId,
    string SectorName,
    int PositionId,
    string PositionName,
    bool IsMain);

public sealed record StaffAccountDto(long Id, string UserName, bool IsAdministrator, bool IsActive);

public sealed record StaffDetailDto(
    long Id,
    int BranchId,
    string BranchName,
    string Code,
    string FirstName,
    string LastName,
    int? GenderId,
    string? GenderName,
    string? IdCard,
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
    decimal? Salary,
    int? StaffSkillLevelId,
    string? StaffSkillLevelName,
    int? ExperienceYear,
    int? ExperienceMonth,
    DateTime? StartJobDate,
    DateTime? EndJobDate,
    string? Note,
    string? PictureUrl,
    bool IsActive,
    StaffAccountDto Account,
    IReadOnlyList<StaffSectorPositionDto> SectorPositions,
    DateTime? CreatedDate,
    DateTime? LastUpdated);

public sealed record StaffUpsertRequest(
    int BranchId,
    string FirstName,
    string LastName,
    int GenderId,
    string? IdCard,
    string? Address1,
    string? Address2,
    int? ProvinceId,
    int? AmphureId,
    int? DistrictId,
    string? ZipCode,
    string PhoneNumber1,
    string? PhoneNumber2,
    string? Email,
    string? LineId,
    decimal Salary,
    int? StaffSkillLevelId,
    int ExperienceYear,
    int ExperienceMonth,
    DateTime? StartJobDate,
    DateTime? EndJobDate,
    string? Note,
    int MainSectorId,
    int PositionId,
    IReadOnlyList<int>? AdditionalSectorIds,
    string? UserName,
    string? Password);

public sealed record StaffStatusRequest(bool IsActive, DateTime? EndJobDate);
public sealed record StaffCodePreviewDto(string Code, string UserName, string Password);
public sealed record StaffImageUpload(string FileName, string ContentType, byte[] Data);
public sealed record StaffImageFile(string FullPath, string ContentType, string FileName);
public sealed record StaffImageValidation(bool IsValid, string? Code, string? MessageTh);

public sealed record StaffReferenceDataDto(
    IReadOnlyList<LookupItemDto> Branches,
    IReadOnlyList<LookupItemDto> Genders,
    IReadOnlyList<LookupItemDto> Departments,
    IReadOnlyList<LookupItemDto> Positions,
    IReadOnlyList<LookupItemDto> SkillLevels);
