using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

public sealed class AttachmentRepository(ServiceDbContext db) : IAttachmentRepository
{
    public async Task AddAsync(Attachment attachment, CancellationToken ct = default) =>
        await db.Attachments.AddAsync(attachment, ct);

    public Task<Attachment?> GetAsync(Guid id, CancellationToken ct = default) =>
        db.Attachments.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<Attachment?> GetByPathAsync(string relativePath, CancellationToken ct = default) =>
        db.Attachments.Include(a => a.Job).FirstOrDefaultAsync(a => a.RelativePath == relativePath, ct);

    public async Task<IReadOnlyList<Attachment>> GetForJobAsync(
        Guid jobId, string? kind, CancellationToken ct = default)
    {
        var query = db.Attachments.Where(a => a.JobId == jobId);

        if (!string.IsNullOrWhiteSpace(kind))
            query = query.Where(a => a.Kind == kind);

        return await query.OrderBy(a => a.UploadedAt).ToListAsync(ct);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
