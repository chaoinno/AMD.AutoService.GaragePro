using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>ส่วนของ sp_Workspace ที่ทีมงาน/การแจ้งเตือนต้องรู้คู่กับรายการแจ้งชำระ</summary>
public sealed record PackagePaymentWorkspace(
    Guid Id,
    int WorkspaceNo,
    string Name,
    int? BranchId,
    WorkspacePlanKind Plan,
    WorkspaceStatus Status);

public sealed record PackagePaymentEntry(PackagePayment Payment, PackagePaymentWorkspace Workspace);

public enum PackagePaymentInsertOutcome
{
    Inserted,

    /// <summary>requestId เดิมของอู่นี้ — <see cref="PackagePaymentInsertResult.Payment"/> คือแถวเดิม</summary>
    DuplicateRequest,

    /// <summary>มีรายการรอตรวจของอู่นี้อยู่แล้ว (ชน UX_sp_PackagePayment_OnePending)</summary>
    PendingExists
}

public sealed record PackagePaymentInsertResult(PackagePaymentInsertOutcome Outcome, PackagePayment? Payment);

/// <summary>ผู้กระทำ + รายละเอียดที่ลง sp_WorkspaceAudit คู่กับการเปลี่ยนสถานะรายการ (ใน SaveChanges เดียวกัน)</summary>
public sealed record PackagePaymentAuditInfo(string Actor, string? Note, string PayloadJson);

/// <summary>
/// ที่เก็บ sp_PackagePayment (SelfServeDbContext · Infrastructure implement)
/// · ทุกการเปลี่ยนสถานะเป็น UPDATE แบบมีเงื่อนไข <c>WHERE Status = Pending</c> (atomic) + audit ใน transaction เดียว
/// · อนุมัติ (ต่อวัน) อยู่ที่ <see cref="Admin.ISelfServeAdminRepository.ApprovePaymentAsync"/> เพราะต้องเขียน Branch + sp_Workspace ด้วย
/// </summary>
public interface IPackagePaymentRepository
{
    Task<PackagePayment?> GetAsync(Guid id, CancellationToken ct = default);

    Task<PackagePaymentEntry?> GetEntryAsync(Guid id, CancellationToken ct = default);

    Task<PackagePayment?> FindByRequestAsync(Guid workspaceId, string requestId, CancellationToken ct = default);

    Task<PackagePayment?> FindPendingAsync(Guid workspaceId, CancellationToken ct = default);

    /// <summary>ใหม่สุดก่อน</summary>
    Task<IReadOnlyList<PackagePayment>> ListForWorkspaceAsync(Guid workspaceId, int take, CancellationToken ct = default);

    /// <summary>INSERT + audit "package-payment-submit" · ชน unique ตัดสินด้วยการอ่านซ้ำ (ไม่ parse ชื่อ index)</summary>
    Task<PackagePaymentInsertResult> TryInsertAsync(PackagePayment payment, PackagePaymentAuditInfo audit,
        CancellationToken ct = default);

    /// <summary>Pending → Cancelled เฉพาะแถวของอู่นี้ · false = ไม่พบ/ไม่ใช่ของอู่นี้/ไม่ได้รอตรวจแล้ว</summary>
    Task<bool> TryCancelAsync(Guid id, Guid workspaceId, DateTime now, PackagePaymentAuditInfo audit,
        CancellationToken ct = default);

    /// <summary>Pending → Rejected · false = ไม่ได้รอตรวจแล้ว</summary>
    Task<bool> TryRejectAsync(Guid id, Guid workspaceId, PackagePaymentRejectReason reason, string? reviewNote,
        DateTime now, PackagePaymentAuditInfo audit, CancellationToken ct = default);

    /// <summary>ใหม่สุดก่อน · <paramref name="status"/> null = ทุกสถานะ</summary>
    Task<IReadOnlyList<PackagePaymentEntry>> ListAsync(PackagePaymentStatus? status, int? workspaceNo, int take,
        CancellationToken ct = default);

    /// <summary>
    /// id เต็ม (มี/ไม่มีขีด) หรือขึ้นต้นด้วยเลขฐานสิบหกอย่างน้อย 8 ตัว · คืนไม่เกิน <paramref name="max"/> รายการ
    /// (ผู้เรียกปฏิเสธเมื่อได้มากกว่าหนึ่ง)
    /// </summary>
    Task<IReadOnlyList<PackagePaymentEntry>> FindByReferenceAsync(string reference, int max, CancellationToken ct = default);

