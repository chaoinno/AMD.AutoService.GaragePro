using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.MasterData;

/// <summary>
/// ข้อมูลหลักชุดเริ่มต้นของสาขาใหม่ — หมวดหมู่ 3 หมวด + คลังหลัก 1 แห่ง
/// · [BIZ] ตั้งแต่ ScopeMasterDataByBranch หมวดหมู่/คลังแยกต่อสาขา สาขาใหม่จึงเริ่มจากว่างเปล่า
///   และสร้างสินค้า/เบิก/รับของไม่ได้จนกว่าจะมีคลัง — seed ชุดเล็กๆ ให้เริ่มใช้งานได้ทันที
/// · idempotent ต่อกลุ่ม: สร้างหมวดหมู่เมื่อสาขายังไม่มีหมวดใดเลย และสร้างคลังเมื่อสาขายังไม่มีคลังใดเลย
///   (นับทั้งที่ปิดใช้งาน) — สาขาที่ตั้งค่าเองไว้แล้วจะไม่ถูกเติมของซ้ำหรือของที่ผู้จัดการตั้งใจลบ/ปิดไป
/// · ผู้กระทำใน ActivityEvent คือ "ระบบ" ของสาขานั้น (ScopedCurrentUser · Source=System) ไม่ใช่ผู้ใช้ใน JWT —
///   ผู้เรียก (DevSeed · provisioning ของ self-serve) อาจไม่มี HttpContext
/// · ไม่ปลอดภัยถ้าเรียกพร้อมกันสองคำขอกับสาขาเดียวกัน: unique index (ชาร์ด, สาขา, Code) เป็นด่านสุดท้าย
///   และ <see cref="MasterDataConflictException"/> จะหลุดถึงผู้เรียก — ผู้เรียกปัจจุบันทำงานต่อสาขาแบบลำดับเดียวอยู่แล้ว
/// </summary>
public sealed class BranchDefaultsSeeder(IMasterDataRepository repository, TimeProvider clock) : IBranchDefaultsSeeder
{
    public const string SystemActorName = "ระบบ (ข้อมูลเริ่มต้นสาขา)";

    /// <summary>หมวดหมู่เริ่มต้น (รหัส, ชื่อ) — เรียงตามลำดับที่แสดง</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> DefaultCategories =
    [
        ("SVC", "บริการ/ค่าแรง"),
        ("PART", "อะไหล่"),
        ("FLUID", "น้ำมันและของเหลว")
    ];

    public const string DefaultWarehouseCode = "MAIN";
    public const string DefaultWarehouseName = "คลังหลัก";

    public async Task EnsureAsync(string shardKey, int branchId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(branchId);

        var actor = new ScopedCurrentUser(shardKey, branchId, userId: 0, userName: SystemActorName);
        var now = clock.GetUtcNow().UtcDateTime;
        // ActivityEvent ไม่มีคอลัมน์สาขา — ใส่ไว้ใน payload ให้ตามรอยได้ว่า seed ให้สาขาไหน (แบบเดียวกับ audit ของจัดซื้อ)
        var payload = JsonSerializer.Serialize(new { shardKey, branchId, source = "branch-defaults" });
        var added = false;

        if (!await repository.AnyCategoryAsync(shardKey, branchId, ct))
        {
            var order = 1;
            foreach (var (code, name) in DefaultCategories)
            {
                var category = new CatalogCategory
                {
                    LegacyShardKey = shardKey, LegacyBranchId = branchId,
                    Code = code, Name = name, SortOrder = order++,
                    CreatedDate = now, LastUpdated = now
                };
                await repository.AddCategoryAsync(category, ct);
                await AddEventAsync(MasterDataSupport.Event(actor, category.Id, nameof(CatalogCategory),
                    "category.created", $"เพิ่มหมวดหมู่เริ่มต้น {category.Code} {category.Name}", now), payload, ct);
            }
            added = true;
        }

        if (!await repository.AnyWarehouseAsync(shardKey, branchId, ct))
        {
            var warehouse = new Warehouse
            {
                LegacyShardKey = shardKey, LegacyBranchId = branchId,
                Code = DefaultWarehouseCode, Name = DefaultWarehouseName,
                CreatedDate = now, LastUpdated = now
            };
            await repository.AddWarehouseAsync(warehouse, ct);
            await AddEventAsync(MasterDataSupport.Event(actor, warehouse.Id, nameof(Warehouse),
                "warehouse.created", $"เพิ่มคลังเริ่มต้น {warehouse.Code} {warehouse.Name}", now), payload, ct);
            added = true;
        }

        // หมวดหมู่และคลังบันทึกใน SaveChanges เดียว — สำเร็จหรือไม่สำเร็จพร้อมกัน
        if (added) await repository.SaveChangesAsync(ct);
    }

    private Task AddEventAsync(ActivityEvent activityEvent, string payload, CancellationToken ct)
    {
        activityEvent.PayloadJson = payload;
        return repository.AddEventAsync(activityEvent, ct);
    }
}
