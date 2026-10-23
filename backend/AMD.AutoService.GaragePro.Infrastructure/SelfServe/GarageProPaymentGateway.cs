using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// ส่งการแจ้งชำระของสาขา GaragePro ให้ API เดิม (docs/17 §8.7.1) · <c>POST {BaseUrl}/api/Branch/AddPayment</c> แบบ multipart
/// · ระบบเดิมคำนวณวันหมดใหม่ เก็บสลิป และส่งการ์ดยืนยันเข้า LINE เอง · ServicePro ไม่เขียน Garage
/// · คำตอบ <c>{ Success, Message, Detail: { Id } }</c> (HTTP 200 แม้ไม่สำเร็จ) · HTTP อื่น/redirect/อ่านไม่ออก = ไม่ทราบผล
/// · HttpClient ตัวเดียวทั้งแอป (ไม่ตาม redirect · timeout 30 วิ) แบบ <see cref="Email.CloudflareEmailSender"/> · เทสต์ส่ง HttpClient เองได้
/// · [SECURITY] ไม่ log เนื้อหาคำตอบ/ไฟล์ · BaseUrl มาจากค่าตั้งเท่านั้น (ไม่มีค่าเริ่มต้นในโค้ด)
/// </summary>
public sealed class GarageProPaymentGateway : IGarageProPaymentGateway
{
    public const string AddPaymentPath = "api/Branch/AddPayment";
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    /// <summary>คำตอบที่ใหญ่กว่านี้ไม่ใช่คำตอบของ endpoint นี้ (กันอ่านหน้า error ขนาดใหญ่เข้าหน่วยความจำ)</summary>
    internal const int MaxResponseBytes = 64 * 1024;

    private static readonly HttpClient SharedHttp = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    })
    {
        Timeout = RequestTimeout,
    };

    private readonly HttpClient _http;
    private readonly SelfServeOptions.GarageProApiOptions _options;
    private readonly ILogger<GarageProPaymentGateway> _logger;

    public GarageProPaymentGateway(IOptions<SelfServeOptions> options, ILogger<GarageProPaymentGateway> logger)
        : this(options, logger, SharedHttp)
    {
    }

    internal GarageProPaymentGateway(IOptions<SelfServeOptions> options, ILogger<GarageProPaymentGateway> logger, HttpClient http)
    {
        _options = options.Value.Package.GarageProApi;
        _logger = logger;
        _http = http;
    }

    public bool IsConfigured => _options.EffectiveBaseUrl is not null;

    public async Task<GarageProAddPaymentResult> AddPaymentAsync(GarageProAddPaymentRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_options.EffectiveBaseUrl is not { } baseUrl)
            return new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Unavailable);

        var url = new Uri(new Uri(baseUrl.AbsoluteUri.TrimEnd('/') + "/"), AddPaymentPath);
        using var form = BuildForm(request);

        try
        {
            using var response = await _http.PostAsync(url, form, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("แจ้งชำระของสาขา {BranchId} ไปยัง GarageProApi ไม่สำเร็จ: HTTP {Status}",
                    request.BranchId, (int)response.StatusCode);
                return new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Unavailable);
            }

            var body = await ReadLimitedAsync(response.Content, ct);
            var parsed = body is null ? null : Parse(body);
            if (parsed is null)
                _logger.LogWarning("แจ้งชำระของสาขา {BranchId}: คำตอบของ GarageProApi อ่านไม่ออก", request.BranchId);
            else if (parsed.Outcome == GarageProAddPaymentOutcome.Rejected)
                _logger.LogInformation("GarageProApi ไม่รับการแจ้งชำระของสาขา {BranchId}", request.BranchId);
            return parsed ?? new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Unavailable);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "เชื่อมต่อ GarageProApi ไม่ได้ (สาขา {BranchId})", request.BranchId);
            return new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Unavailable);
        }
    }

    /// <summary>
    /// ฟิลด์ตามสัญญาของ API เดิม · ชื่อฟิลด์/ชื่อไฟล์ใส่เครื่องหมายคำพูดเองและไม่ส่ง <c>filename*</c>
    /// (ตัวอ่าน multipart รุ่นเก่าบางตัวอ่านแบบไม่มีคำพูด/แบบ RFC 5987 ไม่ได้)
    /// </summary>
    internal static MultipartFormDataContent BuildForm(GarageProAddPaymentRequest request)
    {
        var form = new MultipartFormDataContent();
        Add(form, "Zone", request.Zone.ToString(CultureInfo.InvariantCulture));
        Add(form, "BranchId", request.BranchId.ToString(CultureInfo.InvariantCulture));
        Add(form, "PayDate", request.PaidAtThai.ToString("yyyy-MM-dd'T'00:00:00", CultureInfo.InvariantCulture));
        Add(form, "PayTime", request.PaidAtThai.ToString("HH:mm:ss", CultureInfo.InvariantCulture));
        Add(form, "MonthCount", request.MonthCount.ToString(CultureInfo.InvariantCulture));
        Add(form, "CreatedBy", request.CreatedByUserId.ToString(CultureInfo.InvariantCulture));

        var file = new ByteArrayContent(request.Slip);
        file.Headers.ContentType = new MediaTypeHeaderValue(request.SlipContentType);
        file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
        {
            Name = Quote("PaySlip"),
            FileName = Quote(SafeFileName(request.SlipFileName))
        };
        form.Add(file);
        return form;

        static void Add(MultipartFormDataContent form, string name, string value)
        {
            var content = new StringContent(value);
            content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = Quote(name) };
            form.Add(content);
        }
    }

    /// <summary>
    /// <c>Success</c> true = รับแล้ว · false = ไม่รับ (พร้อม <c>Message</c>) · ไม่มี <c>Success</c>/ไม่ใช่ JSON = null (ไม่ทราบผล)
    /// · ชื่อฟิลด์ไม่สนตัวพิมพ์ · <c>Detail.Id</c> เป็นตัวเลขหรือข้อความก็ได้
    /// </summary>
    internal static GarageProAddPaymentResult? Parse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            if (Property(doc.RootElement, "Success") is not { ValueKind: JsonValueKind.True or JsonValueKind.False } success)
                return null;

            var message = Property(doc.RootElement, "Message") is { ValueKind: JsonValueKind.String } m ? m.GetString() : null;
            long? id = null;
            if (Property(doc.RootElement, "Detail") is { ValueKind: JsonValueKind.Object } detail
                && Property(detail, "Id") is { } raw)
            {
                if (raw.ValueKind == JsonValueKind.Number && raw.TryGetInt64(out var n)) id = n;
                else if (raw.ValueKind == JsonValueKind.String
                         && long.TryParse(raw.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var s)) id = s;
            }

            return success.ValueKind == JsonValueKind.True
                ? new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Accepted, message, id is > 0 ? id : null)
                : new GarageProAddPaymentResult(GarageProAddPaymentOutcome.Rejected, message);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static JsonElement? Property(JsonElement element, string name)
    {
        foreach (var p in element.EnumerateObject())
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                return p.Value;
        return null;
    }

    private static async Task<string?> ReadLimitedAsync(HttpContent content, CancellationToken ct)
    {
        if (content.Headers.ContentLength is > MaxResponseBytes) return null;
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) return null;
            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "", StringComparison.Ordinal) + "\"";

    /// <summary>ชื่อไฟล์ ASCII ล้วน (สร้างเองที่ service อยู่แล้ว · กันอักขระที่ header รับไม่ได้)</summary>
    private static string SafeFileName(string name)
    {
        var clean = new string(name.Where(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.').ToArray());
        return clean.Length == 0 ? "slip.jpg" : clean;
    }
}
