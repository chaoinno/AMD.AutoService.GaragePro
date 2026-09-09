namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IReceiptNumberGenerator
{
    /// <summary>รูปแบบ RC-{yy}-{ลำดับ:D4} — ลำดับเริ่มนับใหม่ทุกปีต่อ (ชาร์ด, สาขา)</summary>
    Task<string> NextAsync(string shardKey, int branchId, DateTime nowLocal, CancellationToken ct = default);
}
