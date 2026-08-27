using System.Net.Mail;
using System.Text.RegularExpressions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.Customers;

public static partial class CustomerVehicleValidator
{
    [GeneratedRegex(@"^[0-9+()\-\s]{8,20}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^[A-Za-z0-9]{17}$")]
    private static partial Regex VinRegex();

    public static ApiError? ValidateCustomer(CustomerUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName))
            return new("CUSTOMER_FIRST_NAME_REQUIRED", "กรุณากรอกชื่อลูกค้า", "firstName");
        if (string.IsNullOrWhiteSpace(request.LastName))
            return new("CUSTOMER_LAST_NAME_REQUIRED", "กรุณากรอกนามสกุลลูกค้า", "lastName");
        if (string.IsNullOrWhiteSpace(request.PhoneNumber1))
            return new("CUSTOMER_PHONE_REQUIRED", "กรุณากรอกเบอร์โทรศัพท์หลัก", "phoneNumber1");
        if (!PhoneRegex().IsMatch(request.PhoneNumber1.Trim()))
            return new("CUSTOMER_PHONE_INVALID", "รูปแบบเบอร์โทรศัพท์หลักไม่ถูกต้อง", "phoneNumber1");
        if (!string.IsNullOrWhiteSpace(request.PhoneNumber2) && !PhoneRegex().IsMatch(request.PhoneNumber2.Trim()))
            return new("CUSTOMER_PHONE_INVALID", "รูปแบบเบอร์โทรศัพท์สำรองไม่ถูกต้อง", "phoneNumber2");
        if (!string.IsNullOrWhiteSpace(request.IdCard) &&
            (request.IdCard.Length != 13 || request.IdCard.Any(c => !char.IsDigit(c))))
            return new("CUSTOMER_ID_CARD_INVALID", "เลขบัตรประชาชนต้องเป็นตัวเลข 13 หลัก", "idCard");
        if (!string.IsNullOrWhiteSpace(request.Email) && !IsEmail(request.Email))
            return new("CUSTOMER_EMAIL_INVALID", "รูปแบบอีเมลไม่ถูกต้อง", "email");
        if (request.IsBlacklist && string.IsNullOrWhiteSpace(request.BlacklistRemark))
            return new("CUSTOMER_BLACKLIST_REMARK_REQUIRED", "กรุณาระบุเหตุผลที่ติดแบล็กลิสต์", "blacklistRemark");
        return null;
    }

    public static ApiError? ValidateVehicle(VehicleUpsertRequest request)
    {
        if (request.CustomerId <= 0)
            return new("VEHICLE_CUSTOMER_REQUIRED", "กรุณาเลือกลูกค้าที่เป็นเจ้าของรถ", "customerId");
        if (string.IsNullOrWhiteSpace(request.Registration))
            return new("VEHICLE_REGISTRATION_REQUIRED", "กรุณากรอกทะเบียนรถ", "registration");
        if (request.ProvinceId <= 0)
            return new("VEHICLE_PROVINCE_REQUIRED", "กรุณาเลือกจังหวัดจดทะเบียน", "provinceId");
        if (request.BrandId <= 0 || request.ModelId <= 0 || request.NicknameId <= 0)
            return new("VEHICLE_MODEL_REQUIRED", "กรุณาเลือกยี่ห้อ รุ่น และโฉมรถให้ครบ", "nicknameId");
        if (request.YearId <= 0)
            return new("VEHICLE_YEAR_REQUIRED", "กรุณาเลือกปีรถ", "yearId");
        if (!string.IsNullOrWhiteSpace(request.Vin) && !VinRegex().IsMatch(request.Vin.Trim()))
            return new("VEHICLE_VIN_INVALID", "หมายเลขตัวถัง VIN ต้องมี 17 ตัวอักษรหรือตัวเลข", "vin");
        return null;
    }

    private static bool IsEmail(string value)
    {
        try { _ = new MailAddress(value); return true; }
        catch (FormatException) { return false; }
    }
}
