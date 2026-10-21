using System.Globalization;
using System.Text.RegularExpressions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

/// <summary>ผลตรวจค่าหนึ่งช่อง · <see cref="Error"/> null = ผ่าน</summary>
public readonly record struct Parsed<T>(T Value, ApiError? Error)
{
    public bool Ok => Error is null;
    public static Parsed<T> Fail(ApiError error) => new(default!, error);
}

/// <summary>
/// กฎของฟอร์มแจ้งชำระ (docs/17 §2 + §5.1) · ฟังก์ชันล้วน ทดสอบได้โดยไม่มีฐาน
/// · ค่าทุกช่องรับเป็นข้อความ (multipart) แล้วตรวจที่นี่ เพื่อให้ตอบ envelope 422 พร้อม <c>field</c> เสมอ
///   ไม่ใช่ 400 ProblemDetails ของ model binding
/// · รหัสสถานะ/เหตุผลใน API เป็น snake_case ตัวพิมพ์เล็ก ส่วนในฐานเป็นชื่อ enum (Pending · SlipUnreadable)
/// </summary>
public static partial class PackagePaymentRules
{
    public const int MaxRequestIdLength = 100;
    public const int MaxNoteLength = 500;
    public const int MaxReviewNoteLength = 1000;
    public const decimal MaxAmount = 999_999.99m;

    /// <summary>ปี ค.ศ. เกินเท่านี้ = ผู้ใช้กรอกปี พ.ศ. (ข้อมูลจริงของระบบเดิมมี PayDate ปี 2569)</summary>
    public const int BuddhistYearThreshold = 2400;

    public static Parsed<string> ParseRequestId(string? value)
    {
        var id = value?.Trim();
        return string.IsNullOrEmpty(id) || id.Length > MaxRequestIdLength
            ? Parsed<string>.Fail(PackageErrors.Validation(
                "คำขอไม่ถูกต้อง กรุณาปิดหน้าต่างแล้วกดแจ้งชำระอีกครั้ง", "requestId"))
            : new Parsed<string>(id, null);
    }

