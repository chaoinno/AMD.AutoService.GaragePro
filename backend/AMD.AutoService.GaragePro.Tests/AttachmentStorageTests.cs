using System.Text;
using AMD.AutoService.GaragePro.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

// เชื่อมต่อ FTP จริงเพื่อทดสอบ Save/OpenRead/Delete แบบ round-trip ได้เฉพาะเมื่อตั้งค่า connection ไว้
// (เหมือน PurchasingSqlFactAttribute) — path-traversal guard ทดสอบแยกไว้ข้างล่างเพราะไม่ต้องต่อ FTP จริง
public sealed class AttachmentFtpFactAttribute : FactAttribute
{
    public AttachmentFtpFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GARAGEPRO_FTP_HOST")))
            Skip = "Set GARAGEPRO_FTP_HOST/GARAGEPRO_FTP_USERNAME/GARAGEPRO_FTP_PASSWORD to run the live FTP round-trip test.";
    }
}

public sealed class AttachmentStorageTests
{
    [Fact]
    public void Validate_rejects_empty_oversized_and_unsupported_files()
    {
        var storage = CreateStorage(new FtpOptions());
        storage.Validate("image/png", 0).Code.Should().Be("ATTACHMENT_EMPTY");
        storage.Validate("image/png", 101).Code.Should().Be("ATTACHMENT_TOO_LARGE");
        storage.Validate("application/x-msdownload", 10).Code.Should().Be("ATTACHMENT_TYPE_INVALID");
        storage.Validate("image/jpeg", 10).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task OpenReadAsync_and_DeleteAsync_reject_path_traversal_without_touching_the_network()
    {
        // Host ว่าง/ผิดจริง — ถ้า guard ทำงานถูกต้อง จะคืน null ทันทีโดยไม่พยายามต่อ FTP เลย (ไม่ทำให้ test ค้างหรือ throw)
        var storage = CreateStorage(new FtpOptions { Host = "unreachable.invalid" });

        (await storage.OpenReadAsync("../outside.txt")).Should().BeNull();
        (await storage.OpenReadAsync("/etc/passwd")).Should().BeNull();
        (await storage.OpenReadAsync("db2/105/x\\..\\..\\y")).Should().BeNull();
        (await storage.OpenReadAsync("")).Should().BeNull();

        // ไม่ throw แปลว่าไม่ได้พยายามต่อเครือข่ายจริงสำหรับ path ที่ไม่ปลอดภัย
        var act = () => storage.DeleteAsync("../outside.txt");
        await act.Should().NotThrowAsync();
    }

    [AttachmentFtpFact]
    public async Task Save_writes_file_and_can_be_read_back_and_deleted()
    {
        var host = Environment.GetEnvironmentVariable("GARAGEPRO_FTP_HOST")!;
        var storage = CreateStorage(new FtpOptions
        {
            Host = host,
            Port = int.TryParse(Environment.GetEnvironmentVariable("GARAGEPRO_FTP_PORT"), out var p) ? p : 21,
            Username = Environment.GetEnvironmentVariable("GARAGEPRO_FTP_USERNAME") ?? "",
            Password = Environment.GetEnvironmentVariable("GARAGEPRO_FTP_PASSWORD") ?? "",
            RootPath = Environment.GetEnvironmentVariable("GARAGEPRO_FTP_ROOT") ?? "/AutoServiceGaragePro/test/",
        });

        await using var content = new MemoryStream(Encoding.UTF8.GetBytes("garage-pro"));
        var saved = await storage.SaveAsync(content, "db2", 105, Guid.NewGuid(), "signature", "signature.png");

        try
        {
            saved.SizeBytes.Should().Be(10);
            saved.Sha256.Should().HaveLength(64);

            await using var readBack = await storage.OpenReadAsync(saved.RelativePath);
            readBack.Should().NotBeNull();
            using var reader = new StreamReader(readBack!);
            (await reader.ReadToEndAsync()).Should().Be("garage-pro");
        }
        finally
        {
            await storage.DeleteAsync(saved.RelativePath);
        }
    }

    private static FtpAttachmentStorage CreateStorage(FtpOptions ftp) => new(
        Options.Create(new AttachmentOptions { MaxSizeBytes = 100, AllowedContentTypes = ["image/png", "image/jpeg"] }),
        Options.Create(ftp));
}
