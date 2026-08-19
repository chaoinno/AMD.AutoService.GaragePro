using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public sealed record StoredFile(Guid Id, string RelativePath, long SizeBytes, string Sha256);

public sealed record AttachmentValidation(bool IsValid, string? Code, string? MessageTh);

public interface IAttachmentStorage
{
    string RootPath { get; }

    AttachmentValidation Validate(string contentType, long sizeBytes);

    Task<StoredFile> SaveAsync(
        Stream content, string shardKey, int branchId, long jobId, string kind,
        string fileName, CancellationToken ct = default);

    /// <summary>แปลง relative path เป็น path จริง — คืน false เมื่อชี้ออกนอก root หรือไม่มีไฟล์</summary>
    bool TryResolve(string relativePath, out string fullPath);

    Task DeleteAsync(string relativePath, CancellationToken ct = default);
}

public interface IAttachmentRepository
{
    Task AddAsync(Attachment attachment, CancellationToken ct = default);
    Task<Attachment?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Attachment?> GetByPathAsync(string relativePath, CancellationToken ct = default);
    Task<IReadOnlyList<Attachment>> GetForJobAsync(
        string shardKey, int branchId, long jobId, string? kind, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
