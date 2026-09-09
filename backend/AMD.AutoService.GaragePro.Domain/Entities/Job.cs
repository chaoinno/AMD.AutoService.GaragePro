using AMD.AutoService.GaragePro.Domain.Enums;
using AMD.AutoService.GaragePro.Domain.StateMachine;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// เจ้าของข้อมูลจ๊อบเพียงแหล่งเดียว — สร้าง/แก้ไข/อ่านที่นี่เท่านั้น ไม่เขียนกลับ legacy อีกต่อไป
/// [BIZ] ทุก state change ต้องเขียน ActivityEvent คู่กันเสมอ (ที่ Application layer)
/// อ้างอิง: docs/02-domain-model.md §Job · docs/05-legacy-db-mapping.md §5 (ข้อยกเว้น 2026-08-26 ถูกยกเลิก 2026-08-31)
/// </summary>
public class Job
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // ---- อ้างอิง legacy แบบอ่านอย่างเดียว: CustomerId/VehicleId/BranchId มาจาก Garage เดิม ----
    // LegacyShardKey ยังจำเป็น เพราะ id พวกนี้ไม่ unique ข้ามชาร์ด
    public string LegacyShardKey { get; set; } = "db2";
    public int BranchId { get; set; }
    public long CustomerId { get; set; }
    public long VehicleId { get; set; }

    public string JobNo { get; set; } = string.Empty;

    public JobStatus Status { get; set; } = JobStatus.WaitInspect;

    // ---- snapshot ข้อมูลแสดงผล ณ เวลาที่เปิดจ๊อบ (ไม่ live-read legacy อีกต่อไป) ----
    public string BranchName { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string VehicleRegistration { get; set; } = string.Empty;
    public string? VehicleModel { get; set; }
    public string? VehicleVin { get; set; }
    public string? VehicleImagePath { get; set; }

    /// <summary>9 = รถในอู่ · 10 = รถนัดหมาย (snapshot ของ legacy PJType)</summary>
    public int JobTypeId { get; set; }
    public string? JobTypeName { get; set; }

    /// <summary>ผู้ส่งรถ — อาจไม่ใช่เจ้าของรถที่ลงทะเบียนไว้</summary>
    public string? SenderName { get; set; }
    public string? SenderPhoneNumber { get; set; }
    public string? Detail { get; set; }

    public DateTime? PromiseAt { get; set; }
    public long? AssignedTechnicianId { get; set; }
    public string? AssignedTechnicianName { get; set; }
    public int? MileageAtIntake { get; set; }

    public long CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public EventSource Source { get; set; }

    /// <summary>[UI] ต้องแสดงเป็นข้อความ ไม่ใช่แค่สีแดง เมื่อเกินนัด</summary>
    public string? OverdueReason { get; set; }

    public string? CancelReason { get; set; }
    public long? CancelledByUserId { get; set; }
    public DateTime? CancelledAt { get; set; }

    /// <summary>optimistic concurrency — 409 เมื่อเปลี่ยนสถานะจากข้อมูลเก่า</summary>
    public byte[]? RowVersion { get; set; }

    /// <summary>ขั้นชำระเงินคิด VAT หรือไม่ — default true, ล็อกแก้ไม่ได้ทันทีที่เริ่มบันทึกชำระเงิน/ออกใบเสร็จแล้ว
    /// (ดู PosService.SetVatIncludedAsync) ไม่ติ๊ก = ลดยอดที่ต้องชำระจริง ไม่ใช่แค่ปรับการแสดงผล</summary>
    public bool VatIncluded { get; set; } = true;

    public bool IsOverdue(DateTime nowUtc) =>
        PromiseAt.HasValue && nowUtc > PromiseAt.Value && !JobStateMachine.IsTerminal(Status);
}
