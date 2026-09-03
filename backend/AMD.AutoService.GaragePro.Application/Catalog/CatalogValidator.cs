using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Catalog;

public static class CatalogValidator
{
    public static ApiError? Validate(CatalogUpsertRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
            return new("CATALOG_CODE_REQUIRED", "กรุณากรอกรหัสสินค้า", "code");
        if (request.Code.Trim().Length > 60)
            return new("CATALOG_CODE_TOO_LONG", "รหัสสินค้าต้องยาวไม่เกิน 60 ตัวอักษร", "code");
        if (!Enum.IsDefined(request.Type))
            return new("CATALOG_TYPE_REQUIRED", "กรุณาเลือกประเภทสินค้า", "type");
        if (string.IsNullOrWhiteSpace(request.Name))
            return new("CATALOG_NAME_REQUIRED", "กรุณากรอกชื่อสินค้า", "name");
        if (request.Name.Trim().Length > 300)
            return new("CATALOG_NAME_TOO_LONG", "ชื่อสินค้าต้องยาวไม่เกิน 300 ตัวอักษร", "name");
        if (request.Compatibility?.Trim().Length > 500)
            return new("CATALOG_COMPATIBILITY_TOO_LONG", "ข้อมูลรุ่นรถที่รองรับต้องยาวไม่เกิน 500 ตัวอักษร", "compatibility");
        if (string.IsNullOrWhiteSpace(request.Unit))
            return new("CATALOG_UNIT_REQUIRED", "กรุณากรอกหน่วยนับ", "unit");
        if (request.Unit.Trim().Length > 40)
            return new("CATALOG_UNIT_TOO_LONG", "หน่วยนับต้องยาวไม่เกิน 40 ตัวอักษร", "unit");
        if (request.Cost < 0)
            return new("CATALOG_COST_INVALID", "ต้นทุนต้องไม่น้อยกว่า 0", "cost");
        if (request.Price < 0)
            return new("CATALOG_PRICE_INVALID", "ราคาขายต้องไม่น้อยกว่า 0", "price");
        if (request.Type == LineType.Labor && request.StandardHours is null or <= 0)
            return new("CATALOG_HOURS_REQUIRED", "ค่าแรงต้องระบุชั่วโมงมาตรฐานมากกว่า 0", "standardHours");
        if (request.StandardHours > 9999.99m)
            return new("CATALOG_HOURS_INVALID", "ชั่วโมงมาตรฐานต้องไม่เกิน 9,999.99 ชั่วโมง", "standardHours");
        if (request.OnHand < 0 || request.Reserved < 0 || request.OnOrder < 0 || request.Damaged < 0)
            return new("CATALOG_STOCK_INVALID", "จำนวนสต็อกต้องไม่น้อยกว่า 0", "onHand");
        if (request.EtaNote?.Trim().Length > 200)
            return new("CATALOG_ETA_TOO_LONG", "หมายเหตุกำหนดรับต้องยาวไม่เกิน 200 ตัวอักษร", "etaNote");
        return null;
    }
}
