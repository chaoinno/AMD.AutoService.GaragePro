using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>รหัส flow ใน <c>PackageOverviewDto.Flow</c> (สัญญากับเว็บ/แอป docs/17 §8.7)</summary>
public static class PackageFlows
{
    /// <summary>อู่ที่สมัครเอง (shard sp1) · ข้อมูลอยู่ที่ sp_PackagePayment ของเรา</summary>
    public const string SelfServe = "selfserve";

    /// <summary>สาขา GaragePro กลุ่ม Service (db1/db2) · ข้อมูลอยู่ที่ระบบแจ้งชำระเดิม ServicePro อ่านอย่างเดียว</summary>
    public const string GaragePro = "garagepro";
}

/// <summary>
/// แถว Branch ของสาขา GaragePro (อ่านอย่างเดียว · วันที่แปลงเป็น UTC แล้ว) · <see cref="BranchGroupId"/> null = ข้อมูลไม่ครบ (ไม่ใช่กลุ่ม Service)
/// </summary>
public sealed record GarageProBranchPackage(int BranchId, string Name, int Status, int? BranchGroupId, DateTime? EndsAtUtc);

/// <summary>
/// หนึ่งแถวของตารางแจ้งชำระเดิม (อ่านอย่างเดียว · วันที่แปลงเป็น UTC แล้ว · ไม่อ่านคอลัมน์ LOB)
/// · <see cref="Status"/>: 1 รอตรวจ · 2 ยืนยันแล้ว · 0 ยกเลิก · -1 ยกเลิกอัตโนมัติ · null = ข้อมูลเสีย
/// · <see cref="ForEndUtc"/> = วันหมดใหม่ที่ระบบเดิมคำนวณไว้ตอนแจ้ง · <see cref="SlipPath"/> = path relative ที่ระบบเดิมเก็บ
/// </summary>
public sealed record GarageProPaymentRow(
    long Id,
    int? Status,
    DateTime? PaidAtUtc,
    DateTime? CreatedAtUtc,
    string? CreatedByName,
    DateTime? ForEndUtc,
    string? SlipPath);

/// <summary>
/// อ่าน Branch + ตารางแจ้งชำระของระบบเดิม (Infrastructure: Dapper · เลือกคอลัมน์ · <c>WITH (READUNCOMMITTED)</c> · ไม่มีคำสั่งเขียน)
/// </summary>
public interface IGarageProPackageReader
{
    Task<GarageProBranchPackage?> GetBranchAsync(string shardKey, int branchId, CancellationToken ct = default);

    /// <summary>ใหม่สุดก่อน</summary>
    Task<IReadOnlyList<GarageProPaymentRow>> ListPaymentsAsync(string shardKey, int branchId, int take,
        CancellationToken ct = default);

    /// <summary>รายการรอตรวจล่าสุดของสาขา (Status = 1) · null = ไม่มี</summary>
    Task<GarageProPaymentRow?> FindPendingAsync(string shardKey, int branchId, CancellationToken ct = default);

    Task<GarageProPaymentRow?> GetPaymentAsync(string shardKey, int branchId, long id, CancellationToken ct = default);
}

/// <summary>คำขอแจ้งชำระที่ส่งให้ API เดิม · <see cref="PaidAtThai"/> = เวลาไทย (API เดิมรับวันกับเวลาแยกช่อง)</summary>
public sealed record GarageProAddPaymentRequest(
    int Zone,
    int BranchId,
    DateTime PaidAtThai,
    int MonthCount,
    long CreatedByUserId,
    byte[] Slip,
    string SlipFileName,
    string SlipContentType);

public enum GarageProAddPaymentOutcome
{
    /// <summary>ระบบเดิมรับแล้ว (<c>Success: true</c>)</summary>
    Accepted,

    /// <summary>ระบบเดิมตอบชัดว่าไม่รับ (<c>Success: false</c>) · ไม่มีอะไรถูกบันทึก</summary>
    Rejected,

    /// <summary>ไม่ทราบผล (เชื่อมต่อไม่ได้ · หมดเวลา · HTTP ผิดปกติ · คำตอบอ่านไม่ออก) · อาจบันทึกไปแล้ว</summary>
    Unavailable
}

/// <param name="Message">ข้อความจากระบบเดิม (ตัดเหลือบรรทัดเดียวแล้ว) · ใช้เมื่อ <see cref="GarageProAddPaymentOutcome.Rejected"/></param>
/// <param name="PaymentId">เลขแถวที่ระบบเดิมสร้าง (ถ้าตอบมา)</param>
public sealed record GarageProAddPaymentResult(GarageProAddPaymentOutcome Outcome, string? Message = null, long? PaymentId = null);

/// <summary>
/// ส่งการแจ้งชำระให้ API เดิม (Infrastructure: HttpClient multipart · timeout 30 วิ · ไม่ตาม redirect)
/// · ไม่โยน exception ยกเว้นผู้เรียกยกเลิกเอง · ทุกความล้มเหลวกลายเป็น <see cref="GarageProAddPaymentOutcome.Unavailable"/>
/// </summary>
public interface IGarageProPaymentGateway
{
    /// <summary>ตั้ง <c>SelfServe:Package:GarageProApi:BaseUrl</c> แล้ว</summary>
    bool IsConfigured { get; }

    Task<GarageProAddPaymentResult> AddPaymentAsync(GarageProAddPaymentRequest request, CancellationToken ct = default);
}

/// <summary>สลิปที่แปลงแล้วพร้อมส่ง (JPEG ไม่มี EXIF หรือ PDF ตามเดิม)</summary>
public sealed record PreparedSlip(byte[] Content, string ContentType, string Extension);

/// <summary>
/// ตรวจ + แปลงสลิปโดยไม่เก็บลง FTP (ใช้กับ flow garagepro ที่ระบบเดิมเป็นคนเก็บไฟล์) · กติกาเดียวกับ <see cref="IPackageSlipStorage"/>
/// </summary>
public interface IPackageSlipNormalizer
{
    long MaxSizeBytes { get; }

    AttachmentValidation Validate(string? contentType, long sizeBytes);

    /// <exception cref="InvalidDataException">ไม่ใช่รูป PNG/JPEG/WebP หรือ PDF จริง · ไฟล์เสีย · ใหญ่เกิน</exception>
    Task<PreparedSlip> NormalizeAsync(Stream content, CancellationToken ct = default);
}
