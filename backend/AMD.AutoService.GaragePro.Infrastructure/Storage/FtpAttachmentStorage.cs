using System.Security.Cryptography;
using AMD.AutoService.GaragePro.Application.Abstractions;
using FluentFTP;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.Storage;

public sealed class AttachmentOptions
{
    public const string SectionName = "Attachments";
    public long MaxSizeBytes { get; set; } = 15 * 1024 * 1024;
    public string[] AllowedContentTypes { get; set; } =
        ["image/png", "image/jpeg", "image/webp", "application/pdf"];
}

public sealed class FtpOptions
{
    public const string SectionName = "Ftp";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 21;

    /// <summary>ตั้งผ่าน dotnet user-secrets เท่านั้น — ห้าม commit (เหมือน Jwt:Key/ConnectionStrings)</summary>
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";

    /// <summary>โฟลเดอร์เฉพาะของโปรเจกต์นี้บน FTP เครื่องเดียวกับ AMD.GaragePro.Admin (10.10.3.11) — แยก root กันชนกัน</summary>
    public string RootPath { get; set; } = "/AutoServiceGaragePro/attachments/";
}

/// <summary>
/// เก็บไฟล์แนบบน FTP แทนดิสก์ในเครื่อง — ยังคง validate/limit ขนาด-ชนิดไฟล์ และกัน path traversal
/// เหมือนของเดิม (AttachmentStorage บนดิสก์) แค่เปลี่ยน backend เท่านั้น
/// ห้ามเลียนแบบ FileManagerController ของ AMD.GaragePro.Admin ตรงๆ — ตัวนั้นต่อ path จาก query string
/// ตรงๆ ไม่มีการกัน path traversal เลย (ดู CLAUDE.md เรื่องไม่ทำตาม pattern ของโปรเจกต์นั้น)
/// </summary>
public sealed class FtpAttachmentStorage(IOptions<AttachmentOptions> options, IOptions<FtpOptions> ftpOptions) : IAttachmentStorage
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".pdf"
    };

    private readonly AttachmentOptions _options = options.Value;
    private readonly FtpOptions _ftp = ftpOptions.Value;
    private readonly string _rootPrefix = "/" + ftpOptions.Value.RootPath.Trim('/') + "/";

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
        Stream content, string shardKey, int branchId, Guid jobId, string kind,
        string fileName, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        var extension = Path.GetExtension(Path.GetFileName(fileName));
        if (!AllowedExtensions.Contains(extension))
            extension = string.Empty;

        var relativePath = string.Join('/',
            SafeSegment(shardKey), branchId.ToString(), jobId.ToString("N"), SafeSegment(kind),
            $"{id:N}{extension.ToLowerInvariant()}");

        // ตัด/แฮชระหว่างอัปโหลดสตรีมตรงเข้า FTP เลย ไม่ต้องพักไฟล์ไว้ที่ดิสก์ในเครื่องก่อน
        var limited = new HashingLimitedStream(content, _options.MaxSizeBytes);

        await using var client = CreateClient();
        await client.Connect(ct);
        await client.UploadStream(limited, _rootPrefix + relativePath,
            FtpRemoteExists.Overwrite, createRemoteDir: true, token: ct);

        return new StoredFile(id, relativePath, limited.TotalBytesRead, limited.GetSha256Hex());
    }

    public async Task<Stream?> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        if (!IsSafeRelativePath(relativePath)) return null;

        await using var client = CreateClient();
        await client.Connect(ct);
        var remotePath = _rootPrefix + relativePath;

        if (!await client.FileExists(remotePath, ct)) return null;

        var buffer = new MemoryStream();
        var ok = await client.DownloadStream(buffer, remotePath, token: ct);
        if (!ok) return null;

        buffer.Position = 0;
        return buffer;
    }

    public async Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        if (!IsSafeRelativePath(relativePath)) return;

        await using var client = CreateClient();
        await client.Connect(ct);
        await client.DeleteFile(_rootPrefix + relativePath, ct);
    }

    private AsyncFtpClient CreateClient() => new(_ftp.Host, _ftp.Username, _ftp.Password, _ftp.Port);

    /// <summary>กัน path traversal — ต้องเป็น relative path ล้วน ไม่มี .. หรือ \ (เทียบเจตนาเดียวกับ ResolveInsideRoot เดิมบนดิสก์)</summary>
    private static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.StartsWith('/') || relativePath.Contains('\\'))
            return false;

        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && segments.All(s => s != "." && s != "..");
    }

    private static string SafeSegment(string value)
    {
        var safe = new string(value.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').ToArray());
        if (string.IsNullOrEmpty(safe))
            throw new ArgumentException("Storage path segment is invalid.", nameof(value));

        return safe;
    }

    /// <summary>ห่อ stream ต้นทางเพื่อนับขนาด/แฮชระหว่างที่ FluentFTP อ่านไปอัปโหลด โดยไม่ต้องอ่านซ้ำสองรอบ</summary>
    private sealed class HashingLimitedStream(Stream inner, long maxSizeBytes) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private string? _sha256Hex;

        public long TotalBytesRead { get; private set; }

        /// <summary>เรียกได้หลังอ่านจน EOF แล้วเท่านั้น (คือหลัง UploadStream อัปโหลดสำเร็จ)</summary>
        public string GetSha256Hex() => _sha256Hex
            ?? throw new InvalidOperationException("Stream ยังอ่านไม่จบ — เรียก GetSha256Hex ก่อนอัปโหลดเสร็จ");

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                TotalBytesRead += read;
                if (TotalBytesRead > maxSizeBytes)
                    throw new InvalidDataException("Attachment exceeds the configured size limit.");

                _hash.AppendData(buffer.Span[..read]);
            }
            else
            {
                _sha256Hex ??= Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
            }

            return read;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
