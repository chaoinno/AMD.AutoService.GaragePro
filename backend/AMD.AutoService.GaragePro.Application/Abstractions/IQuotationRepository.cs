using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

public interface IQuotationRepository
{
    Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Quotation?> GetWithLinesAsync(Guid id, CancellationToken ct = default);

    /// <summary>ใบล่าสุดของงานนี้ (เวอร์ชันสูงสุด)</summary>
    Task<Quotation?> GetLatestForJobAsync(Guid jobId, CancellationToken ct = default);

    Task<IReadOnlyList<Quotation>> GetQueueAsync(
        string shardKey, int branchId, string? statusFilter, Guid? jobId = null, CancellationToken ct = default);

    Task<int> GetNextVersionAsync(Guid jobId, CancellationToken ct = default);

    Task AddAsync(Quotation quotation, CancellationToken ct = default);
    Task AddEventAsync(ActivityEvent evt, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface ICatalogRepository
{
    Task<IReadOnlyList<CatalogItem>> SearchAsync(
        string shardKey, int branchId, string? keyword, CancellationToken ct = default);

    Task<IReadOnlyList<CatalogItem>> GetByCodesAsync(
        string shardKey, int branchId, IEnumerable<string> codes, CancellationToken ct = default);
}
