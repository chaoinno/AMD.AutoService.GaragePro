using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Dtos;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Storage;

public sealed class VehicleImageOptions
{
    public const string SectionName = "VehicleImages";
    public string RootPath { get; set; } = "storage/vehicle-images";
    public long MaxSizeBytes { get; set; } = 5 * 1024 * 1024;
}

public sealed class VehicleImageStorage : IVehicleImageStorage
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png" };
    private static readonly HashSet<string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png" };
    private readonly VehicleImageOptions _options;
    private readonly string _root;
    private readonly string _rootPrefix;

    public VehicleImageStorage(IOptions<VehicleImageOptions> options)
    {
        _options = options.Value;
        _root = Path.GetFullPath(_options.RootPath);
        _rootPrefix = _root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_root);
    }

    public VehicleImageValidation Validate(VehicleImageUpload upload)
    {
        if (upload.Data.Length == 0)
            return new(false, "VEHICLE_IMAGE_EMPTY", "ไฟล์รูปไม่มีข้อมูล");
        if (upload.Data.LongLength > _options.MaxSizeBytes)
            return new(false, "VEHICLE_IMAGE_TOO_LARGE", $"รูปต้องมีขนาดไม่เกิน {_options.MaxSizeBytes / 1024 / 1024} MB");
        var extension = Path.GetExtension(Path.GetFileName(upload.FileName));
        if (!Extensions.Contains(extension) || !ContentTypes.Contains(upload.ContentType))
            return new(false, "VEHICLE_IMAGE_TYPE_INVALID", "รองรับเฉพาะไฟล์ JPG, JPEG และ PNG");
        if (!HasValidSignature(upload.Data, extension))
            return new(false, "VEHICLE_IMAGE_CONTENT_INVALID", "เนื้อหาไฟล์ไม่ตรงกับชนิดรูปที่ระบุ");
        return new(true, null, null);
    }

    public async Task<string> SaveAsync(long vehicleId, VehicleImageUpload upload, CancellationToken ct = default)
    {
        var extension = Path.GetExtension(Path.GetFileName(upload.FileName)).ToLowerInvariant();
        var now = DateTime.Now;
        var relativePath = Path.Combine(now.ToString("yyyy"), now.ToString("MM"),
            $"{vehicleId}_{now:yyyyMMddHHmmssfff}{extension}");
        var fullPath = Resolve(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllBytesAsync(fullPath, upload.Data, ct);
        return relativePath.Replace('\\', '/');
    }

    public bool TryResolve(string relativePath, out string fullPath)
    {
        try
        {
            fullPath = Resolve(NormalizeLegacyPath(relativePath));
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
        var normalized = NormalizeLegacyPath(relativePath);
        if (!string.IsNullOrWhiteSpace(normalized)) File.Delete(Resolve(normalized));
        return Task.CompletedTask;
    }

    public string GetContentType(string relativePath) =>
        Path.GetExtension(relativePath).Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? "image/png" : "image/jpeg";

    private string Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("Vehicle image path must be relative.", nameof(relativePath));
        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Vehicle image path is outside the configured root.", nameof(relativePath));
        return fullPath;
    }

    private static string NormalizeLegacyPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("Images/", StringComparison.OrdinalIgnoreCase)
            ? normalized[7..] : normalized;
    }

    private static bool HasValidSignature(byte[] data, string extension) =>
        extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            ? data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            : data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;
}
