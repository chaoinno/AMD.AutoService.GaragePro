using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Dtos;

public sealed record PaymentDto(
    Guid Id,
    string Method,
    decimal Amount,
    string? Reference,
    string ReceivedByName,
    DateTime ReceivedAt);

public sealed record PaymentReceiptDto(
    Guid Id,
    string DocumentNo,
    decimal NetAmount,
    decimal VatAmount,
    decimal TotalAmount,
    string IssuedByName,
    DateTime IssuedAt);

/// <summary>ยอดสรุปเพื่อขับหน้า "ชำระเงิน/ส่งมอบ" ทั้งหมดในคำขอเดียว</summary>
public sealed record PaymentSummaryDto(
    Guid JobId,
    decimal NetAmount,
    decimal VatAmount,
    decimal GrandTotal,
    decimal PaidAmount,
    decimal RemainingAmount,
    bool BalanceSettled,
    bool VatIncluded,
    /// <summary>ล็อกแก้ VatIncluded ไม่ได้อีก — เริ่มบันทึกชำระเงินหรือออกใบเสร็จไปแล้ว</summary>
    bool VatLocked,
    IReadOnlyList<PaymentDto> Payments,
    PaymentReceiptDto? Receipt);

/// <summary>Method: "cash" | "transfer" | "card" | "qr" — RequestId กันบันทึกซ้ำเมื่อ retry</summary>
public sealed record RecordPaymentRequest(string Method, decimal Amount, string? Reference, Guid RequestId);

public sealed record SetVatIncludedRequest(bool Included);

public static class PosMapper
{
    public static PaymentDto ToDto(Payment payment) => new(
        payment.Id, ToMethodToken(payment.Method), payment.Amount, payment.Reference,
        payment.ReceivedByName, payment.ReceivedAt);

    public static PaymentReceiptDto ToDto(Receipt receipt) => new(
        receipt.Id, receipt.DocumentNo, receipt.NetAmount, receipt.VatAmount, receipt.TotalAmount,
        receipt.IssuedByName, receipt.IssuedAt);

    public static string ToMethodToken(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "cash",
        PaymentMethod.Transfer => "transfer",
        PaymentMethod.Card => "card",
        PaymentMethod.Qr => "qr",
        _ => "cash"
    };

    public static PaymentMethod? ParseMethodToken(string token) => token.Trim().ToLowerInvariant() switch
    {
        "cash" => PaymentMethod.Cash,
        "transfer" => PaymentMethod.Transfer,
        "card" => PaymentMethod.Card,
        "qr" => PaymentMethod.Qr,
        _ => null
    };
}
