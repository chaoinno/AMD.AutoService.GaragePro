using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// Audit trail — [BIZ] ทุก state change ต้องเขียนพร้อม Source (มือถือ/เว็บ/ระบบ)
/// timeline บนหน้ารายละเอียดงานแสดงรวมทั้ง 2 แพลตฟอร์ม
/// </summary>
public class ActivityEvent
{
    public long Id { get; set; }

    public string LegacyShardKey { get; set; } = "db2";
    public int LegacyBranchId { get; set; }
    public long LegacyJobId { get; set; }

    /// <summary>เอกสารที่เกี่ยวข้อง เช่น QuotationId</summary>
    public Guid? EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;

    /// <summary>เช่น quotation.created · quotation.sent · quotation.revised</summary>
    public string EventType { get; set; } = string.Empty;
    public string DescriptionTh { get; set; } = string.Empty;

    public long PerformedByUserId { get; set; }
    public string PerformedByName { get; set; } = string.Empty;
    public EventSource Source { get; set; }

    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    /// <summary>ข้อมูลเพิ่มเติมเป็น JSON — ไม่ index ไม่ query</summary>
    public string? PayloadJson { get; set; }
}
