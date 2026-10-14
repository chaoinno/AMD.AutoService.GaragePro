using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// เนื้อหาข้อมูลตัวอย่างของอู่ทดลองใช้ (S3) — ข้อมูลล้วน ไม่มี I/O จึงทดสอบความถูกต้องได้โดยไม่ต้องต่อฐาน (StarterCatalogTests)
/// · [BIZ] ต้องดูเหมือนอู่เซอร์วิสไทยจริง ผู้สมัครจะเปิดเห็นทันทีหลังสมัคร — ราคา/ชั่วโมงเป็นค่าตลาดโดยประมาณ ไม่ใช่ราคามาตรฐานของใคร
/// · ทุกชื่อที่คนเห็นในรายการลูกค้า/พนักงานมี "(ตัวอย่าง)" กำกับ ให้แยกออกจากข้อมูลจริงได้ด้วยตา
///   (แคตตาล็อก/เทมเพลตไม่มี เพราะผู้ใช้มักเก็บไว้ใช้ต่อจริง — ล้างข้อมูลตัวอย่างจะไม่ลบรายการที่ถูกใช้ในใบเสนอราคาจริงแล้ว)
/// · หมวดหมู่อ้างรหัสของ <see cref="MasterData.BranchDefaultsSeeder.DefaultCategories"/> (B2) — ไม่สร้างหมวดของตัวเอง
/// </summary>
public static class StarterCatalog
{
    public const string LaborCategoryCode = "SVC";
    public const string PartCategoryCode = "PART";
    public const string FluidCategoryCode = "FLUID";

    public const string LaborUnit = "งาน";

    /// <summary>ข้อความกำกับที่ต่อท้ายชื่อคน/ใส่ในหมายเหตุ — ใช้ร่วมกับเหตุผลการเปลี่ยนสถานะจ๊อบตัวอย่าง</summary>
    public const string SampleMark = "(ตัวอย่าง)";

    public sealed record Item(
        string Code,
        LineType Type,
        string Name,
        string Unit,
        decimal Cost,
        decimal Price,
        decimal? StandardHours,
        int OnHand,
        string CategoryCode);

    private static Item Labor(string code, string name, decimal price, decimal hours) =>
        new(code, LineType.Labor, name, LaborUnit, 0m, price, hours, 0, LaborCategoryCode);

    private static Item Part(string code, string name, string unit, decimal cost, decimal price, int onHand,
        string category = PartCategoryCode) =>
        new(code, LineType.Part, name, unit, cost, price, null, onHand, category);

    /// <summary>ค่าแรง 21 รายการ — ต้นทุน 0 (ค่าแรงช่างไม่ได้คิดต้นทุนต่อรายการในระบบนี้) · มีชั่วโมงมาตรฐานทุกรายการ</summary>
    public static readonly IReadOnlyList<Item> LaborItems =
    [
        Labor("L-OIL-01", "เปลี่ยนถ่ายน้ำมันเครื่อง+กรอง", 150m, 0.5m),
        Labor("L-PM10-01", "เช็คระยะ 10,000 กม.", 350m, 1m),
        Labor("L-PM20-01", "เช็คระยะ 20,000 กม.", 500m, 1.5m),
        Labor("L-PM40-01", "เช็คระยะ 40,000 กม.", 900m, 2m),
        Labor("L-BRK-01", "เปลี่ยนผ้าเบรกหน้า", 400m, 1m),
        Labor("L-BRK-02", "เปลี่ยนผ้าเบรกหลัง", 400m, 1m),
        Labor("L-BRK-03", "เจียจานเบรก", 600m, 1m),
        Labor("L-BRK-04", "เปลี่ยนน้ำมันเบรก", 250m, 0.5m),
        Labor("L-BAT-01", "เปลี่ยนแบตเตอรี่", 100m, 0.3m),
        Labor("L-TIR-01", "ถอด-ใส่ยาง+ถ่วงล้อ", 150m, 0.5m),
        Labor("L-TIR-02", "ตั้งศูนย์ล้อ", 500m, 1m),
        Labor("L-AC-01", "ล้างแอร์", 900m, 2m),
        Labor("L-AC-02", "เติมน้ำยาแอร์", 400m, 0.5m),
        Labor("L-AC-03", "เปลี่ยนกรองแอร์", 80m, 0.2m),
        Labor("L-SUS-01", "เปลี่ยนโช้คอัพหน้า (คู่)", 900m, 2m),
        Labor("L-SUS-02", "เปลี่ยนลูกหมาก", 450m, 1m),
        Labor("L-ENG-01", "เปลี่ยนหัวเทียน", 250m, 0.5m),
        Labor("L-ENG-02", "เปลี่ยนสายพาน", 450m, 1m),
        Labor("L-COOL-01", "เปลี่ยนน้ำยาหล่อเย็น", 350m, 0.5m),
        Labor("L-CHK-01", "ตรวจเช็ค 20 รายการ", 200m, 0.5m),
        Labor("L-ELC-01", "ตรวจระบบไฟ/ไดชาร์จ", 400m, 1m)
    ];

