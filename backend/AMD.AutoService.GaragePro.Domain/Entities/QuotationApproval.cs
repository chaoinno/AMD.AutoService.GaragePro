namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// บันทึกการอนุมัติของลูกค้า — [BIZ] ผูกกับ QuotationVersion เสมอ
/// ออกเวอร์ชันใหม่ = ApprovalRecord เดิมใช้ไม่ได้ ต้องอนุมัติใหม่ทั้งชุด
/// </summary>
public class QuotationApproval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid QuotationId { get; set; }

    /// <summary>เวอร์ชันที่ลายเซ็นนี้ผูกอยู่ — validate ก่อนให้เริ่มซ่อมเสมอ</summary>
    public int QuotationVersion { get; set; }

    /// <summary>ลายเซ็นเก็บเป็นไฟล์ — DB เก็บแค่ path</summary>
    public string SignatureImagePath { get; set; } = string.Empty;
    public DateTime SignedAt { get; set; }

    /// <summary>ข้อความยินยอมที่ลูกค้าเห็นตอนเซ็น (เก็บไว้ทั้งข้อความ)</summary>
    public string ConsentText { get; set; } = string.Empty;

    /// <summary>เครื่องที่ใช้เซ็น — [BIZ] ต้องเก็บเพื่อการตรวจสอบย้อนหลัง</summary>
    public string? DeviceInfo { get; set; }

    /// <summary>พนักงานผู้รับรองการเซ็น</summary>
    public long WitnessEmployeeId { get; set; }
    public string WitnessEmployeeName { get; set; } = string.Empty;

    public int ApprovedLineCount { get; set; }
    public int RejectedLineCount { get; set; }
    public decimal ApprovedNetAmount { get; set; }

    public Quotation? Quotation { get; set; }
}
