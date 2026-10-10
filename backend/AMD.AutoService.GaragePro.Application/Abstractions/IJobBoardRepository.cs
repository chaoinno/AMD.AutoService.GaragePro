using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// อ่านอย่างเดียวสำหรับบอร์ดสถานะรถในอู่ — แยกจาก IJobRepository โดยตั้งใจ (fake ของ IJobRepository มีหลายไฟล์ในเทสต์
/// เพิ่มเมธอดที่นั่นแล้ว build พังทุกไฟล์) · jobIds ที่รับมาต้องมาจาก GetOpenInShopJobsAsync ซึ่งสโคป shard/สาขาแล้ว
/// </summary>
public interface IJobBoardRepository
{
    /// <summary>จ๊อบรถในอู่ (JobTypeId=9) ที่ยังไม่ถึงสถานะจบ ของสาขานี้ — ค้นด้วยเลขจ๊อบ/ทะเบียน/ชื่อลูกค้า/เบอร์</summary>
    Task<IReadOnlyList<Job>> GetOpenInShopJobsAsync(
        string shardKey, int branchId, string? keyword, int take, CancellationToken ct = default);

    /// <summary>เวลาของ event job.opened/job.status.changed ล่าสุดต่อจ๊อบ</summary>
    Task<IReadOnlyDictionary<Guid, DateTime>> GetLastStatusChangeAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default);

    /// <summary>คาบจับเวลาที่ยังเปิดอยู่ (ไม่รวมคาบที่ถูกยกเลิก)</summary>
    Task<IReadOnlyList<WorkInterval>> GetOpenWorkIntervalsAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default);

    /// <summary>ใบเสนอราคาสถานะ "ส่งแล้ว" พร้อมบรรทัดและการเซ็น — ให้ JobQuotations.AwaitingCustomer ตัดสินต่อ</summary>
    Task<IReadOnlyList<Quotation>> GetSentQuotationsAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default);
}
