using System.Linq.Expressions;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Common;

/// <summary>
/// กฎ min-max ของสต็อกอะไหล่ — แหล่งเดียวของ "ถึงจุดต่ำสุด" และ "จำนวนแนะนำให้สั่ง"
/// ใช้ทั้ง query นับ/กรอง (ผ่าน <see cref="BelowMin"/> ที่แปลงเป็น SQL ได้) DTO และเทสต์ — ห้ามเขียนเงื่อนไขซ้ำที่อื่น
/// [BIZ] พร้อมใช้ = OnHand − Reserved (กฎข้อ 6: ไม่รวม OnOrder ไม่รวม Damaged)
/// · เตือนที่ระดับเดียว: พร้อมใช้ ≤ min (ไม่มีเกณฑ์ % ล่วงหน้า · ไม่เตือนเมื่อเกิน max)
/// · max ใช้คำนวณจำนวนแนะนำเท่านั้น และนับของที่สั่งไว้แล้ว (OnOrder) เพื่อไม่แนะนำสั่งซ้ำ
/// </summary>
public static class StockLevel
{
    public static int Available(int onHand, int reserved) => onHand - reserved;

    /// <summary>ไม่ได้ตั้ง min (null) = ไม่เคยเตือน/ไม่ถูกนับ</summary>
    public static bool IsBelowMin(int onHand, int reserved, int? minStock) =>
        minStock.HasValue && Available(onHand, reserved) <= minStock.Value;

    /// <summary>null เมื่อไม่ได้ตั้ง max · ไม่ติดลบ</summary>
    public static int? SuggestedOrderQty(int onHand, int reserved, int onOrder, int? maxStock) =>
        maxStock.HasValue ? Math.Max(0, maxStock.Value - Available(onHand, reserved) - onOrder) : null;

    public static bool IsBelowMin(CatalogItem item) =>
        item.Type == LineType.Part && IsBelowMin(item.OnHand, item.Reserved, item.MinStock);

    public static int? SuggestedOrderQty(CatalogItem item) =>
        item.Type == LineType.Part ? SuggestedOrderQty(item.OnHand, item.Reserved, item.OnOrder, item.MaxStock) : null;

    /// <summary>
    /// เงื่อนไขเดียวกับ <see cref="IsBelowMin(CatalogItem)"/> ในรูป expression สำหรับ EF
    /// (เฉพาะอะไหล่ — ค่าแรงไม่มีสต็อก) · ผู้เรียกกรอง IsActive/สาขาเอง
    /// </summary>
    public static readonly Expression<Func<CatalogItem, bool>> BelowMin = c =>
        c.Type == LineType.Part && c.MinStock != null && c.OnHand - c.Reserved <= c.MinStock;
}
