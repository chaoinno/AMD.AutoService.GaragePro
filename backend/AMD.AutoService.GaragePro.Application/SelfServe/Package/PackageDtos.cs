namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

// สัญญา JSON ของ docs/17 §5.1 (camelCase · ฟิลด์ null ไม่อยู่ใน JSON เพราะ WhenWritingNull) · ห้ามเปลี่ยนชื่อฟิลด์:
// เว็บ (PR 2) แอป (PR 3) และ API ภายใน (PR 4/6) เขียนตามสัญญานี้คู่ขนานกัน
// · ฟิลด์ ...Date เป็น DateOnly → "yyyy-MM-dd" ตามปฏิทินไทย · เวลาเป็น UTC ลงท้าย Z

/// <summary>ไฟล์สลิปที่เก็บไว้ (ไม่ใช่ Manager = ไม่อยู่ใน JSON)</summary>
public sealed record PackageSlipDto(string ContentType, long SizeBytes);

/// <summary>
/// หนึ่งรายการแจ้งชำระ · <see cref="Amount"/>/<see cref="ExpectedAmount"/>/<see cref="Note"/>/<see cref="Slip"/> = null สำหรับ role ที่ไม่ใช่ Manager
/// (ตัดที่ server) · <see cref="PeriodLastUsableDate"/> เฉพาะ approved · <see cref="RejectReason"/>/<see cref="RejectNote"/> เฉพาะ rejected
/// </summary>
public sealed record PackagePaymentDto(
    Guid Id,
    string ShortId,
    string Status,
    int MonthCount,
    decimal? Amount,
    decimal? ExpectedAmount,
    DateTime PaidAt,
    DateTime SubmittedAt,
    string SubmittedBy,
    string? Note,
    PackageSlipDto? Slip,
    DateTime? PeriodStartsAt,
    DateTime? PeriodEndsAt,
    DateOnly? PeriodLastUsableDate,
    DateTime? ReviewedAt,
    string? RejectReason,
    string? RejectReasonTh,
    string? RejectNote,
    DateTime? CancelledAt);

/// <summary>ตัวเลือกจำนวนเดือน + พรีวิว "ถ้าอนุมัติวันนี้" (server คำนวณ) · <see cref="Price"/> null = ยังไม่ตั้งราคา</summary>
public sealed record PackageMonthOptionDto(int Months, decimal? Price, DateTime PreviewEndsAt, DateOnly PreviewLastUsableDate);

public sealed record PackagePaymentAccountDto(string? BankName, string? AccountNo, string? AccountName, string? PromptPayId);

/// <summary>
/// <c>GET /api/v1/selfserve/package</c> · <see cref="CannotSubmitCode"/>: not_manager | pending_exists | not_renewable | unavailable
/// · <see cref="DaysLeft"/> ค่าเดียวกับ /auth/me (PlanAccess.DaysLeft) · <see cref="PaymentAccount"/> null = ยังไม่ตั้งบัญชีรับเงิน
/// </summary>
public sealed record PackageOverviewDto(
    string WorkspaceNo,
    string Plan,
    string Status,
    DateTime? EndsAt,
    DateOnly? LastUsableDate,
    int? DaysLeft,
    bool ReadOnly,
    IReadOnlyList<PackageMonthOptionDto> MonthOptions,
    PackagePaymentAccountDto? PaymentAccount,
    PackagePaymentDto? Pending,
    bool CanSubmit,
    string? CannotSubmitCode,
    string? CannotSubmitReasonTh,
    int PaidAtMaxPastDays);

/// <summary>
/// ฟอร์มแจ้งชำระที่ controller อ่านจาก multipart (ทุกค่าเป็นข้อความ ตรวจที่ service) · <see cref="Slip"/> null = ไม่ได้แนบ
/// </summary>
public sealed record PackagePaymentSubmission(
    string? RequestId,
    string? PaidAt,
    string? Amount,
    string? MonthCount,
    string? Note,
    PackageSlipUpload? Slip);

public sealed record PackageSlipUpload(Stream Content, string? ContentType, long SizeBytes, string? FileName);

/// <summary>ไฟล์สลิปที่เปิดอ่าน · ผู้เรียกเป็นเจ้าของ <see cref="Content"/> (ต้อง dispose)</summary>
public sealed record PackageSlipFile(Stream Content, string ContentType, string FileName);

// ---------- ทีมงาน (spadmin · API ภายในของ GaragePro Admin ระยะ B docs/17 §8.6) ----------

public sealed record PackagePaymentDuplicateDto(Guid PaymentId, string WorkspaceNo, string Status, DateTime SubmittedAt);

/// <summary>พรีวิวการต่อถ้าอนุมัติตอนนี้ · <see cref="BaseAt"/> = จุดเริ่มนับ (หัวข้อ 4)</summary>
public sealed record PackageRenewalPreviewDto(DateTime BaseAt, DateTime EndsAt, DateOnly LastUsableDate);

/// <summary>
/// หนึ่งรายการของทีมงาน (ฟิลด์ครบ ไม่ซ่อน) · <see cref="Preview"/> null = ต่ออายุไม่ได้ (เช่น ไม่ได้รอตรวจแล้ว · อู่ถูกลบ)
/// · <see cref="CurrentEndsAt"/> = วันหมดปัจจุบัน (Branch เป็นตัวจริง)
/// · <see cref="ReviewedBy"/>/<see cref="ReviewNote"/>/<see cref="CancelledBy"/> = ข้อมูลของทีมงานที่ <see cref="PackagePaymentDto"/>
///   ของลูกค้าไม่ส่ง (ReviewNote ตอนอนุมัติเป็นหมายเหตุภายใน) · API ภายในส่งให้ GaragePro Admin ครบ (docs/17 §8.6)
/// </summary>
public sealed record PackagePaymentAdminItem(
    PackagePaymentDto Payment,
    string WorkspaceNo,
    string WorkspaceName,
    int? BranchId,
    string Plan,
    string WorkspaceStatus,
    DateTime? CurrentEndsAt,
    IReadOnlyList<PackagePaymentDuplicateDto> Duplicates,
    PackageRenewalPreviewDto? Preview,
    string? ReviewedBy = null,
    string? ReviewNote = null,
    string? CancelledBy = null);

/// <summary>รายละเอียด (spadmin payment) = รายการ + ลิงก์สลิปที่เพิ่งออก (null = ไม่ได้ตั้งคีย์) + หมายเหตุทีมงาน</summary>
public sealed record PackagePaymentAdminDetail(
    PackagePaymentAdminItem Item,
    string? SlipLink,
    string? ReviewedBy,
    string? ReviewNote,
    string SlipPath);

/// <summary>
/// ผลอนุมัติ/ปฏิเสธ (docs/17 §8.6) · <see cref="EndsFrom"/>/<see cref="EndsTo"/> เฉพาะอนุมัติ
/// · อีเมล/แจ้งเตือนในระบบเป็น best effort หลัง commit: false = ส่งไม่สำเร็จ (การอนุมัติบันทึกแล้ว)
/// </summary>
public sealed record PackagePaymentReviewResult(
    PackagePaymentAdminItem Payment,
    DateTime? EndsFrom,
    DateTime? EndsTo,
    bool EmailSent,
    bool InAppSaved);
