using System.Security.Cryptography;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace AMD.AutoService.GaragePro.Infrastructure.Storage;

/// <summary>สลิปหลังแปลงแล้ว พร้อมอัปโหลด · <see cref="OriginalSha256"/> = hash ของไฟล์ต้นฉบับ (ไว้เตือนสลิปซ้ำ)</summary>
internal sealed record NormalizedSlip(MemoryStream Content, string ContentType, string Extension, string OriginalSha256);

/// <summary>
/// ไฟล์สลิปค่าแพ็กเกจ (docs/17 §5.2) · FTP และ root เดียวกับไฟล์แนบของจ๊อบ แต่ path ไม่ผูกจ๊อบ:
/// <c>{shard}/{branch}/package-slip/{paymentId:N}/{fileId:N}.jpg|.pdf</c> (ผ่าน prefix guard ของ WorkspacePurger ได้)
/// · [SECURITY] ตรวจชนิดจากเนื้อไฟล์ ไม่เชื่อ content-type/นามสกุลจาก client · รูปทุกไฟล์ (เล็กหรือใหญ่) ถูก decode แล้ว
///   เข้ารหัสใหม่เป็น JPEG: ลบ EXIF (มีพิกัด GPS ได้) · ตัดเนื้อหาที่ซ่อนมากับไฟล์รูป · LINE แสดงได้แน่นอน
/// · ตรวจขนาดพิกเซลจาก header ก่อน decode (กันไฟล์เล็กที่ขยายเป็นรูปหลายหมื่นล้านพิกเซล)
/// </summary>
public sealed partial class FtpAttachmentStorage : IPackageSlipStorage, IPackageSlipNormalizer
{
    internal const int MaxSlipDimensionPx = 1600;
    internal const long MaxSlipPixels = 40_000_000;
    internal const long MaxSlipJpegBytes = 1_000_000;
    internal const int SlipJpegQuality = 85;
    internal const int SlipJpegFallbackQuality = 70;

    private static readonly byte[] PdfMagic = "%PDF-"u8.ToArray();

    long IPackageSlipStorage.MaxSizeBytes => _options.MaxSizeBytes;

    /// <summary>
    /// ขนาดตามเดิม (ว่าง · เกินกำหนด) · content-type ที่ client ประกาศต้องอยู่ในรายการ หรือไม่ระบุ/octet-stream
    /// (แอปบางตัวส่งแบบนั้น) เพราะตัวตัดสินจริงคือการตรวจเนื้อไฟล์ใน <see cref="NormalizeSlipAsync"/>
    /// </summary>
    AttachmentValidation IPackageSlipStorage.Validate(string? contentType, long sizeBytes)
    {
        if (sizeBytes <= 0)
            return new(false, PackageErrors.AttachmentEmptyCode, "ไฟล์สลิปไม่มีข้อมูล");
        if (sizeBytes > _options.MaxSizeBytes)
            return new(false, PackageErrors.AttachmentTooLargeCode,
                $"ไฟล์สลิปต้องมีขนาดไม่เกิน {_options.MaxSizeBytes / 1024 / 1024} MB");

        var declared = contentType?.Split(';')[0].Trim();
        if (!string.IsNullOrEmpty(declared)
            && !string.Equals(declared, "application/octet-stream", StringComparison.OrdinalIgnoreCase)
            && !_options.AllowedContentTypes.Contains(declared, StringComparer.OrdinalIgnoreCase))
            return new(false, PackageErrors.AttachmentTypeInvalidCode, "สลิปต้องเป็นรูป PNG, JPEG, WebP หรือไฟล์ PDF");

        return new(true, null, null);
    }

    long IPackageSlipNormalizer.MaxSizeBytes => _options.MaxSizeBytes;

    AttachmentValidation IPackageSlipNormalizer.Validate(string? contentType, long sizeBytes) =>
        ((IPackageSlipStorage)this).Validate(contentType, sizeBytes);

    /// <summary>
    /// แปลงแบบเดียวกับ <see cref="SaveAsync"/> แต่ไม่อัปโหลด (flow garagepro: ระบบเดิมเป็นคนเก็บไฟล์) · ไม่แตะ FTP
    /// </summary>
    async Task<PreparedSlip> IPackageSlipNormalizer.NormalizeAsync(Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var normalized = await NormalizeSlipAsync(content, _options.MaxSizeBytes, ct);
        await using var stream = normalized.Content;
        return new PreparedSlip(stream.ToArray(), normalized.ContentType, normalized.Extension);
    }

