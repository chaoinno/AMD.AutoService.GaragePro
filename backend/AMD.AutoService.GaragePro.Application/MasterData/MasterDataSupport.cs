using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.MasterData;

internal static class MasterDataSupport
{
    public static string Code(string value) => value.Trim().ToUpperInvariant();
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static ApiError? Required(string value, int maxLength, string prefix, string label, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return new($"{prefix}_{field.ToUpperInvariant()}_REQUIRED", $"กรุณากรอก{label}", field);
        return value.Trim().Length > maxLength
            ? new($"{prefix}_{field.ToUpperInvariant()}_TOO_LONG", $"{label}ต้องยาวไม่เกิน {maxLength} ตัวอักษร", field)
            : null;
    }

    public static ApiError? Optional(string? value, int maxLength, string prefix, string label, string field) =>
        value?.Trim().Length > maxLength
            ? new($"{prefix}_{field.ToUpperInvariant()}_TOO_LONG", $"{label}ต้องยาวไม่เกิน {maxLength} ตัวอักษร", field)
            : null;

    public static ApiError? EnsureManager(ICurrentUser user) => user.CanSeeCost
        ? null
        : new("MASTER_DATA_MANAGE_FORBIDDEN", "เฉพาะผู้จัดการสาขาเท่านั้นที่แก้ไขข้อมูลหลักได้");

    public static ActivityEvent Event(
        ICurrentUser user, Guid id, string entityType, string eventType, string description, DateTime now) => new()
    {
        EntityId = id,
        EntityType = entityType,
        EventType = eventType,
        DescriptionTh = description,
        PerformedByUserId = user.UserId,
        PerformedByName = user.UserName,
        Source = user.Source,
        OccurredAt = now
    };

    public static string ConflictCode(MasterDataConflictException ex, string fallback) => ex.ConstraintName switch
    {
        "UX_svc_Supplier_Code" => "SUPPLIER_CODE_DUPLICATE",
        "UX_svc_Warehouse_Code" => "WAREHOUSE_CODE_DUPLICATE",
        "UX_svc_CatalogCategory_Code" => "CATEGORY_CODE_DUPLICATE",
        "UX_svc_CatalogItemSupplier_Preferred" => "SUPPLIER_PREFERRED_DUPLICATE",
        "UX_svc_CatalogItemSupplier_Item_Supplier" => "SUPPLIER_LINK_DUPLICATE",
        _ => fallback
    };
}
