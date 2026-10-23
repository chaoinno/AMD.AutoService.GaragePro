using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe.Package;

public enum LineCommandAction
{
    Approve,
    Reject
}

/// <summary>คำสั่งที่ตรวจลายเซ็นและวันหมดแล้ว · <see cref="Reason"/> เฉพาะ <see cref="LineCommandAction.Reject"/></summary>
public sealed record LineCommand(Guid PaymentId, LineCommandAction Action, PackagePaymentRejectReason? Reason, long ExpiresUnix);

public enum LineCommandCheck
{
    Valid,

    /// <summary>ไม่ได้ขึ้นต้นด้วย <c>/SP_</c> (ข้อความอื่นในกลุ่ม · ตัวส่งต่อไม่ควรส่งมา)</summary>
    NotCommand,

    /// <summary>server ไม่ได้ตั้ง <c>LineCommandKey</c> จึงตรวจอะไรไม่ได้</summary>
    NoKey,

    /// <summary>รูปแบบผิด หรือลายเซ็นไม่ตรง (พิมพ์เอง/แก้ข้อความ/คีย์เปลี่ยน)</summary>
    Invalid,

    /// <summary>ลายเซ็นถูกแต่เลย 14 วันแล้ว</summary>
    Expired
}

/// <summary>
/// คำสั่ง <c>/SP_{token}</c> บนปุ่มของการ์ดแจ้งชำระใน LINE (docs/17 §8.7.2) · ฟังก์ชันล้วน
/// · <c>token = v1.{base64url(payload)}.{base64url(HMAC-SHA256(LineCommandKey, payload))}</c>
///   <c>payload = "{paymentId:N}|{approve|reject}|{reasonCode}|{exp}"</c> (reasonCode ว่างเมื่ออนุมัติ · exp = unix วินาที)
/// · [SECURITY] ตรวจลายเซ็นแบบ constant-time ก่อนดูวันหมดและก่อนแตะฐาน · เดาไม่ได้โดยไม่มีคีย์ · อายุ 14 วัน
/// · ข้อความรวมไม่เกิน 300 ตัวอักษร (ขีดจำกัด text ของ message action ใน LINE)
/// </summary>
public static class PackageLineCommands
{
    public const string Prefix = "/SP_";
    public const string Version = "v1";
    public const int MaxTextLength = 300;
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public static bool IsCommand(string? text) =>
        text is not null && text.TrimStart().StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>ข้อความเต็มที่ปุ่มส่ง (<c>/SP_v1.….…</c>)</summary>
    public static string Create(string key, Guid paymentId, LineCommandAction action, PackagePaymentRejectReason? reason,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (action == LineCommandAction.Reject && reason is null)
            throw new ArgumentException("คำสั่งปฏิเสธต้องมีเหตุผล", nameof(reason));

        var exp = Unix(nowUtc + Lifetime);
        var payload = Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"{paymentId:N}|{ActionCode(action)}|{(action == LineCommandAction.Reject ? PackagePaymentRules.RejectReasonCode(reason!.Value) : "")}|{exp}"));
        var text = $"{Prefix}{Version}.{Base64Url(payload)}.{Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), payload))}";
        if (text.Length > MaxTextLength) throw new InvalidOperationException("คำสั่ง LINE ยาวเกิน 300 ตัวอักษร");
        return text;
    }

    public static LineCommandCheck Verify(string? text, string? key, DateTime nowUtc, out LineCommand? command)
    {
        command = null;
        if (!IsCommand(text)) return LineCommandCheck.NotCommand;
        if (string.IsNullOrEmpty(key)) return LineCommandCheck.NoKey;

        var token = text!.Trim()[Prefix.Length..];
        if (token.Length > MaxTextLength) return LineCommandCheck.Invalid;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0] != Version) return LineCommandCheck.Invalid;
        if (!TryBase64Url(parts[1], out var payload) || !TryBase64Url(parts[2], out var signature))
            return LineCommandCheck.Invalid;

        var expected = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), payload);
        if (!CryptographicOperations.FixedTimeEquals(expected, signature)) return LineCommandCheck.Invalid;

        // ลายเซ็นถูก = payload มาจากเราเอง · ผิดรูปตรงนี้ = บั๊กของเรา ถือว่าใช้ไม่ได้เหมือนกัน
        var fields = Encoding.UTF8.GetString(payload).Split('|');
        if (fields.Length != 4
            || !Guid.TryParseExact(fields[0], "N", out var paymentId)
            || !TryAction(fields[1], out var action)
            || !long.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out var exp))
            return LineCommandCheck.Invalid;

        PackagePaymentRejectReason? reason = null;
        if (action == LineCommandAction.Reject)
        {
            if (!PackagePaymentRules.TryParseRejectReason(fields[2], out var parsed)) return LineCommandCheck.Invalid;
            reason = parsed;
        }
        else if (fields[2].Length > 0) return LineCommandCheck.Invalid;

        if (exp <= Unix(nowUtc)) return LineCommandCheck.Expired;

        command = new LineCommand(paymentId, action, reason, exp);
        return LineCommandCheck.Valid;
    }

    private static string ActionCode(LineCommandAction action) => action == LineCommandAction.Approve ? "approve" : "reject";

    private static bool TryAction(string code, out LineCommandAction action)
    {
        action = code == "reject" ? LineCommandAction.Reject : LineCommandAction.Approve;
        return code is "approve" or "reject";
    }

    private static long Unix(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static bool TryBase64Url(string text, out byte[] bytes)
    {
        bytes = [];
        if (text.Length is 0 or > 200) return false;
        var s = text.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: return false;
        }

        var buffer = new byte[s.Length];
        if (!Convert.TryFromBase64String(s, buffer, out var written)) return false;
        bytes = buffer[..written];
        return true;
    }
}
