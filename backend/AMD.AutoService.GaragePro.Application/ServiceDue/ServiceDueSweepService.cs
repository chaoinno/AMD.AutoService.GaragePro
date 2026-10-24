using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using Microsoft.Extensions.DependencyInjection;
using AMD.AutoService.GaragePro.Application.Reports;

namespace AMD.AutoService.GaragePro.Application.ServiceDue;

/// <summary>ฐานที่ต้องไล่หา — shard key ตัวแทนของแต่ละฐาน (ฐานหลัก "db2" + ฐาน self-serve เมื่อตั้งค่า)
/// ServiceDbContext เลือกฐานตาม shard key ของ scope (ServiceDbConnectionResolver) จึงต้องไล่ทีละฐาน</summary>
public sealed record ServiceDueSweepOptions(IReadOnlyList<string> DatabaseShardKeys);

public sealed record ServiceDueSweepReport(
    int BranchesChecked, int NotificationsCreated, IReadOnlyList<string> Errors);

public interface IServiceDueSweepService
{
    Task<ServiceDueSweepReport> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// งานรายวัน "รถใกล้ครบรอบบริการ": ไล่ทุกสาขาที่มีรายการนัดครบกำหนดภายใน 7 วัน ในทุกฐาน แล้วให้ <see cref="IServiceDueNotifier"/> ทำต่อสาขา
/// · แต่ละสาขารันใน scope ของ <c>ScopedCurrentUser(shard, branch, 0, "ระบบ")</c> ผ่าน <see cref="IShardScopeFactory"/> — โค้ดนอก HTTP
///   ห้ามแตะ CurrentUser ของ JWT (ข้อมูลสาขา/shard ต้องไม่ปนกัน)
/// · idempotent ด้วย SubjectKey (ดู notifier) — รันซ้ำ/หลาย instance ได้ ไม่ต้องมีตาราง claim
/// · สาขาหนึ่งล้มต้องไม่หยุดสาขาอื่น — เก็บ error ใน report ให้ผู้เรียก log (Application ไม่มี logging)
/// </summary>
public sealed class ServiceDueSweepService(
    IShardScopeFactory scopes, ServiceDueSweepOptions options, TimeProvider clock) : IServiceDueSweepService
{
    public const string SystemActorName = "ระบบ (รถใกล้ครบรอบบริการ)";

    public async Task<ServiceDueSweepReport> RunAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddHours(7));
        var to = today.AddDays(ReportsService.ServiceDueNotifyDays);
        var errors = new List<string>();
        var branches = new List<ServiceDueBranch>();

        foreach (var databaseShard in options.DatabaseShardKeys.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var found = await scopes.RunAsAsync(
                    new ScopedCurrentUser(databaseShard, 0, 0, SystemActorName),
                    (sp, token) => sp.GetRequiredService<IReportsRepository>().GetBranchesWithServiceDueAsync(today, to, token),
                    ct);
                branches.AddRange(found);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"ไล่หาสาขาในฐานของ shard {databaseShard} ไม่สำเร็จ: {ex.Message}");
            }
        }

        var created = 0;
        foreach (var branch in branches.Where(b => b.BranchId > 0).Distinct())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                created += await scopes.RunAsAsync(
                    new ScopedCurrentUser(branch.ShardKey, branch.BranchId, 0, SystemActorName),
                    (sp, token) => sp.GetRequiredService<IServiceDueNotifier>().NotifyBranchAsync(token),
                    ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errors.Add($"แจ้งเตือนสาขา {branch.ShardKey}/{branch.BranchId} ไม่สำเร็จ: {ex.Message}");
            }
        }

        return new ServiceDueSweepReport(branches.Count, created, errors);
    }
}
