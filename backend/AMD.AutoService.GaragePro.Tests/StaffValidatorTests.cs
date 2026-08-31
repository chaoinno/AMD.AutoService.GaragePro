using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.Staff;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class StaffValidatorTests
{
    [Fact]
    public void Valid_staff_passes() => Assert.Null(StaffValidator.Validate(Valid(), isCreate: true));

    [Theory]
    [InlineData("123")]
    [InlineData("123456789012A")]
    public void Id_card_must_be_thirteen_digits(string idCard) =>
        Assert.Equal("STAFF_ID_CARD_INVALID", StaffValidator.Validate(Valid() with { IdCard = idCard }, true)?.Code);

    [Fact]
    public void Salary_cannot_be_negative() =>
        Assert.Equal("STAFF_SALARY_INVALID", StaffValidator.Validate(Valid() with { Salary = -1 }, true)?.Code);

    [Fact]
    public void Experience_month_is_zero_to_eleven() =>
        Assert.Equal("STAFF_EXPERIENCE_MONTH_INVALID", StaffValidator.Validate(Valid() with { ExperienceMonth = 12 }, true)?.Code);

    [Fact]
    public void Inactive_status_requires_end_date() =>
        Assert.Equal("STAFF_END_DATE_REQUIRED", StaffValidator.ValidateStatus(new(false, null))?.Code);

    [Fact]
    public void Update_requires_username() =>
        Assert.Equal("STAFF_USERNAME_REQUIRED", StaffValidator.Validate(Valid() with { UserName = null }, false)?.Code);

    private static StaffUpsertRequest Valid() => new(
        BranchId: 1, FirstName: "สมชาย", LastName: "ใจดี", GenderId: 1, IdCard: "1234567890123",
        Address1: null, Address2: null, ProvinceId: null, AmphureId: null, DistrictId: null, ZipCode: null,
        PhoneNumber1: "0812345678", PhoneNumber2: null, Email: "staff@example.com", LineId: null,
        Salary: 15000, StaffSkillLevelId: null, ExperienceYear: 1, ExperienceMonth: 6,
        StartJobDate: DateTime.Today, EndJobDate: null, Note: null, MainSectorId: 1, PositionId: 1,
        AdditionalSectorIds: [], UserName: "staff", Password: null);
}
