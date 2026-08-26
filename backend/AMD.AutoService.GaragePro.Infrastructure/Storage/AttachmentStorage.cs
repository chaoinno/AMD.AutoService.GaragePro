using System.Security.Cryptography;
using AMD.AutoService.GaragePro.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Storage;

public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";

    public string RootPath { get; set; } = "storage/attachments";
    public long MaxSizeBytes { get; set; } = 15 * 1024 * 1024;
    public string[] AllowedContentTypes { get; set; } =
        ["image/png", "image/jpeg", "image/webp", "application/pdf"];
}

public sealed class AttachmentStorage : IAttachmentStorage
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".pdf"
    };

    private readonly AttachmentOptions _options;
    private readonly string _rootPrefix;

    public AttachmentStorage(IOptions<AttachmentOptions> options)
    {
        _options = options.Value;
        RootPath = Path.GetFullPath(_options.RootPath);
        _rootPrefix = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public AttachmentValidation Validate(string contentType, long sizeBytes)
    {
        if (sizeBytes <= 0)
            return new(false, "ATTACHMENT_EMPTY", "ไฟล์แนบไม่มีข้อมูล");

        if (sizeBytes > _options.MaxSizeBytes)
            return new(false, "ATTACHMENT_TOO_LARGE",
                $"ไฟล์แนบต้องมีขนาดไม่เกิน {_options.MaxSizeBytes / 1024 / 1024} MB");

        if (!_options.AllowedContentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            return new(false, "ATTACHMENT_TYPE_INVALID", "รองรับเฉพาะไฟล์ PNG, JPEG, WebP และ PDF");

        return new(true, null, null);
    }

    public async Task<StoredFile> SaveAsync(
        Stream content, string shardKey, int branchId, long jobId, string kind,
        string fileName, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        var extension = Path.GetExtension(Path.GetFileName(fileName));
        if (!AllowedExtensions.Contains(extension))
            extension = string.Empty;

        var relativePath = Path.Combine(
            SafeSegment(shardKey),
            branchId.ToString(),
            jobId.ToString(),
            SafeSegment(kind),
            $"{id:N}{extension.ToLowerInvariant()}");
        var fullPath = ResolveInsideRoot(relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        try
        {
            await using var destination = new FileStream(
                fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920];
            long sizeBytes = 0;

            while (true)
            {
                var read = await content.ReadAsync(buffer.AsMemory(), ct);
                if (read == 0)
                    break;

                sizeBytes += read;
                if (sizeBytes > _options.MaxSizeBytes)
                    throw new InvalidDataException("Attachment exceeds the configured size limit.");

                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), ct);
            }

            return new StoredFile(
                id,
                relativePath.Replace('\\', '/'),
                sizeBytes,
                Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
        }
        catch
        {
            File.Delete(fullPath);
            throw;
        }
    }

    public bool TryResolve(string relativePath, out string fullPath)
    {
        try
        {
            fullPath = ResolveInsideRoot(relativePath);
            return File.Exists(fullPath);
        }
        catch (ArgumentException)
        {
            fullPath = string.Empty;
            return false;
        }
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        File.Delete(ResolveInsideRoot(relativePath));
        return Task.CompletedTask;
    }

    private string ResolveInsideRoot(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("Attachment path must be relative.", nameof(relativePath));

        var fullPath = Path.GetFullPath(Path.Combine(RootPath, relativePath));
        if (!fullPath.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Attachment path is outside the storage root.", nameof(relativePath));

        return fullPath;
    }

    private static string SafeSegment(string value)
    {
        var safe = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrEmpty(safe))
            throw new ArgumentException("Storage path segment is invalid.", nameof(value));

        return safe;
    }
}