    /// <summary>"1500.00" · "1,500" · "1500.5" → ทศนิยม 2 ตำแหน่ง · ต้อง &gt; 0 · ทศนิยมไม่เกิน 2 · ไม่เกิน 999,999.99</summary>
    public static Parsed<decimal> ParseAmount(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return Parsed<decimal>.Fail(PackageErrors.Validation("กรุณากรอกยอดเงินที่โอน", "amount"));
        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture, out var amount))
            return Parsed<decimal>.Fail(PackageErrors.Validation("ยอดเงินต้องเป็นตัวเลข เช่น 1500.00", "amount"));
        if (amount <= 0)
            return Parsed<decimal>.Fail(PackageErrors.Validation("ยอดเงินต้องมากกว่า 0", "amount"));
        if (decimal.Round(amount, 2) != amount)
            return Parsed<decimal>.Fail(PackageErrors.Validation("ยอดเงินมีทศนิยมได้ไม่เกิน 2 ตำแหน่ง", "amount"));
        if (amount > MaxAmount)
            return Parsed<decimal>.Fail(PackageErrors.Validation("ยอดเงินต้องไม่เกิน 999,999.99 บาท", "amount"));
        return new Parsed<decimal>(Money(amount), null);
    }

    /// <summary>เงินทศนิยม 2 ตำแหน่งเสมอ (590 → 590.00) ให้ JSON ออกมาเป็นรูปเดียวกันทุกจุด</summary>
    public static decimal Money(decimal value) => decimal.Round(value, 2, MidpointRounding.AwayFromZero) + 0.00m;

    /// <summary>
    /// วันเวลาที่โอน → UTC · ISO 8601 มี offset ใช้ตามนั้น · ไม่มี offset = เวลาไทย (ลูกค้าอยู่ไทย กันพลาด 7 ชม.)
    /// · ห้ามเป็นอนาคตเกิน 5 นาที (นาฬิกาคลาด/วงล้อนาทีของแอป) · ย้อนหลังได้ไม่เกิน <paramref name="maxPastDays"/> วัน
    /// · ปี ≥ 2400 ได้ข้อความ "กรุณาใช้ปี ค.ศ." แยกจากข้อความอื่น (ตัวเลือกวันที่ของเว็บ/แอปให้ ค.ศ. เสมอ แต่ API ต้องกันเอง)
    /// </summary>
    public static Parsed<DateTime> ParsePaidAt(string? value, DateTime nowUtc, int maxPastDays)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return Parsed<DateTime>.Fail(PackageErrors.Validation("กรุณาระบุวันเวลาที่โอน", "paidAt"));

        DateTime utc;
        var hasOffset = text.EndsWith('Z') || text.EndsWith('z') || OffsetSuffix().IsMatch(text);
        if (hasOffset)
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var offset))
                return InvalidPaidAt();
            if (offset.Year >= BuddhistYearThreshold) return BuddhistYear();
            utc = offset.UtcDateTime;
        }
        else
        {
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
                return InvalidPaidAt();
            if (local.Year >= BuddhistYearThreshold) return BuddhistYear();
            utc = BranchPackageDates.ToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified));
        }

        if (utc > nowUtc + SelfServeOptions.PackageOptions.PaidAtFutureTolerance)
            return Parsed<DateTime>.Fail(PackageErrors.Validation("วันเวลาที่โอนต้องไม่เป็นเวลาในอนาคต", "paidAt"));
        if (utc < nowUtc.AddDays(-maxPastDays))
            return Parsed<DateTime>.Fail(PackageErrors.Validation(
                $"วันที่โอนย้อนหลังได้ไม่เกิน {maxPastDays} วัน ถ้าโอนนานกว่านั้นกรุณาติดต่อ {SelfServeText.ContactLine}",
                "paidAt"));

        return new Parsed<DateTime>(DateTime.SpecifyKind(utc, DateTimeKind.Utc), null);

        static Parsed<DateTime> InvalidPaidAt() => Parsed<DateTime>.Fail(PackageErrors.Validation(
            "รูปแบบวันเวลาที่โอนไม่ถูกต้อง", "paidAt"));

        static Parsed<DateTime> BuddhistYear() => Parsed<DateTime>.Fail(PackageErrors.Validation(
            "กรุณาใช้ปี ค.ศ. (เช่น 2026 ไม่ใช่ 2569)", "paidAt"));
    }

    public static Parsed<int> ParseMonthCount(string? value, IReadOnlyList<int> options)
    {
        if (int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var months)
            && options.Contains(months))
            return new Parsed<int>(months, null);
        return Parsed<int>.Fail(PackageErrors.Validation(
            $"จำนวนเดือนต้องเป็น {string.Join(", ", options.Select(m => m.ToString(CultureInfo.InvariantCulture)))}",
            "monthCount"));
    }

    /// <summary>ว่าง = null · ตัดขึ้นบรรทัดใหม่ (ข้อความไปลง LINE บรรทัดเดียว · กติกาเดียวกับ SelfServeText.SingleLine)</summary>
    public static Parsed<string?> ParseNote(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new Parsed<string?>(null, null);
        if (value.Trim().Length > MaxNoteLength)
            return Parsed<string?>.Fail(PackageErrors.Validation($"หมายเหตุยาวเกิน {MaxNoteLength} ตัวอักษร", "note"));
        return new Parsed<string?>(SelfServeText.SingleLine(value, MaxNoteLength), null);
    }

    /// <summary>ชื่อไฟล์สลิปตอนดาวน์โหลด (ลูกค้า · API ภายใน) · เก็บเป็น JPEG หรือ PDF เท่านั้น (docs/17 §5.2)</summary>
    public static string SlipFileName(PackagePayment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        var extension = payment.SlipContentType == "application/pdf" ? "pdf" : "jpg";
        return $"slip-{ShortId(payment.Id)}.{extension}";
    }

    // ---------- รหัสใน API ----------

    public static string StatusCode(PackagePaymentStatus status) => status switch
    {
        PackagePaymentStatus.Pending => "pending",
        PackagePaymentStatus.Approved => "approved",
        PackagePaymentStatus.Rejected => "rejected",
        PackagePaymentStatus.Cancelled => "cancelled",
        _ => status.ToString().ToLowerInvariant()
    };

    public static bool TryParseStatus(string? code, out PackagePaymentStatus status)
    {
        foreach (var candidate in Enum.GetValues<PackagePaymentStatus>())
            if (string.Equals(StatusCode(candidate), code?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }

        status = default;
        return false;
    }

    public static string RejectReasonCode(PackagePaymentRejectReason reason) => reason switch
    {
        PackagePaymentRejectReason.SlipUnreadable => "slip_unreadable",
        PackagePaymentRejectReason.AmountMismatch => "amount_mismatch",
        PackagePaymentRejectReason.NotReceived => "not_received",
        PackagePaymentRejectReason.Duplicate => "duplicate",
        _ => "other"
    };

    /// <summary>ข้อความตายตัวตามตาราง docs/17 §5.1</summary>
    public static string RejectReasonTh(PackagePaymentRejectReason reason) => reason switch
    {
        PackagePaymentRejectReason.SlipUnreadable => "สลิปไม่ชัด อ่านไม่ออก",
        PackagePaymentRejectReason.AmountMismatch => "ยอดเงินไม่ตรงกับแพ็กเกจ",
        PackagePaymentRejectReason.NotReceived => "ไม่พบยอดเงินเข้าบัญชี",
        PackagePaymentRejectReason.Duplicate => "สลิปซ้ำกับที่เคยแจ้ง",
        _ => "อื่นๆ"
    };

    public static IReadOnlyList<string> RejectReasonCodes { get; } =
        Enum.GetValues<PackagePaymentRejectReason>().Select(RejectReasonCode).ToArray();

    public static bool TryParseRejectReason(string? code, out PackagePaymentRejectReason reason)
    {
        foreach (var candidate in Enum.GetValues<PackagePaymentRejectReason>())
            if (string.Equals(RejectReasonCode(candidate), code?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                reason = candidate;
                return true;
            }

        reason = default;
        return false;
    }

    /// <summary>8 ตัวแรกของ id แบบไม่มีขีด ตัวพิมพ์เล็ก (ใช้ใน LINE/spadmin)</summary>
    public static string ShortId(Guid id) => id.ToString("N")[..8];

    [GeneratedRegex(@"[+-]\d{2}:?\d{2}$")]
    private static partial Regex OffsetSuffix();
}
