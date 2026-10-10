namespace AMD.AutoService.GaragePro.Application.Dtos;

/// <summary>
/// บอร์ดสถานะรถในอู่ (ดูอย่างเดียว) — คอลัมน์ครบทุกสถานะที่ยังเปิดอยู่เสมอแม้ว่าง ให้ตำแหน่งคอลัมน์คงที่
/// [BIZ] ไม่มีตัวเงินใดๆ เพราะทุกบทบาทรวมช่างเปิดหน้านี้ได้
/// </summary>
public sealed record JobBoardDto(
    IReadOnlyList<JobBoardColumnDto> Columns,
    int Total,
    bool Truncated,
    int Limit,
    DateTime GeneratedAt);

public sealed record JobBoardColumnDto(string Status, string StatusLabel, IReadOnlyList<JobBoardCardDto> Cards);

/// <param name="StatusSince">เวลาที่เข้าสถานะปัจจุบัน (event เปลี่ยนสถานะล่าสุด หรือเวลาเปิดจ๊อบถ้าไม่มี)</param>
/// <param name="LatestChatMessageId">id ข้อความแชทล่าสุด — client เทียบกับที่เครื่องนี้เคยเห็นเอง (ไม่ sync ข้ามเครื่อง)</param>
public sealed record JobBoardCardDto(
    JobDto Job,
    DateTime StatusSince,
    IReadOnlyList<JobBoardWorkerDto> ActiveWorkers,
    IReadOnlyList<string> AwaitingCustomerQuotationCodes,
    Guid? LatestChatMessageId);

/// <param name="Kind">work = กำลังทำ · pause = พักอยู่</param>
public sealed record JobBoardWorkerDto(string TechnicianName, string Kind, DateTime StartedAt);
