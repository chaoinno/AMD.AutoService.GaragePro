using AMD.AutoService.GaragePro.Application.Notifications;
using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// ใช้ร่วมกันใน JobChat/Quotation/Purchasing tests — บันทึกว่าแต่ละ action สั่งแจ้งใครด้วยอะไร
/// การตัดผู้กระทำ/แปลง UserId→StaffId เป็นงานของ NotificationPublisher ตัวจริง (ทดสอบแยกที่ NotificationPublisherTests)
/// </summary>
internal sealed class FakeNotificationPublisher : INotificationPublisher
{
    public List<(NotificationDraft Draft, long StaffId)> ToStaff { get; } = [];
    public List<(NotificationDraft Draft, long UserId)> ToUser { get; } = [];
    public List<(NotificationDraft Draft, UserRole[] Roles)> ToRoles { get; } = [];
    public List<string> Resolved { get; } = [];
    public HashSet<string> OpenSubjects { get; } = [];

    public Task ToStaffAsync(NotificationDraft draft, IEnumerable<long> staffIds, CancellationToken ct = default)
    {
        foreach (var id in staffIds) ToStaff.Add((draft, id));
        Open(draft);
        return Task.CompletedTask;
    }

    public Task ToUserAsync(NotificationDraft draft, long userId, CancellationToken ct = default)
    {
        ToUser.Add((draft, userId));
        Open(draft);
        return Task.CompletedTask;
    }

    public Task ToRolesAsync(NotificationDraft draft, IEnumerable<UserRole> roles, CancellationToken ct = default)
    {
        ToRoles.Add((draft, roles.ToArray()));
        Open(draft);
        return Task.CompletedTask;
    }

    public Task ResolveAsync(string subjectKey, CancellationToken ct = default)
    {
        Resolved.Add(subjectKey);
        OpenSubjects.Remove(subjectKey);
        return Task.CompletedTask;
    }

    public Task<bool> HasOpenAsync(string subjectKey, CancellationToken ct = default) =>
        Task.FromResult(OpenSubjects.Contains(subjectKey));

    private void Open(NotificationDraft draft)
    {
        if (draft.SubjectKey is { } subject) OpenSubjects.Add(subject);
    }
}