    public async Task<StoredSlip> SaveAsync(Stream content, string shardKey, int branchId, Guid paymentId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var normalized = await NormalizeSlipAsync(content, _options.MaxSizeBytes, ct);
        await using var upload = normalized.Content;

        var fileId = Guid.NewGuid();
        var relativePath = string.Join('/',
            SafeSegment(shardKey), branchId.ToString(System.Globalization.CultureInfo.InvariantCulture), "package-slip",
            paymentId.ToString("N"), $"{fileId:N}{normalized.Extension}");

        var (size, _) = await UploadAsync(upload, relativePath, ct);
        return new StoredSlip(fileId, relativePath, size, normalized.ContentType, normalized.OriginalSha256);
    }

    /// <summary>
    /// อ่านทั้งไฟล์ (≤ <paramref name="maxBytes"/>) → hash ต้นฉบับ → PDF (<c>%PDF-</c>) เก็บตามเดิม ·
    /// รูป PNG/JPEG/WebP: ≤ 40 ล้านพิกเซล · AutoOrient · ย่อด้านยาว ≤ 1600px · ลบ EXIF/IPTC/XMP · พื้นขาว (รูปโปร่งใส) ·
    /// JPEG quality 85 แล้วลดเป็น 70 ถ้าเกิน 1 MB (รูปย่อของ LINE รับไม่เกิน 1 MB) ยังเกินอีกค่อยย่อขนาดลงทีละขั้น
    /// </summary>
    /// <exception cref="InvalidDataException">ไม่ใช่รูป/PDF จริง · ไฟล์เสีย · พิกเซลเกิน · ไฟล์ใหญ่เกิน</exception>
    internal static async Task<NormalizedSlip> NormalizeSlipAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        var original = await ReadLimitedAsync(content, maxBytes, ct);
        var sha256 = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();

        if (original.AsSpan().StartsWith(PdfMagic))
            return new NormalizedSlip(new MemoryStream(original, writable: false), "application/pdf", ".pdf", sha256);

        var decoder = new DecoderOptions { MaxFrames = 1 };
        try
        {
            var format = Image.DetectFormat(decoder, original);
            if (format is not (PngFormat or JpegFormat or WebpFormat))
                throw new InvalidDataException("รองรับเฉพาะรูป PNG, JPEG, WebP และไฟล์ PDF");

            var info = Image.Identify(decoder, original);
            if ((long)info.Width * info.Height > MaxSlipPixels)
                throw new InvalidDataException("รูปสลิปมีความละเอียดเกินกำหนด");

            using var image = Image.Load<Rgba32>(decoder, original);
            image.Mutate(x => x.AutoOrient());
            if (image.Width > MaxSlipDimensionPx || image.Height > MaxSlipDimensionPx)
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(MaxSlipDimensionPx, MaxSlipDimensionPx)
                }));

            StripMetadata(image);
            image.Mutate(x => x.BackgroundColor(Color.White));

            var jpeg = await EncodeAsync(image, SlipJpegQuality, ct);
            if (jpeg.Length > MaxSlipJpegBytes)
            {
                await jpeg.DisposeAsync();
                jpeg = await EncodeAsync(image, SlipJpegFallbackQuality, ct);
            }

            // สลิปที่มีลายละเอียดสูงมากอาจยังเกิน 1 MB ที่ quality 70 · ย่อต่อจนพอ (LINE ปฏิเสธรูปย่อที่ใหญ่กว่านี้)
            foreach (var side in new[] { 1280, 1024, 800 })
            {
                if (jpeg.Length <= MaxSlipJpegBytes) break;
                if (image.Width <= side && image.Height <= side) continue;
                image.Mutate(x => x.Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(side, side) }));
                await jpeg.DisposeAsync();
                jpeg = await EncodeAsync(image, SlipJpegFallbackQuality, ct);
            }

            return new NormalizedSlip(jpeg, "image/jpeg", ".jpg", sha256);
        }
        catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException
                                       or ImageFormatException or NotSupportedException)
        {
            throw new InvalidDataException("ไฟล์สลิปไม่ใช่รูปภาพหรือ PDF ที่อ่านได้", ex);
        }
    }

    private static void StripMetadata(Image image)
    {
        image.Metadata.ExifProfile = null;
        image.Metadata.IptcProfile = null;
        image.Metadata.XmpProfile = null;
        foreach (var frame in image.Frames)
        {
            frame.Metadata.ExifProfile = null;
            frame.Metadata.IptcProfile = null;
            frame.Metadata.XmpProfile = null;
        }
    }

    private static async Task<MemoryStream> EncodeAsync(Image image, int quality, CancellationToken ct)
    {
        var output = new MemoryStream();
        await image.SaveAsJpegAsync(output, new JpegEncoder { Quality = quality }, ct);
        output.Position = 0;
        return output;
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream content, long maxBytes, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                throw new InvalidDataException("ไฟล์สลิปมีขนาดเกินกำหนด");
            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0) throw new InvalidDataException("ไฟล์สลิปไม่มีข้อมูล");
        return buffer.ToArray();
    }
}
