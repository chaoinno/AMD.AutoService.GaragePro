using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IQuotationTemplateRepository
{
    /// <summary>รายการเทมเพลต (พร้อม Lines สำหรับนับจำนวน) — เปิดให้ทุก role อ่านได้ (ตัวเลือกเทมเพลตต้องใช้)</summary>
    Task<IReadOnlyList<QuotationTemplate>> SearchAsync(
        string shardKey, int branchId, string? keyword, bool includeInactive, CancellationToken ct = default);

    Task<QuotationTemplate?> GetWithLinesAsync(
        string shardKey, int branchId, Guid id, CancellationToken ct = default);

    Task<bool> CodeExistsAsync(
        string shardKey, int branchId, string code, Guid? excludingId, CancellationToken ct = default);

    Task AddAsync(QuotationTemplate template, CancellationToken ct = default);

    /// <summary>ใช้ตอนแก้ไขเทมเพลต — แทนที่บรรทัดทั้งชุด (ไม่มีอะไรอ้างอิงบรรทัดเทมเพลตจากภายนอก จึง diff ไม่คุ้ม)</summary>
    void RemoveLines(IEnumerable<QuotationTemplateLine> lines);

    Task AddEventAsync(ActivityEvent activityEvent, CancellationToken ct = default);

    /// <summary>ยิง MasterDataConflictException เมื่อชนรหัสซ้ำ (constraint UX_svc_QuotationTemplate_Code)</summary>
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
