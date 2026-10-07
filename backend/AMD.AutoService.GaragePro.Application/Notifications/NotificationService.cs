using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.Notifications;

public sealed record NotificationDto(
    Guid Id,
    string Kind,
    string TitleTh,
    string? BodyTh,
    Guid? JobId,
    string EntityType,
    Guid? EntityId,
    string? LinkHint,
    string ActorName,
    DateTime CreatedAt,
    DateTime? ReadAt,
    DateTime? ResolvedAt,
    string? ResolvedByName);

public sealed record NotificationPageDto(IReadOnlyList<NotificationDto> Items, bool HasMore);

/// <summary>ห่อเป็น object เพราะ client ถือว่า data ที่เป็น null/ไม่มีคือ error — ตัวเลข 0 เปล่าๆ ปลอดภัย แต่ขยายต่อไม่ได้</summary>
public sealed record NotificationCountDto(int Unread);

public interface INotificationService
{
    Task<Result<NotificationPageDto>> ListAsync(
        bool unreadOnly, DateTime? beforeAt, Guid? beforeId, int take, CancellationToken ct = default);
    Task<Result<NotificationCountDto>> UnreadCountAsync(CancellationToken ct = default);
    Task<Result<NotificationCountDto>> MarkReadAsync(Guid id, CancellationToken ct = default);
    Task<Result<NotificationCountDto>> MarkUnreadAsync(Guid id, CancellationToken ct = default);
    Task<Result<NotificationCountDto>> MarkAllReadAsync(CancellationToken ct = default);
}

/// <summary>
/// กล่องแจ้งเตือนของผู้ใช้ปัจจุบัน — เห็นได้เฉพาะตาม <see cref="NotificationRules.VisibleTo"/>
/// ไม่ผูก role เพิ่ม: ข้อมูลถูกกรองตามผู้รับอยู่แล้ว ทุกคนจึงเห็นได้แค่ของตัวเอง
/// </summary>
public sealed class NotificationService(
    INotificationRepository repository,
    ILegacyUserReader legacyUsers,
    ICurrentUser user,
    TimeProvider clock) : INotificationService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private DateTime Since => Now - NotificationRules.HistoryWindow;

    public async Task<Result<NotificationPageDto>> ListAsync(
        bool unreadOnly, DateTime? beforeAt, Guid? beforeId, int take, CancellationToken ct = default)
    {
        if (await ViewerAsync(ct) is not { } viewer) return NoStaff<NotificationPageDto>();
        if (beforeAt.HasValue != beforeId.HasValue)
            return Result<NotificationPageDto>.Fail(
                "NOTIFICATION_VALIDATION", "ต้องส่ง beforeAt และ beforeId คู่กันเสมอ");

        take = take is > 0 and <= 100 ? take : 30;
        var rows = await repository.PageAsync(viewer, Since, unreadOnly, beforeAt, beforeId, take + 1, ct);
        var hasMore = rows.Count > take;
        var items = rows.Take(take).Select(ToDto).ToList();
        return Result<NotificationPageDto>.Ok(new NotificationPageDto(items, hasMore));
    }

    public async Task<Result<NotificationCountDto>> UnreadCountAsync(CancellationToken ct = default)
    {
        if (await ViewerAsync(ct) is not { } viewer) return NoStaff<NotificationCountDto>();
        return Result<NotificationCountDto>.Ok(await CountAsync(viewer, ct));
    }

    public async Task<Result<NotificationCountDto>> MarkReadAsync(Guid id, CancellationToken ct = default)
    {
        if (await ViewerAsync(ct) is not { } viewer) return NoStaff<NotificationCountDto>();
        if (await repository.GetVisibleAsync(viewer, Since, id, ct) is null) return NotFound();

        // ทำซ้ำได้โดยผลไม่เปลี่ยน — คลิกการ์ดเดิมสองครั้งไม่ใช่ error และเวลาที่อ่านครั้งแรกไม่ถูกทับ
        if (await repository.GetReadAsync(id, viewer.StaffId, ct) is null)
        {
            repository.AddRead(new NotificationRead { NotificationId = id, StaffId = viewer.StaffId, ReadAt = Now });
            await repository.SaveChangesAsync(ct);
        }

        return Result<NotificationCountDto>.Ok(await CountAsync(viewer, ct));
    }

    public async Task<Result<NotificationCountDto>> MarkUnreadAsync(Guid id, CancellationToken ct = default)
    {
        if (await ViewerAsync(ct) is not { } viewer) return NoStaff<NotificationCountDto>();
        if (await repository.GetVisibleAsync(viewer, Since, id, ct) is null) return NotFound();

        if (await repository.GetReadAsync(id, viewer.StaffId, ct) is { } read)
        {
            repository.RemoveRead(read);
            await repository.SaveChangesAsync(ct);
        }

        return Result<NotificationCountDto>.Ok(await CountAsync(viewer, ct));
    }

    public async Task<Result<NotificationCountDto>> MarkAllReadAsync(CancellationToken ct = default)
    {
        if (await ViewerAsync(ct) is not { } viewer) return NoStaff<NotificationCountDto>();

        var ids = await repository.GetUnreadIdsAsync(viewer, Since, ct);
        foreach (var id in ids)
            repository.AddRead(new NotificationRead { NotificationId = id, StaffId = viewer.StaffId, ReadAt = Now });
        if (ids.Count > 0) await repository.SaveChangesAsync(ct);

        return Result<NotificationCountDto>.Ok(await CountAsync(viewer, ct));
    }

    private async Task<NotificationCountDto> CountAsync(NotificationViewer viewer, CancellationToken ct) =>
        new(await repository.CountUnreadAsync(viewer, Since, ct));

    private async Task<NotificationViewer?> ViewerAsync(CancellationToken ct) =>
        await CurrentStaff.ResolveAsync(user, legacyUsers, ct) is long staffId
            ? new NotificationViewer(user.ShardKey, user.BranchId, staffId, user.Role)
            : null;

    private static Result<T> NoStaff<T>() => Result<T>.Fail(
        "NOTIFICATION_NO_STAFF_PROFILE", "บัญชีนี้ไม่ได้ผูกกับข้อมูลพนักงาน — ออกจากระบบแล้วเข้าใหม่อีกครั้ง");

    private static Result<NotificationCountDto> NotFound() => Result<NotificationCountDto>.Fail(
        "NOTIFICATION_NOT_FOUND", "ไม่พบการแจ้งเตือนนี้ หรือไม่ใช่การแจ้งเตือนของคุณ");

    private static NotificationDto ToDto(NotificationRow row)
    {
        var n = row.Notification;
        return new NotificationDto(
            n.Id, n.Kind, n.TitleTh, n.BodyTh, n.JobId, n.EntityType, n.EntityId, n.LinkHint, n.ActorName,
            n.CreatedAt, row.ReadAt, n.ResolvedAt, n.ResolvedByName);
    }
}
