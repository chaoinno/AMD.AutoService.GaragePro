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

/// <summary>เก็บไฟล์แนบบน local filesystem โดย DB เก็บเฉพาะ relative path และ metadata</summary>
public sealed class AttachmentStorage : IAttachmentStorage
{
    private const int BufferSize = 81920;

    private readonly AttachmentOptions _options;
    private readonly HashSet<string> _allowedContentTypes;
    private readonly string _rootWithSeparator;

    public AttachmentStorage(IOptions<AttachmentOptions> options)
    {
        _options = options.Value;
        if (string.IsNullOrWhiteSpace(_options.RootPath))
            throw new InvalidOperationException("Attachments:RootPath ต้องไม่เป็นค่าว่าง");
        if (_options.MaxSizeBytes <= 0)
            throw new InvalidOperationException("Attachments:MaxSizeBytes ต้องมากกว่า 0");

        RootPath = Path.GetFullPath(_options.RootPath);
        _rootWithSeparator = RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        _allowedContentTypes = new HashSet<string>(
            _options.AllowedContentTypes ?? [], StringComparer.OrdinalIgnoreCase);

        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public AttachmentValidation Validate(string contentType, long sizeBytes)
    {
        if (sizeBytes <= 0)
            return new(false, "ATTACHMENT_EMPTY", "ไฟล์ที่อัปโหลดไม่มีข้อมูล");

        if (sizeBytes > _options.MaxSizeBytes)
            return new(false, "ATTACHMENT_TOO_LARGE",
                $"ไฟล์มีขนาดเกิน {_options.MaxSizeBytes / 1024 / 1024} MB");

        var normalizedContentType = NormalizeContentType(contentType);
        if (!_allowedContentTypes.Contains(normalizedContentType))
            return new(false, "ATTACHMENT_TYPE_INVALID", "รองรับเฉพาะไฟล์ PNG, JPEG, WebP และ PDF");

        return new(true, null, null);
    }

    public async Task<StoredFile> SaveAsync(
        Stream content, string shardKey, int branchId, long jobId, string kind,
        string fileName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var id = Guid.NewGuid();
        var extension = ExtensionFor(fileName);
        var relativePath = Path.Combine(
            SafeSegment(shardKey), branchId.ToString(), jobId.ToString(), SafeSegment(kind), $"{id:N}{extension}");

        if (!TryGetSafeFullPath(relativePath, out var fullPath))
            throw new InvalidOperationException("ไม่สามารถสร้าง path สำหรับไฟล์แนบได้");

        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = fullPath + ".tmp";

        long sizeBytes = 0;
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        try
        {
            await using (var output = new FileStream(
                             temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    sizeBytes += read;
                    if (sizeBytes > _options.MaxSizeBytes)
                        throw new InvalidDataException("ไฟล์มีขนาดเกินค่าที่กำหนด");

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                }

                await output.FlushAsync(ct);
            }

            File.Move(temporaryPath, fullPath);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }

        var portableRelativePath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        return new StoredFile(id, portableRelativePath, sizeBytes,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    public bool TryResolve(string relativePath, out string fullPath) =>
        TryGetSafeFullPath(relativePath, out fullPath) && File.Exists(fullPath);

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (TryGetSafeFullPath(relativePath, out var fullPath) && File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private bool TryGetSafeFullPath(string relativePath, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return false;

        try
        {
            var platformPath = relativePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(RootPath, platformPath));
            if (!candidate.StartsWith(_rootWithSeparator, StringComparison.Ordinal)) return false;

            fullPath = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string NormalizeContentType(string contentType) =>
        (contentType ?? string.Empty).Split(';', 2)[0].Trim();

    private static string ExtensionFor(string fileName) =>
        Path.GetExtension(Path.GetFileName(fileName)).ToLowerInvariant() switch
        {
            ".png" => ".png",
            ".jpg" or ".jpeg" => ".jpg",
            ".webp" => ".webp",
            ".pdf" => ".pdf",
            _ => ".bin"
        };

    private static string SafeSegment(string value)
    {
        var cleaned = new string((value ?? string.Empty)
            .Where(c => char.IsLetterOrDigit(c) || c is '-' or '_')
            .ToArray());
        if (string.IsNullOrWhiteSpace(cleaned))
            throw new ArgumentException("ส่วนประกอบ path ไม่ถูกต้อง", nameof(value));
        return cleaned;
    }
}
