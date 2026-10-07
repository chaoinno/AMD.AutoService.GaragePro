using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Notifications;
using AMD.AutoService.GaragePro.Domain.Entities;
using AMD.AutoService.GaragePro.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AMD.AutoService.GaragePro.Infrastructure.Notifications;

/// <summary>
/// อยู่ที่ Infrastructure (ไม่ใช่ Application) เพราะต้อง log ตอนข้ามผู้รับ — Application ไม่มี dependency logging
/// ดูสัญญาที่ <see cref="INotificationPublisher"/> · ห้าม SaveChanges ที่นี่
/// </summary>
public sealed class NotificationPublisher(
    INotificationRepository repository,
    ILegacyUserReader legacyUsers,
    ICurrentUser user,
    TimeProvider clock,
    ILogger<NotificationPublisher> logger) : INotificationPublisher
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool actorResolved;
    private long? actorStaffId;

    public async Task ToStaffAsync(NotificationDraft draft, IEnumerable<long> staffIds, CancellationToken ct = default)
    {
        var actor = await ActorStaffIdAsync(ct);
        foreach (var staffId in staffIds.Where(id => id > 0 && id != actor).Distinct())
            repository.Add(Build(draft, actor, recipientStaffId: staffId, audience: 0));
    }

    public async Task ToUserAsync(NotificationDraft draft, long userId, CancellationToken ct = default)
    {
        if (userId <= 0 || userId == user.UserId) return;

        long? staffId;
        try
        {
            staffId = (await legacyUsers.FindByIdAsync(user.ShardKey, userId, ct))?.StaffId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // [BIZ] แจ้งเตือนเป็นของแถม — Garage DB สะดุดต้องไม่ทำให้อนุมัติ/เซ็นที่เพิ่งทำล้มไปด้วย
            logger.LogWarning(ex, "Skip notification {Kind}: cannot resolve staff for user {UserId}", draft.Kind, userId);
            return;
        }

        if (staffId is long id) await ToStaffAsync(draft, [id], ct);
        else logger.LogWarning("Skip notification {Kind}: user {UserId} has no staff profile", draft.Kind, userId);
    }

    public async Task ToRolesAsync(NotificationDraft draft, IEnumerable<UserRole> roles, CancellationToken ct = default)
    {
        var audience = NotificationAudience.Of(roles);
        if (audience == 0) return;
        repository.Add(Build(draft, await ActorStaffIdAsync(ct), recipientStaffId: null, audience));
    }

    public async Task ResolveAsync(string subjectKey, CancellationToken ct = default)
    {
        foreach (var n in await repository.GetOpenBySubjectAsync(user.ShardKey, user.BranchId, subjectKey, ct))
        {
            n.ResolvedAt = Now;
            n.ResolvedByName = user.UserName;
        }
    }

    public async Task<bool> HasOpenAsync(string subjectKey, CancellationToken ct = default) =>
        (await repository.GetOpenBySubjectAsync(user.ShardKey, user.BranchId, subjectKey, ct)).Count > 0;

    private Notification Build(NotificationDraft draft, long? actor, long? recipientStaffId, int audience) => new()
    {
        LegacyShardKey = user.ShardKey,
        LegacyBranchId = user.BranchId,
        Kind = draft.Kind,
        TitleTh = NotificationRules.Truncate(draft.TitleTh, 200),
        BodyTh = draft.BodyTh is null ? null : NotificationRules.Truncate(draft.BodyTh, 500),
        RecipientStaffId = recipientStaffId,
        AudienceRoles = audience,
        JobId = draft.JobId,
        EntityType = draft.EntityType,
        EntityId = draft.EntityId,
        LinkHint = draft.LinkHint,
        ActorUserId = user.UserId,
        ActorStaffId = actor,
        ActorName = user.UserName,
        SubjectKey = draft.SubjectKey,
        CreatedAt = Now
    };

    /// <summary>ระบุไม่ได้ (legacy ล่ม + token ไม่มี claim) = null → ไม่ตัดผู้กระทำออก ดีกว่าทำ action ล้ม</summary>
    private async Task<long?> ActorStaffIdAsync(CancellationToken ct)
    {
        if (actorResolved) return actorStaffId;
        try
        {
            actorStaffId = await CurrentStaff.ResolveAsync(user, legacyUsers, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Cannot resolve actor staff id for user {UserId}", user.UserId);
            actorStaffId = null;
        }
        actorResolved = true;
        return actorStaffId;
    }
}
