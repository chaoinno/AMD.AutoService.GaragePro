namespace AMD.AutoService.GaragePro.Domain.Common;

public sealed record IntakeChecklistTemplateItem(
    string CategoryKey,
    string CategoryLabelTh,
    string ItemCode,
    string LabelTh,
    string? HintTh,
    int SortOrder);

/// <summary>
/// แคตตาล็อกคงที่ของ checklist สภาพรถขณะรับ — 4 หมวด 20 รายการ
/// [BIZ] itemCode ที่ใช้ได้มาจากที่นี่เท่านั้น ห้าม client ส่ง itemCode อื่นเข้ามา (validate ที่ Service)
///
/// granularity ในลิสต์นี้ไม่สม่ำเสมอโดยตั้งใจ (เช่น "กระจกมองข้าง ซ้าย/ขวา" รวมเป็นข้อเดียว
/// ต่างจาก "แก้มข้าง & ประตู" ที่แยกซ้าย/ขวาคนละข้อ) — คัดลอกตามฟอร์มกระดาษต้นฉบับที่ผู้ใช้ยืนยันแล้ว
/// ถ้าต้องการระบุตำแหน่งที่ชำรุดแม่นยำกว่านี้ (กันข้อพิพาท) ค่อยแยกเพิ่มทีหลังได้ — itemCode ใหม่ไม่กระทบของเดิม
/// </summary>
public static class IntakeChecklistTemplate
{
    public const string Exterior = "exterior";
    public const string Wheels = "wheels";
    public const string Interior = "interior";
    public const string UnderHood = "underhood";

    public static readonly IReadOnlyList<IntakeChecklistTemplateItem> Items =
    [
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext1", "กันชนหน้า / กระจังหน้า", "รอยหินดีด / ครูดใต้กันชน", 1),
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext2", "ไฟหน้า / ไฟเลี้ยว / ไฟตัดหมอก", "โคมร้าว / น้ำเข้า / ไฟติดครบ", 2),
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext3", "กระจกบังลมหน้า / ใบปัดน้ำฝน", "รอยหินดีด / รอยร้าว", 3),
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext4", "แก้มข้าง & ประตู (ซ้าย)", "รอยลักยิ้ม / ขูดขีด", 4),
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext5", "แก้มข้าง & ประตู (ขวา)", "รอยลักยิ้ม / ขูดขีด", 5),
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext6", "กระจกมองข้าง ซ้าย / ขวา", "รอยครูด / การพับไฟฟ้า", 6),
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext7", "หลังคารถ / เสาอากาศ", "รอยบุบ / รอยมูลนกฝังลึก", 7),
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext8", "ฝากระโปรงท้าย / กันชนหลัง", "รอยขนของ / รอยชนท้าย", 8),
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext9", "ไฟท้าย / ไฟเบรกดวงที่ 3", "โคมร้าว / หลอดขาด", 9),
        new(Exterior, "ตรวจสอบภายนอกรอบคัน", "ext10", "กระจกบังลมหลัง / ไล่ฝ้า", "ฟิล์มบวม / รอยร้าว", 10),

        new(Wheels, "ล้อและยาง", "whl1", "ล้อแม็กซ์ หน้าซ้าย / หลังซ้าย", "รอยเบียดฟุตบาท", 1),
        new(Wheels, "ล้อและยาง", "whl2", "ล้อแม็กซ์ หน้าขวา / หลังขวา", "รอยเบียดฟุตบาท", 2),
        new(Wheels, "ล้อและยาง", "whl3", "สภาพยางภายนอก 4 ล้อ", "เนื้อยางฉีก / บวม / ตะปูตำ", 3),

        new(Interior, "ภายในห้องโดยสาร", "cab1", "ไฟหน้าปัดรถยนต์", "ไม่มีไฟเครื่องยนต์ / Airbag / ABS โชว์ค้าง", 1),
        new(Interior, "ภายในห้องโดยสาร", "cab2", "กล้องบันทึกหน้ารถ / หลังรถ", "เปิดติด / มีการ์ดความจำ", 2),
        new(Interior, "ภายในห้องโดยสาร", "cab3", "ทรัพย์สินมีค่าของลูกค้า", "แจ้งนำออกเรียบร้อย หรือ ลงบันทึกเฉพาะชิ้น", 3),
        new(Interior, "ภายในห้องโดยสาร", "cab4", "เบาะนั่ง / คอนโซล / แผงประตู", "รอยฉีกขาด / คราบเปื้อนก่อนซ่อม", 4),

        new(UnderHood, "ห้องเครื่องยนต์เบื้องต้น", "uh1", "ระดับของเหลว", "น้ำมันเครื่อง / น้ำหม้อน้ำ / น้ำมันเบรก อยู่ในเกณฑ์", 1),
        new(UnderHood, "ห้องเครื่องยนต์เบื้องต้น", "uh2", "รอยรั่วซึมที่มองเห็น", "ไม่มีคราบน้ำมันเยิ้ม / คราบน้ำยาแอร์", 2),
        new(UnderHood, "ห้องเครื่องยนต์เบื้องต้น", "uh3", "สภาพแบตเตอรี่เบื้องต้น", "ไม่มีขี้เกลือขึ้นที่ขั้ว", 3),
    ];

    private static readonly HashSet<string> ValidCodes = Items.Select(i => i.ItemCode).ToHashSet();

    public static bool IsValidItemCode(string itemCode) => ValidCodes.Contains(itemCode);
}