    /// <summary>อะไหล่ 21 รายการ — ต้นทุน &lt; ราคาขายทุกรายการ · ยอดคงเหลือเริ่มต้น 4–20 (ให้หน้าใบเสนอราคาแสดง "พร้อมใช้" ได้)</summary>
    public static readonly IReadOnlyList<Item> PartItems =
    [
        Part("P-OIL-5W30-4L", "น้ำมันเครื่องสังเคราะห์ 5W-30 (4 ลิตร)", "แกลลอน", 820m, 1250m, 12, FluidCategoryCode),
        Part("P-OIL-10W40-4L", "น้ำมันเครื่องกึ่งสังเคราะห์ 10W-40 (4 ลิตร)", "แกลลอน", 550m, 850m, 8, FluidCategoryCode),
        Part("P-OIL-DSL-6L", "น้ำมันเครื่องดีเซล 15W-40 (6 ลิตร)", "แกลลอน", 780m, 1150m, 6, FluidCategoryCode),
        Part("P-FLT-OIL", "กรองน้ำมันเครื่อง", "ลูก", 120m, 220m, 20),
        Part("P-FLT-AIR", "กรองอากาศ", "ลูก", 250m, 420m, 10),
        Part("P-FLT-AC", "กรองแอร์", "ลูก", 180m, 350m, 10),
        Part("P-FLT-FUEL", "กรองน้ำมันเชื้อเพลิง", "ลูก", 350m, 600m, 6),
        Part("P-BRK-PAD-F", "ผ้าเบรกหน้า", "ชุด", 890m, 1290m, 6),
        Part("P-BRK-PAD-R", "ผ้าเบรกหลัง", "ชุด", 750m, 1100m, 6),
        Part("P-BRK-FLUID", "น้ำมันเบรก DOT 4 (0.5 ลิตร)", "กระป๋อง", 150m, 250m, 10, FluidCategoryCode),
        Part("P-BAT-60B24L", "แบตเตอรี่ 60B24L", "ลูก", 2010m, 2750m, 4),
        Part("P-BAT-80D26L", "แบตเตอรี่ 80D26L", "ลูก", 2600m, 3400m, 4),
        Part("P-TIR-185-60-15", "ยางรถยนต์ 185/60R15", "เส้น", 1900m, 2500m, 8),
        Part("P-TIR-215-60-16", "ยางรถยนต์ 215/60R16", "เส้น", 2800m, 3600m, 4),
        Part("P-PLUG-IR", "หัวเทียนอิริเดียม", "หัว", 280m, 450m, 16),
        Part("P-BELT-SERP", "สายพานหน้าเครื่อง", "เส้น", 650m, 950m, 4),
        Part("P-COOL-1L", "น้ำยาหล่อเย็น (1 ลิตร)", "ขวด", 180m, 280m, 12, FluidCategoryCode),
        Part("P-AC-R134A", "น้ำยาแอร์ R-134a", "กระป๋อง", 180m, 350m, 10, FluidCategoryCode),
        Part("P-WIPER-SET", "ใบปัดน้ำฝน (คู่)", "ชุด", 350m, 590m, 6),
        Part("P-SHOCK-F", "โช้คอัพหน้า", "ต้น", 1780m, 2400m, 4),
        Part("P-BULB-H4", "หลอดไฟหน้า H4", "ดวง", 120m, 250m, 10)
    ];

    /// <summary>ทั้งหมดตามลำดับที่สร้าง (ค่าแรงก่อน อะไหล่ทีหลัง)</summary>
    public static readonly IReadOnlyList<Item> Items = [.. LaborItems, .. PartItems];

