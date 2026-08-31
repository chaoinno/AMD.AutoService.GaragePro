using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace AMD.AutoService.GaragePro.Infrastructure.Storage;

public sealed class StaffImageOptions
{
    public const string SectionName = "StaffImages";
    public string RootPath { get; set; } = "storage/staff-images";
    public long MaxSizeBytes { get; set; } = 5 * 1024 * 1024;
}

public sealed class StaffImageStorage : IStaffImageStorage
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png" };
    private static readonly HashSet<string> ContentTypes = new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png" };
    private readonly StaffImageOptions _options;
    private readonly string _root;
    private readonly string _rootPrefix;

    public StaffImageStorage(IOptions<StaffImageOptions> options)
    {
        _options = options.Value;
        _root = Path.GetFullPath(_options.RootPath);
        _rootPrefix = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public StaffImageValidation Validate(StaffImageUpload upload)
    {
        if (upload.Data.Length == 0) return new(false, "STAFF_IMAGE_EMPTY", "ไฟล์รูปไม่มีข้อมูล");
        if (upload.Data.LongLength > _options.MaxSizeBytes)
            return new(false, "STAFF_IMAGE_TOO_LARGE", $"รูปต้องมีขนาดไม่เกิน {_options.MaxSizeBytes / 1024 / 1024} MB");
        var extension = Path.GetExtension(Path.GetFileName(upload.FileName));
        if (!Extensions.Contains(extension) || !ContentTypes.Contains(upload.ContentType))
            return new(false, "STAFF_IMAGE_TYPE_INVALID", "รองรับเฉพาะไฟล์ JPG, JPEG และ PNG");
        try { using var image = Image.Load(upload.Data); }
        catch (UnknownImageFormatException) { return new(false, "STAFF_IMAGE_CONTENT_INVALID", "เนื้อหาไฟล์ไม่ใช่รูปภาพที่รองรับ"); }
        catch (InvalidImageContentException) { return new(false, "STAFF_IMAGE_CONTENT_INVALID", "ไฟล์รูปเสียหรืออ่านไม่ได้"); }
        return new(true, null, null);
    }

    public async Task<string> SaveAsync(long staffId, StaffImageUpload upload, CancellationToken ct = default)
    {
        using var image = Image.Load(upload.Data);
        image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions
        {
            Size = new Size(300, 300), Mode = ResizeMode.Crop, Position = AnchorPositionMode.Center
        }));
        var now = DateTime.Now;
        var relative = Path.Combine(now.ToString("yyyy"), now.ToString("MM"), $"{staffId}_{now:yyyyMMddHHmmssfff}.jpg");
        var fullPath = Resolve(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await image.SaveAsJpegAsync(fullPath, new JpegEncoder { Quality = 88 }, ct);
        return relative.Replace('\\', '/');
    }

    public bool TryResolve(string relativePath, out string fullPath)
    {
        try { fullPath = Resolve(relativePath); return File.Exists(fullPath); }
        catch (ArgumentException) { fullPath = string.Empty; return false; }
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(relativePath) && !relativePath.StartsWith("Images/", StringComparison.OrdinalIgnoreCase))
            File.Delete(Resolve(relativePath));
        return Task.CompletedTask;
    }

    public string GetContentType(string relativePath) => "image/jpeg";

    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("Staff image path must be relative.", nameof(relativePath));
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Staff image path is outside the configured root.", nameof(relativePath));
        return fullPath;
    }
}
