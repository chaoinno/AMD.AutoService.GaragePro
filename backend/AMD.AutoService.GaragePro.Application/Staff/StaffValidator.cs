using System.Net.Mail;
using System.Text.RegularExpressions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Staff;

public static partial class StaffValidator
{
    [GeneratedRegex(@"^[0-9+()\-\s]{8,20}$")]
    private static partial Regex PhoneRegex();

    public static ApiError? Validate(StaffUpsertRequest request, bool isCreate)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
            return new("STAFF_FIRST_NAME_REQUIRED", "กรุณากรอกชื่อพนักงาน", "firstName");
        if (string.IsNullOrWhiteSpace(request.LastName))
            return new("STAFF_LAST_NAME_REQUIRED", "กรุณากรอกนามสกุลพนักงาน", "lastName");
        if (request.FirstName.Trim().Length > 100 || request.LastName.Trim().Length > 100)
            return new("STAFF_NAME_TOO_LONG", "ชื่อและนามสกุลต้องยาวไม่เกิน 100 ตัวอักษร", "firstName");
        if (request.GenderId <= 0)
            return new("STAFF_GENDER_REQUIRED", "กรุณาเลือกคำนำหน้า/เพศ", "genderId");
        if (string.IsNullOrWhiteSpace(request.PhoneNumber1) || !PhoneRegex().IsMatch(request.PhoneNumber1))
            return new("STAFF_PHONE_INVALID", "กรุณากรอกเบอร์โทรศัพท์หลักให้ถูกต้อง", "phoneNumber1");
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber2) && !PhoneRegex().IsMatch(request.PhoneNumber2))
            return new("STAFF_PHONE_INVALID", "รูปแบบเบอร์โทรศัพท์สำรองไม่ถูกต้อง", "phoneNumber2");
        if (!string.IsNullOrWhiteSpace(request.IdCard) &&
            (request.IdCard.Length != 13 || request.IdCard.Any(c => !char.IsDigit(c))))
            return new("STAFF_ID_CARD_INVALID", "เลขบัตรประชาชนต้องเป็นตัวเลข 13 หลัก", "idCard");
        if (!string.IsNullOrWhiteSpace(request.Email) && !IsEmail(request.Email))
            return new("STAFF_EMAIL_INVALID", "รูปแบบอีเมลไม่ถูกต้อง", "email");
        if (request.Email?.Length > 100)
            return new("STAFF_EMAIL_TOO_LONG", "อีเมลต้องยาวไม่เกิน 100 ตัวอักษร", "email");
        if (request.Salary < 0)
            return new("STAFF_SALARY_INVALID", "เงินเดือนต้องไม่น้อยกว่า 0", "salary");
        if (request.ExperienceYear < 0)
            return new("STAFF_EXPERIENCE_INVALID", "จำนวนปีประสบการณ์ต้องไม่น้อยกว่า 0", "experienceYear");
        if (request.ExperienceMonth is < 0 or > 11)
            return new("STAFF_EXPERIENCE_MONTH_INVALID", "จำนวนเดือนประสบการณ์ต้องอยู่ระหว่าง 0–11", "experienceMonth");
        if (request.BranchId <= 0)
            return new("STAFF_BRANCH_REQUIRED", "กรุณาเลือกสาขา", "branchId");
        if (request.MainSectorId <= 0)
            return new("STAFF_SECTOR_REQUIRED", "กรุณาเลือกแผนกหลัก", "mainSectorId");
        if (request.PositionId <= 0)
            return new("STAFF_POSITION_REQUIRED", "กรุณาเลือกตำแหน่งหลัก", "positionId");
        if (!isCreate && string.IsNullOrWhiteSpace(request.UserName))
            return new("STAFF_USERNAME_REQUIRED", "กรุณากรอกชื่อผู้ใช้", "userName");
        if (!isCreate && request.UserName?.Length > 50)
            return new("STAFF_USERNAME_TOO_LONG", "ชื่อผู้ใช้ต้องยาวไม่เกิน 50 ตัวอักษร", "userName");
        if (!isCreate && !string.IsNullOrWhiteSpace(request.Password) && request.Password.Length > 100)
            return new("STAFF_PASSWORD_TOO_LONG", "รหัสผ่านต้องยาวไม่เกิน 100 ตัวอักษร", "password");
        if (request.EndJobDate is not null && request.StartJobDate is not null && request.EndJobDate < request.StartJobDate)
            return new("STAFF_END_DATE_INVALID", "วันสิ้นสุดงานต้องไม่ก่อนวันเริ่มงาน", "endJobDate");
        return null;
    }

    public static ApiError? ValidateStatus(StaffStatusRequest request) =>
        !request.IsActive && request.EndJobDate is null
            ? new("STAFF_END_DATE_REQUIRED", "กรุณาระบุวันสิ้นสุดงานเมื่อปิดใช้งาน", "endJobDate")
            : null;

    private static bool IsEmail(string value)
    {
        try { _ = new MailAddress(value); return true; }
        catch (FormatException) { return false; }
    }
}
