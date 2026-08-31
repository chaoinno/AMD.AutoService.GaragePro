using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.API;

/// <summary>
/// แคตตาล็อกตัวอย่างสำหรับ dev — ค่าตรงกับ prototype (GaragePro Quotation.dc.html)
/// [ASSUME] ของจริงต้องมาจากการตั้งค่าแคตตาล็อกของแต่ละอู่ ไม่ใช่ hard-code
/// </summary>
public static class DevSeed
{
    private const string Shard = "db2";

    /// <summary>
    /// อู่ที่ต้องมีแคตตาล็อกตัวอย่าง
    /// 105 = อู่ตงเจริญยนต์ (3) อู่ที่มีงานมากสุดใน db2 · 227 = Service Center Demo (สาขาที่ใช้ทดสอบจริง)
    /// </summary>
    private static readonly int[] Branches = [105, 227];

    public static async Task RunAsync(ServiceDbContext db)
    {
        foreach (var branchId in Branches)
            await SeedBranchAsync(db, branchId);
    }

    private static async Task SeedBranchAsync(ServiceDbContext db, int branchId)
    {
        // ต้องเช็คทีละอู่ — เดิมเช็ค AnyAsync() รวมทุกอู่ ทำให้อู่ที่ seed ทีหลัง
        // ไม่เคยได้แคตตาล็อกเลย แล้วหน้าแคตตาล็อกขึ้นว่างทั้งที่ตารางมีข้อมูล
        if (await db.CatalogItems.AnyAsync(
                c => c.LegacyShardKey == Shard && c.LegacyBranchId == branchId))
            return;

        db.CatalogItems.AddRange(
            Part(branchId, "P-BRK-0421", "ผ้าเบรกหน้า Bendix MKT", "Yaris Ativ 2563–2566 · Vios", "ชุด", 890, 1290, 6, 2, 0),
            Part(branchId, "P-BRK-0422", "ผ้าเบรกหน้า แท้ศูนย์ Toyota", "Yaris Ativ 2563–2566", "ชุด", 1320, 1850, 2, 2, 4, "ออโต้พาร์ท · 31 ก.ค."),
            Part(branchId, "P-SUS-1188", "โช้คอัพหน้า KYB Excel-G", "Yaris Ativ / Vios 2560–2566", "ต้น", 1780, 2400, 0, 0, 2, "ออโต้พาร์ท · 31 ก.ค."),
            Part(branchId, "P-AC-0310", "กรองแอร์ Denso", "Yaris / Vios / City", "ชิ้น", 260, 450, 14, 1, 0),
            Part(branchId, "P-ENG-2044", "ยางแท่นเครื่อง (ขวา)", "Yaris Ativ 1.2", "ชิ้น", 1240, 1850, 1, 1, 0, "สั่งได้ 2 วัน"),
            Part(branchId, "P-BAT-0755", "แบตเตอรี่ GS 60B24L", "รถเก๋งขนาดเล็ก", "ลูก", 2010, 2750, 5, 0, 0),
            Part(branchId, "P-OIL-1001", "น้ำมันเครื่อง 5W-30 สังเคราะห์ (4 ลิตร)", "ใช้ได้ทุกรุ่นเบนซิน", "แกลลอน", 820, 1250, 3, 0, 12),
            Part(branchId, "P-FLT-0501", "กรองน้ำมันเครื่อง", "Yaris / Vios / Altis · City", "ชิ้น", 120, 220, 22, 3, 0),

            Labor(branchId, "L-BRK-01", "ค่าแรงเปลี่ยนผ้าเบรกหน้า", "มาตรฐาน 1.5 ชม.", 450, 750, 1.5m),
            Labor(branchId, "L-BRK-05", "ค่าแรงเจียจานเบรกหน้า 2 ข้าง", "มาตรฐาน 1.0 ชม.", 420, 800, 1.0m),
            Labor(branchId, "L-SUS-02", "ค่าแรงเปลี่ยนโช้คอัพหน้า (คู่)", "มาตรฐาน 2.0 ชม.", 520, 900, 2.0m),
            Labor(branchId, "L-AC-03", "ล้างแอร์และเปลี่ยนกรองแอร์", "มาตรฐาน 1.0 ชม.", 280, 500, 1.0m),
            Labor(branchId, "L-ALN-04", "ตั้งศูนย์และถ่วงล้อ", "มาตรฐาน 1.0 ชม.", 320, 600, 1.0m),
            Labor(branchId, "L-PM40-01", "ค่าแรงเช็คระยะ 40,000 กม.", "มาตรฐาน 2.0 ชม.", 480, 900, 2.0m));

        await db.SaveChangesAsync();
    }

    private static CatalogItem Part(
        int branchId,
        string code, string name, string compat, string unit,
        decimal cost, decimal price, int onHand, int reserved, int onOrder, string? eta = null) => new()
    {
        Code = code,
        Type = LineType.Part,
        Name = name,
        Compatibility = compat,
        Unit = unit,
        Cost = cost,
        Price = price,
        OnHand = onHand,
        Reserved = reserved,
        OnOrder = onOrder,
        EtaNote = eta,
        LegacyShardKey = Shard,
        LegacyBranchId = branchId
    };

    private static CatalogItem Labor(
        int branchId,
        string code, string name, string compat, decimal cost, decimal price, decimal hours) => new()
    {
        Code = code,
        Type = LineType.Labor,
        Name = name,
        Compatibility = compat,
        Unit = "งาน",
        Cost = cost,
        Price = price,
        StandardHours = hours,
        LegacyShardKey = Shard,
        LegacyBranchId = branchId
    };
}
