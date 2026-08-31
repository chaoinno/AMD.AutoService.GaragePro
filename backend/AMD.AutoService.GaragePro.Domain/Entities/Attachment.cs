namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// ไฟล์แนบ — ลายเซ็น รูปรับรถ รูปตรวจเช็ค รูปก่อน/หลัง
///
/// [BIZ] DB เก็บเฉพาะ metadata + path · ตัวไฟล์อยู่บน storage
/// ประเมินไว้ ~25–30 ไฟล์ต่องาน (docs/02-domain-model.md) จึงห้ามเก็บ blob ลง DB
/// </summary>
public class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid JobId { get; set; }
    public Job? Job { get; set; }

    /// <summary>signature | intake | inspection | repair-before | repair-after | qc | document</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>เอกสาร/รายการที่ไฟล์นี้ผูกอยู่ เช่น QuotationId</summary>
    public Guid? EntityId { get; set; }

    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }

    /// <summary>path ใต้ storage root — ไม่เก็บ absolute path เพื่อให้ย้าย storage ได้</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>ตรวจไฟล์ซ้ำและยืนยันความถูกต้องหลังย้าย storage</summary>
    public string? Sha256 { get; set; }

    public long UploadedByUserId { get; set; }
    public string UploadedByName { get; set; } = string.Empty;
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
