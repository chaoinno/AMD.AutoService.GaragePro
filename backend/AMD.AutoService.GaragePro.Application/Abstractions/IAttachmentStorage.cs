using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public sealed record StoredFile(Guid Id, string RelativePath, long SizeBytes, string Sha256);

public sealed record AttachmentValidation(bool IsValid, string? Code, string? MessageTh);

public interface IAttachmentStorage
{
    AttachmentValidation Validate(string contentType, long sizeBytes);

    Task<StoredFile> SaveAsync(
        Stream content, string shardKey, int branchId, Guid jobId, string kind,
        string fileName, CancellationToken ct = default);

    /// <summary>ดาวน์โหลดไฟล์กลับมาเป็น stream — คืน null เมื่อไม่พบไฟล์ หรือ path ไม่ปลอดภัย (path traversal)</summary>
    Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default);

    Task DeleteAsync(string relativePath, CancellationToken ct = default);
}

public interface IAttachmentRepository
{
    Task AddAsync(Attachment attachment, CancellationToken ct = default);
    Task<Attachment?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Attachment?> GetByPathAsync(string relativePath, CancellationToken ct = default);
    Task<IReadOnlyList<Attachment>> GetForJobAsync(
        Guid jobId, string? kind, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