    /// <summary>รายการที่สลิปมี SHA-256 ตรงกับค่าใดค่าหนึ่งใน <paramref name="sha256s"/> (ทุกอู่ ทุกสถานะ)</summary>
    Task<IReadOnlyList<PackagePaymentEntry>> FindBySlipHashesAsync(IReadOnlyCollection<string> sha256s,
        CancellationToken ct = default);
}

/// <summary>ไฟล์สลิปที่เก็บแล้ว · <see cref="OriginalSha256"/> = hash ของไฟล์ต้นฉบับที่อัปโหลด (ก่อนแปลงเป็น JPEG)</summary>
public sealed record StoredSlip(Guid FileId, string RelativePath, long SizeBytes, string ContentType, string OriginalSha256);

/// <summary>
/// ที่เก็บไฟล์สลิป (docs/17 §5.2) แยกจาก <see cref="IAttachmentStorage"/> ที่ผูกจ๊อบ · ไม่บันทึกลง svc_Attachment
/// · path <c>{shard}/{branch}/package-slip/{paymentId:N}/{fileId:N}.jpg|.pdf</c> (ผ่าน prefix guard ของ purge)
/// · รูปทุกไฟล์ถูกแปลงเป็น JPEG ≤ 1600px ไม่มี EXIF · PDF เก็บตามเดิม · ไฟล์ที่ไม่ใช่รูป/PDF จริงได้ <see cref="InvalidDataException"/>
/// </summary>
public interface IPackageSlipStorage
{
    AttachmentValidation Validate(string? contentType, long sizeBytes);

    long MaxSizeBytes { get; }

    /// <exception cref="InvalidDataException">เนื้อไฟล์ไม่ใช่ PNG/JPEG/WebP/PDF หรือรูปใหญ่เกินกำหนด</exception>
    Task<StoredSlip> SaveAsync(Stream content, string shardKey, int branchId, Guid paymentId, CancellationToken ct = default);

    /// <summary>null = ไม่พบ หรือ path ไม่ปลอดภัย</summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default);

    Task DeleteAsync(string relativePath, CancellationToken ct = default);
}

/// <summary>ข้อมูลที่ข้อความแจ้งทีมงาน/ลูกค้าต้องใช้ (snapshot ตอนเกิดเหตุ)</summary>
public sealed record PackagePaymentNoticeContext(
    PackagePayment Payment,
    Guid WorkspaceId,
    int WorkspaceNo,
    string WorkspaceName,
    string? WorkspacePhone,
    int BranchId,
    string? OwnerEmail);

public sealed record PackagePaymentSubmittedNotice(
    PackagePaymentNoticeContext Context,
    DateTime? CurrentEndsAt,
    RenewalPeriod PreviewIfApprovedNow,
    IReadOnlyList<PackagePaymentDuplicateDto> Duplicates);

public sealed record PackagePaymentReviewedNotice(
    PackagePaymentNoticeContext Context,
    DateTime? EndsTo);

/// <summary>ผลการแจ้งหลังอนุมัติ/ปฏิเสธ (best effort ทั้งคู่)</summary>
public sealed record PackageReviewNotifyOutcome(bool EmailSent, bool InAppSaved);

/// <summary>
/// แจ้ง LINE ทีมงาน / อีเมลเจ้าของ / แจ้งเตือนในระบบ (Infrastructure implement)
/// · [BIZ] ไม่เคยทำให้การแจ้งชำระ/อนุมัติล้ม: ผิดพลาดทุกอย่าง log แล้วคืนผล (โยนเฉพาะ OperationCanceledException)
/// </summary>
public interface IPackagePaymentNotifier
{
    /// <returns>true = LINE รับแล้ว (หรือไม่ได้ตั้ง LINE จึงไม่ต้องส่ง: false)</returns>
    Task<bool> SubmittedAsync(PackagePaymentSubmittedNotice notice, CancellationToken ct = default);

    Task<bool> CancelledAsync(PackagePaymentNoticeContext context, CancellationToken ct = default);

    Task<PackageReviewNotifyOutcome> ReviewedAsync(PackagePaymentReviewedNotice notice, CancellationToken ct = default);
}
