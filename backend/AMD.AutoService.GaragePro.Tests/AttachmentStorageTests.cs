using System.Text;
using AMD.AutoService.GaragePro.Infrastructure.Storage;
using FluentAssertions;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class AttachmentStorageTests
{
    [Fact]
    public async Task Save_writes_file_with_hash_and_resolves_only_paths_inside_root()
    {
        var root = Path.Combine(Path.GetTempPath(), $"garagepro-storage-{Guid.NewGuid():N}");

        try
        {
            var storage = CreateStorage(root);
            await using var content = new MemoryStream(Encoding.UTF8.GetBytes("garage-pro"));

            var saved = await storage.SaveAsync(
                content, "db2", 105, Guid.NewGuid(), "signature", "signature.png");

            saved.SizeBytes.Should().Be(10);
            saved.Sha256.Should().HaveLength(64);
            storage.TryResolve(saved.RelativePath, out var fullPath).Should().BeTrue();
            File.Exists(fullPath).Should().BeTrue();
            storage.TryResolve("../outside.txt", out _).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Validate_rejects_empty_oversized_and_unsupported_files()
    {
        var root = Path.Combine(Path.GetTempPath(), $"garagepro-storage-{Guid.NewGuid():N}");

        try
        {
            var storage = CreateStorage(root);
            storage.Validate("image/png", 0).Code.Should().Be("ATTACHMENT_EMPTY");
            storage.Validate("image/png", 101).Code.Should().Be("ATTACHMENT_TOO_LARGE");
            storage.Validate("application/x-msdownload", 10).Code.Should().Be("ATTACHMENT_TYPE_INVALID");
            storage.Validate("image/jpeg", 10).IsValid.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static AttachmentStorage CreateStorage(string root) => new(Options.Create(new AttachmentOptions
    {
        RootPath = root,
        MaxSizeBytes = 100,
        AllowedContentTypes = ["image/png", "image/jpeg"]
    }));
}
