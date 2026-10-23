using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// การแปลงไฟล์สลิปก่อนเก็บ (docs/17 §5.2) โดยไม่ต้องต่อ FTP: ตรวจชนิดจากเนื้อไฟล์ · PDF ผ่านตามเดิม · รูปทุกไฟล์เป็น JPEG
/// ≤ 1600px ไม่มี EXIF พื้นขาว ≤ 1 MB · hash เป็นของไฟล์ต้นฉบับ
/// </summary>
public class PackageSlipStorageTests
{
    private const long Max = 15 * 1024 * 1024;

    private static Task<NormalizedSlip> Normalize(byte[] bytes, long max = Max) =>
        FtpAttachmentStorage.NormalizeSlipAsync(new MemoryStream(bytes), max, CancellationToken.None);

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] Encode<TPixel>(Image<TPixel> image, Action<Image<TPixel>, Stream> save)
        where TPixel : unmanaged, IPixel<TPixel>
    {
        using var stream = new MemoryStream();
        save(image, stream);
        return stream.ToArray();
    }

    [Fact]
    public async Task Pdf_is_stored_byte_for_byte()
    {
        var pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << >> endobj\n%%EOF");

        var slip = await Normalize(pdf);

        slip.ContentType.Should().Be("application/pdf");
        slip.Extension.Should().Be(".pdf");
        slip.Content.ToArray().Should().Equal(pdf);
        slip.OriginalSha256.Should().Be(Sha(pdf));
    }

    [Fact]
    public async Task Small_png_is_still_re_encoded_as_jpeg_and_hashed_from_the_original()
    {
        using var image = new Image<Rgba32>(200, 150, new Rgba32(30, 60, 90));
        var png = Encode(image, (i, s) => i.SaveAsPng(s));

        var slip = await Normalize(png);

        slip.ContentType.Should().Be("image/jpeg");
        slip.Extension.Should().Be(".jpg");
        Image.DetectFormat(slip.Content.ToArray()).Should().BeOfType<JpegFormat>();
        slip.OriginalSha256.Should().Be(Sha(png), "เตือนสลิปซ้ำจากไฟล์ที่ลูกค้าอัปโหลด ไม่ใช่ไฟล์ที่แปลงแล้ว");
        using var result = Image.Load(slip.Content.ToArray());
        result.Width.Should().Be(200);
    }

    [Fact]
    public async Task Large_images_shrink_to_1600px_on_the_long_side()
    {
        using var image = new Image<Rgba32>(4000, 2000, new Rgba32(200, 200, 200));
        var slip = await Normalize(Encode(image, (i, s) => i.SaveAsPng(s)));

        using var result = Image.Load(slip.Content.ToArray());
        result.Width.Should().Be(1600);
        result.Height.Should().Be(800);
    }

    [Fact]
    public async Task Transparent_pixels_become_white()
    {
        using var image = new Image<Rgba32>(50, 50, new Rgba32(0, 0, 0, 0));
        var slip = await Normalize(Encode(image, (i, s) => i.SaveAsPng(s)));

        using var result = Image.Load<Rgba32>(slip.Content.ToArray());
        var pixel = result[10, 10];
        pixel.R.Should().BeGreaterThan(245);
        pixel.G.Should().BeGreaterThan(245);
        pixel.B.Should().BeGreaterThan(245);
    }

    [Fact]
    public async Task Exif_is_applied_then_stripped()
    {
        using var image = new Image<Rgba32>(200, 100, new Rgba32(10, 120, 10));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Orientation, (ushort)6);   // หมุน 90° ตามกล้อง
        image.Metadata.ExifProfile.SetValue(ExifTag.Make, "PhoneMaker");
        var jpeg = Encode(image, (i, s) => i.SaveAsJpeg(s));

        var slip = await Normalize(jpeg);

        using var result = Image.Load(slip.Content.ToArray());
        result.Width.Should().Be(100, "AutoOrient ก่อนลบ EXIF");
        result.Height.Should().Be(200);
        result.Metadata.ExifProfile.Should().BeNull("พิกัด/รุ่นเครื่องห้ามติดไปกับสลิปที่ส่งเข้า LINE");
    }

    [Fact]
    public async Task Webp_is_accepted()
    {
        using var image = new Image<Rgba32>(64, 64, new Rgba32(1, 2, 3));
        var slip = await Normalize(Encode(image, (i, s) => i.SaveAsWebp(s)));
        slip.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task Gif_and_non_image_content_are_rejected_even_with_an_image_name()
    {
        using var gif = new Image<Rgba32>(10, 10);
        var gifBytes = Encode(gif, (i, s) => i.SaveAsGif(s));

        await FluentActions.Invoking(() => Normalize(gifBytes)).Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Invoking(() => Normalize(Encoding.UTF8.GetBytes("<html>not a slip</html>")))
            .Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Invoking(() => Normalize([0xFF, 0xD8, 0xFF, 0x00, 0x01]))
            .Should().ThrowAsync<InvalidDataException>("JPEG ที่เสีย");
    }

    [Fact]
    public async Task Images_over_forty_megapixels_are_rejected_before_decoding()
    {
        using var huge = new Image<L8>(7000, 6000);   // 42 ล้านพิกเซล (PNG สีเดียวไฟล์เล็กมาก)
        var png = Encode(huge, (i, s) => i.SaveAsPng(s));

        await FluentActions.Invoking(() => Normalize(png)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Empty_and_oversized_streams_are_rejected()
    {
        await FluentActions.Invoking(() => Normalize([])).Should().ThrowAsync<InvalidDataException>();
        await FluentActions.Invoking(() => Normalize(new byte[100], max: 50)).Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task Noisy_photos_end_up_under_one_megabyte_for_line()
    {
        var random = new Random(7);
        using var image = new Image<Rgba32>(2400, 2400);
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                    row[x] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256));
            }
        });
        var jpeg = Encode(image, (i, s) => i.SaveAsJpeg(s, new JpegEncoder { Quality = 95 }));

        var slip = await Normalize(jpeg);

        slip.Content.Length.Should().BeLessThanOrEqualTo(FtpAttachmentStorage.MaxSlipJpegBytes);
    }

    [Theory]
    [InlineData("image/png", 10, true, null)]
    [InlineData("application/pdf", 10, true, null)]
    [InlineData("application/octet-stream", 10, true, null)]
    [InlineData(null, 10, true, null)]
    [InlineData("image/jpeg; charset=binary", 10, true, null)]
    [InlineData("image/gif", 10, false, PackageErrors.AttachmentTypeInvalidCode)]
    [InlineData("image/png", 0, false, PackageErrors.AttachmentEmptyCode)]
    [InlineData("image/png", Max + 1, false, PackageErrors.AttachmentTooLargeCode)]
    public void Validate_checks_size_and_declared_type(string? contentType, long size, bool valid, string? code)
    {
        IPackageSlipStorage storage = new FtpAttachmentStorage(Options.Create(new AttachmentOptions()),
            Options.Create(new FtpOptions()));

        var result = storage.Validate(contentType, size);

        result.IsValid.Should().Be(valid);
        result.Code.Should().Be(code);
        storage.MaxSizeBytes.Should().Be(Max);
    }
}
