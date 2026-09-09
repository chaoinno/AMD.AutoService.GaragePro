using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Domain.Entities;

/// <summary>
/// การชำระเงินหนึ่งรายการของงาน — MVP: บันทึกยอดเดียวต่อครั้ง ไม่มี split/EDC/QR gateway จริง
/// (docs/01-workflow.md §3.9 ตัดขอบเขตแล้ว) [BIZ] RequestId/RequestHash กันบันทึกซ้ำ (invariant #8)
/// </summary>
public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public PaymentMethod Method { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public Guid RequestId { get; set; }
    public string RequestHash { get; set; } = "";
    public long ReceivedByUserId { get; set; }
    public string ReceivedByName { get; set; } = "";
    public DateTime ReceivedAt { get; set; }
}

/// <summary>
/// ใบเสร็จรับเงิน — 1 job ออกได้ใบเดียว (MVP: ไม่มีใบกำกับภาษี/reprint/void, OQ#6-7 ยังไม่ตอบ)
/// </summary>
public class Receipt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid JobId { get; set; }
    public string DocumentNo { get; set; } = "";
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal TotalAmount { get; set; }
    public long IssuedByUserId { get; set; }
    public string IssuedByName { get; set; } = "";
    public DateTime IssuedAt { get; set; }
}

/// <summary>ตัวนับเลขใบเสร็จ RC-{yy}-{seq:D4} ต่อ (ชาร์ด, สาขา, ปี) — MERGE...HOLDLOCK แบบเดียวกับ JobNumberCounter</summary>
public class ReceiptNumberCounter
{
    public string LegacyShardKey { get; set; } = "";
    public int LegacyBranchId { get; set; }
    public int Year { get; set; }
    public int LastSequence { get; set; }
}
