using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Attachments;

public sealed record AttachmentDto(
    Guid Id,
    string Kind,
    Guid? EntityId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string RelativePath,
    string Url,
    string UploadedByName,
    DateTime UploadedAt);

public sealed record UploadAttachmentRequest(
    Guid JobId,
    string Kind,
    Guid? EntityId,
    string FileName,
    string ContentType,
    Stream Content,
    long SizeBytes);

public interface IAttachmentService
{
    Task<Result<AttachmentDto>> UploadAsync(UploadAttachmentRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<AttachmentDto>>> GetForJobAsync(Guid jobId, string? kind, CancellationToken ct = default);
    Task<Result<AttachmentFile>> OpenAsync(string relativePath, CancellationToken ct = default);
}

public sealed record AttachmentFile(Attachment Meta, string FullPath);

public sealed class AttachmentService(
    IAttachmentStorage storage,
    IAttachmentRepository repository,
    IJobRepository jobs,
    ICurrentUser currentUser,
    TimeProvider clock) : IAttachmentService
{
    /// <summary>ชนิดไฟล์ที่ระบบรู้จัก — กันการสร้างโฟลเดอร์มั่วจาก client</summary>
    private static readonly string[] AllowedKinds =
        ["signature", "intake", "inspection", "repair-before", "repair-after", "qc", "document"];

    public async Task<Result<AttachmentDto>> UploadAsync(
        UploadAttachmentRequest request, CancellationToken ct = default)
    {
        var kind = (request.Kind ?? string.Empty).Trim().ToLowerInvariant();

        if (!AllowedKinds.Contains(kind))
            return Result<AttachmentDto>.Fail("ATTACHMENT_KIND_INVALID",
                $"ชนิดไฟล์แนบ “{request.Kind}” ไม่ถูกต้อง", nameof(request.Kind));

        var validation = storage.Validate(request.ContentType, request.SizeBytes);
        if (!validation.IsValid)
            return Result<AttachmentDto>.Fail(validation.Code!, validation.MessageTh!);

        // งานต้องอยู่สาขาเดียวกับที่เข้าใช้งานอยู่ — ไม่งั้นไฟล์จะถูกเก็บใต้สาขาผิด
        // แล้วเจ้าของงานตัวจริงจะเปิดไฟล์ไม่ได้ (OpenAsync เช็ค branch)
        var job = await jobs.GetAsync(request.JobId, ct);
        if (job is null)
            return Result<AttachmentDto>.Fail("JOB_NOT_FOUND",
                $"ไม่พบงานเลขที่ {request.JobId}", nameof(request.JobId));

        if (job.BranchId != currentUser.BranchId || job.LegacyShardKey != currentUser.ShardKey)
            return Result<AttachmentDto>.Fail("JOB_OTHER_BRANCH",
                "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่", nameof(request.JobId));

        var stored = await storage.SaveAsync(
            request.Content, currentUser.ShardKey, currentUser.BranchId,
            request.JobId, kind, request.FileName, ct);

        var attachment = new Attachment
        {
            Id = stored.Id,
            JobId = request.JobId,
            Kind = kind,
            EntityId = request.EntityId,
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = stored.SizeBytes,
            RelativePath = stored.RelativePath,
            Sha256 = stored.Sha256,
            UploadedByUserId = currentUser.UserId,
            UploadedByName = currentUser.UserName,
            UploadedAt = clock.GetUtcNow().UtcDateTime
        };

        await repository.AddAsync(attachment, ct);
        await repository.SaveChangesAsync(ct);

        return Result<AttachmentDto>.Ok(ToDto(attachment));
    }

    public async Task<Result<IReadOnlyList<AttachmentDto>>> GetForJobAsync(
        Guid jobId, string? kind, CancellationToken ct = default)
    {
        var items = await repository.GetForJobAsync(jobId, kind, ct);

        return Result<IReadOnlyList<AttachmentDto>>.Ok(items.Select(ToDto).ToList());
    }

    public async Task<Result<AttachmentFile>> OpenAsync(
        string relativePath, CancellationToken ct = default)
    {
        var meta = await repository.GetByPathAsync(relativePath, ct);
        if (meta is null)
            return Result<AttachmentFile>.Fail("ATTACHMENT_NOT_FOUND", "ไม่พบไฟล์แนบที่ระบุ");

        // ไฟล์แนบเป็นข้อมูลของสาขา — คนละสาขาห้ามเปิด
        if (meta.Job is null || meta.Job.LegacyShardKey != currentUser.ShardKey || meta.Job.BranchId != currentUser.BranchId)
            return Result<AttachmentFile>.Fail("ATTACHMENT_OTHER_SCOPE",
                "ไฟล์นี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        if (!storage.TryResolve(meta.RelativePath, out var fullPath))
            return Result<AttachmentFile>.Fail("ATTACHMENT_FILE_MISSING",
                "ไม่พบไฟล์บนที่เก็บข้อมูล — อาจถูกลบหรือย้ายไปแล้ว");

        return Result<AttachmentFile>.Ok(new AttachmentFile(meta, fullPath));
    }

    private static AttachmentDto ToDto(Attachment a) => new(
        a.Id, a.Kind, a.EntityId, a.FileName, a.ContentType, a.SizeBytes, a.RelativePath,
        Url: $"/api/v1/attachments/file?path={Uri.EscapeDataString(a.RelativePath)}",
        UploadedByName: a.UploadedByName,
        UploadedAt: a.UploadedAt);
}
