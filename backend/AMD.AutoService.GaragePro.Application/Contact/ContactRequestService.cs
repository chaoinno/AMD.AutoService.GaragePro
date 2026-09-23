using System.Globalization;
using System.Text;
using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.Contact;

/// <summary>
/// ฟอร์ม "ขอ Demo" จากหน้า landing (/login) — ผู้ส่งเป็นบุคคลภายนอกที่ไม่ได้ล็อกอิน
/// </summary>
/// <param name="RequestId">UUID ที่ client สร้างครั้งเดียวต่อการกดส่ง ใช้เป็น X-Line-Retry-Key
/// ให้การกดซ้ำ/ลองใหม่หลังเน็ตหลุดไม่ทำให้กลุ่ม LINE ได้ข้อความซ้ำ</param>
/// <param name="Website">honeypot — ช่องที่ซ่อนจากคน ถ้ามีค่าแปลว่าเป็นบอทกรอกทุกช่อง</param>
public sealed record ContactRequest(
    Guid RequestId,
    string? Name,
    string? Garage,
    string? Phone,
    string? LineId,
    string? GarageSize,
    string? Note,
    string? Website = null);

public sealed record ContactRequestAccepted(Guid RequestId);

/// <summary>ส่งข้อความแจ้งทีมขาย — implementation ปัจจุบันคือ LINE Messaging API push ไปกลุ่ม</summary>
public interface IContactNotifier
{
    bool IsConfigured { get; }

    /// <returns>true เมื่อปลายทางรับข้อความแล้ว (รวมกรณีเป็นคำขอซ้ำที่เคยส่งสำเร็จแล้ว)</returns>
    Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct);
}

public interface IContactRequestService
{
    Task<Result<ContactRequestAccepted>> SubmitAsync(ContactRequest request, CancellationToken ct);
}

public sealed class ContactRequestService(IContactNotifier notifier, TimeProvider clock) : IContactRequestService
{
    // ตัวเลือกเดียวกับปุ่มบนหน้าเว็บ — ค่าอื่นจาก client ถูกปฏิเสธ ไม่ปล่อยข้อความอิสระเข้ากลุ่ม
    public static readonly IReadOnlyList<string> GarageSizes = ["น้อยกว่า 30 คัน", "30–100 คัน", "มากกว่า 100 คัน"];

    private const int MaxShort = 100;
    private const int MaxNote = 1000;

    private static readonly TimeZoneInfo Bangkok = ResolveBangkok();

    public async Task<Result<ContactRequestAccepted>> SubmitAsync(ContactRequest request, CancellationToken ct)
    {
        // [SECURITY] บอทที่กรอก honeypot ได้คำตอบ "สำเร็จ" เหมือนคนจริง — ไม่บอกใบ้ว่าถูกกรอง
        if (!string.IsNullOrWhiteSpace(request.Website))
            return Result<ContactRequestAccepted>.Ok(new ContactRequestAccepted(request.RequestId));

        var error = Validate(request);
        if (error is not null) return Result<ContactRequestAccepted>.Fail(error);

        if (!notifier.IsConfigured)
            return Result<ContactRequestAccepted>.Fail("CONTACT_UNAVAILABLE",
                "ระบบรับข้อมูลติดต่อยังไม่พร้อมใช้งาน กรุณาติดต่อทาง LINE @garagepro หรือโทร 090-996-6446");

        var sent = await notifier.SendAsync(request.RequestId, FormatMessage(request), ct);
        return sent
            ? Result<ContactRequestAccepted>.Ok(new ContactRequestAccepted(request.RequestId))
            : Result<ContactRequestAccepted>.Fail("CONTACT_SEND_FAILED",
                "ส่งข้อมูลไม่สำเร็จ กรุณาลองใหม่อีกครั้ง หรือติดต่อทาง LINE @garagepro / โทร 090-996-6446");
    }

    private static ApiError? Validate(ContactRequest r)
    {
        if (r.RequestId == Guid.Empty)
            return new ApiError("CONTACT_VALIDATION", "คำขอไม่ถูกต้อง กรุณาโหลดหน้าใหม่แล้วลองอีกครั้ง", "requestId");
        if (string.IsNullOrWhiteSpace(r.Name))
            return new ApiError("CONTACT_VALIDATION", "กรุณากรอกชื่อผู้ติดต่อ", "name");
        if (string.IsNullOrWhiteSpace(r.Garage))
            return new ApiError("CONTACT_VALIDATION", "กรุณากรอกชื่ออู่ / บริษัท", "garage");
        if (string.IsNullOrWhiteSpace(r.Phone))
            return new ApiError("CONTACT_VALIDATION", "กรุณากรอกเบอร์โทร", "phone");

        var digits = r.Phone.Count(char.IsAsciiDigit);
        if (digits is < 9 or > 15 || r.Phone.Any(c => !char.IsAsciiDigit(c) && c is not ('-' or ' ' or '+' or '(' or ')')))
            return new ApiError("CONTACT_VALIDATION", "เบอร์โทรไม่ถูกต้อง", "phone");

        foreach (var (value, field) in new[] { (r.Name, "name"), (r.Garage, "garage"), (r.Phone, "phone"), (r.LineId, "lineId") })
            if (value is { Length: > MaxShort })
                return new ApiError("CONTACT_VALIDATION", $"ข้อมูลยาวเกิน {MaxShort} ตัวอักษร", field);
        if (r.Note is { Length: > MaxNote })
            return new ApiError("CONTACT_VALIDATION", $"รายละเอียดเพิ่มเติมยาวเกิน {MaxNote} ตัวอักษร", "note");

        if (!string.IsNullOrWhiteSpace(r.GarageSize) && !GarageSizes.Contains(r.GarageSize))
            return new ApiError("CONTACT_VALIDATION", "ขนาดอู่ไม่ถูกต้อง", "garageSize");

        return null;
    }

    public string FormatMessage(ContactRequest r)
    {
        var at = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Bangkok);
        var sb = new StringBuilder()
            .AppendLine("📩 ขอ Demo — GaragePro Auto Services")
            .AppendLine($"ชื่อผู้ติดต่อ: {Clean(r.Name)}")
            .AppendLine($"อู่/บริษัท: {Clean(r.Garage)}")
            .AppendLine($"เบอร์โทร: {Clean(r.Phone)}");
        if (!string.IsNullOrWhiteSpace(r.LineId)) sb.AppendLine($"LINE ID: {Clean(r.LineId)}");
        if (!string.IsNullOrWhiteSpace(r.GarageSize)) sb.AppendLine($"ขนาดอู่: {r.GarageSize}");
        if (!string.IsNullOrWhiteSpace(r.Note)) sb.AppendLine($"รายละเอียด: {r.Note.Trim()}");
        // พ.ศ. คำนวณเองแทนการพึ่ง culture ของเครื่อง (th-TH ให้ พ.ศ., container แบบ invariant ให้ ค.ศ.)
        sb.Append(string.Create(CultureInfo.InvariantCulture, $"เวลา: {at:dd/MM}/{at.Year + 543} {at:HH:mm} น."));
        return sb.ToString();
    }

    // ช่องบรรทัดเดียวห้ามมีขึ้นบรรทัดใหม่ — กันการปลอมบรรทัด "เบอร์โทร: ..." ซ้อนเข้ามาในข้อความกลุ่ม
    private static string Clean(string? value) =>
        string.Join(' ', (value ?? "").Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static TimeZoneInfo ResolveBangkok()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("ICT", TimeSpan.FromHours(7), "ICT", "ICT"); }
    }
}