    public static Item? Find(string code) =>
        Items.FirstOrDefault(i => string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase));

    public sealed record TemplateLine(string CatalogCode, decimal Quantity);

    public sealed record Template(string Code, string Name, string Description, IReadOnlyList<TemplateLine> Lines);

    public static readonly IReadOnlyList<Template> Templates =
    [
        new("TPL-PM10", "เช็คระยะ 10,000 กม.", "ชุดตัวอย่าง — ค่าแรงเช็คระยะ + น้ำมันเครื่อง + กรองน้ำมันเครื่อง",
        [
            new("L-PM10-01", 1m),
            new("P-OIL-5W30-4L", 1m),
            new("P-FLT-OIL", 1m)
        ]),
        new("TPL-BRK-F", "เปลี่ยนผ้าเบรกหน้า", "ชุดตัวอย่าง — ค่าแรงเปลี่ยนผ้าเบรก + ผ้าเบรกหน้า + เจียจานเบรก",
        [
            new("L-BRK-01", 1m),
            new("P-BRK-PAD-F", 1m),
            new("L-BRK-03", 1m)
        ])
    ];

    public sealed record Technician(string FirstName, string LastName, string Phone);

    /// <summary>ช่างตัวอย่าง 2 คน — ระบบออกชื่อผู้ใช้ SP{no}.T01/T02 + รหัสผ่านให้เอง (B5) แล้วคืนให้เจ้าของอู่ครั้งเดียว</summary>
    public static readonly IReadOnlyList<Technician> Technicians =
    [
        new("ช่างเอก", SampleMark, "0800000001"),
        new("ช่างบอย", SampleMark, "0800000002")
    ];

    public sealed record Customer(string Code, string FirstName, string LastName, string Phone);

    public static readonly IReadOnlyList<Customer> Customers =
    [
        new("SAMPLE-C01", "คุณสมชาย", $"ใจดี {SampleMark}", "0811111111"),
        new("SAMPLE-C02", "คุณสุดา", $"รักรถ {SampleMark}", "0822222222")
    ];

    /// <summary>รถของลูกค้าตัวอย่างลำดับเดียวกัน (index ตรงกับ <see cref="Customers"/>) · ยี่ห้อ/รุ่น/ปีเลือกจากข้อมูลอ้างอิงจริงตอนสร้าง</summary>
    public sealed record Vehicle(string Code, string Registration);

    public static readonly IReadOnlyList<Vehicle> Vehicles =
    [
        new("SAMPLE-V01", "1กข 1234"),
        new("SAMPLE-V02", "2ขค 5678")
    ];

    /// <summary>ปีรถตัวอย่าง = ปีปัจจุบัน − ค่านี้ (รถอายุ 3 ปี — ช่วงที่เข้าเช็คระยะ/เปลี่ยนเบรกบ่อยที่สุด)</summary>
    public const int VehicleAgeYears = 3;

    public sealed record JobLine(string CatalogCode, decimal Quantity, LineSource Source);

    /// <summary>
    /// จ๊อบตัวอย่าง — <paramref name="Index"/> ชี้ลูกค้า/รถลำดับเดียวกัน
    /// · <paramref name="ApproveAndStart"/> = false → จบที่ "รอลูกค้าอนุมัติ" (ใบเสนอราคาส่งแล้ว ให้ลองกดอนุมัติแทนลูกค้าเอง)
    ///   true → ลูกค้าอนุมัติทุกบรรทัด + เซ็น → "กำลังซ่อม" (ให้ช่างตัวอย่างลองกดจับเวลาบนแอป)
    /// · บรรทัดค่าแรงระบุช่างตัวอย่างคนแรกให้เอง ([BIZ] ค่าแรงต้องระบุช่างก่อนส่งใบเสนอราคา)
    /// </summary>
    public sealed record Job(string Key, int Index, int MileageAtIntake, string Detail, bool ApproveAndStart,
        IReadOnlyList<JobLine> Lines);

    public static readonly IReadOnlyList<Job> Jobs =
    [
        new("A", 0, 45_210, $"เข้าเช็คระยะตามกำหนด แอร์ไม่ค่อยเย็น {SampleMark}", ApproveAndStart: false,
        [
            new("L-PM10-01", 1m, LineSource.Customer),
            new("P-OIL-5W30-4L", 1m, LineSource.Customer),
            new("P-FLT-OIL", 1m, LineSource.Customer),
            new("L-AC-03", 1m, LineSource.Technician),
            new("P-FLT-AC", 1m, LineSource.Technician)
        ]),
        new("B", 1, 68_900, $"เบรกมีเสียงดังเวลาเหยียบ ลูกค้าขอเปลี่ยนผ้าเบรก {SampleMark}", ApproveAndStart: true,
        [
            new("L-BRK-01", 1m, LineSource.Customer),
            new("P-BRK-PAD-F", 1m, LineSource.Customer),
            new("L-BRK-03", 1m, LineSource.Customer),
            new("L-BRK-04", 1m, LineSource.Technician),
            new("P-BRK-FLUID", 1m, LineSource.Technician)
        ])
    ];
}
