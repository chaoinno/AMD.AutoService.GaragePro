using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>
/// นโยบายแพ็กเกจจำลองสำหรับเทสต์ของ B5 — คืนแผนที่ตั้งไว้ทุกครั้ง และบันทึกว่าถูกถามอะไรบ้าง
/// (ใช้ยืนยันว่า legacy ไม่ถูกนับลิมิต/ไม่ถามซ้ำ) · ลิมิตที่ใส่ใน <see cref="ReachedLimits"/> ตอบ PLAN_LIMIT_REACHED
/// </summary>
public sealed class FakeWorkspacePolicy(WorkspacePlan? plan = null) : IWorkspacePolicy
{
    public WorkspacePlan Plan { get; set; } = plan ?? WorkspacePlan.Legacy;
    public HashSet<PlanLimitKind> ReachedLimits { get; } = [];
    public List<(string ShardKey, int BranchId)> Requests { get; } = [];
    public List<PlanLimitKind> LimitChecks { get; } = [];

    public Task<WorkspacePlan> GetAsync(string shardKey, int branchId, CancellationToken ct = default)
    {
        Requests.Add((shardKey, branchId));
        return Task.FromResult(Plan);
    }

    public Task<ApiError?> CheckLimitAsync(string shardKey, int branchId, PlanLimitKind kind,
        CancellationToken ct = default)
    {
        LimitChecks.Add(kind);
        return Task.FromResult(ReachedLimits.Contains(kind)
            ? PlanErrors.LimitReached(kind, Plan.Limits.For(kind) ?? 0)
            : null);
    }

    public static readonly PlanLimits TrialLimits = new(5, 200, 200, 100);

    public static WorkspacePlan Trial(int? workspaceNo = 1001, DateTime? expiresAt = null,
        bool readOnly = false, bool locked = false) =>
        new(PlanKind.Trial, expiresAt, PlanFeature.None, TrialLimits, readOnly, locked, workspaceNo);

    public static WorkspacePlan Paid(int? workspaceNo = 1001, DateTime? expiresAt = null, bool readOnly = false) =>
        new(PlanKind.Paid, expiresAt, PlanFeature.All, PlanLimits.Unlimited, readOnly, false, workspaceNo);
}

/// <summary>IServiceProvider ที่ตอบเฉพาะชนิดที่ใส่ไว้ (ไม่มี = null แบบ GetService ของ DI จริง)</summary>
public sealed class MapServiceProvider : IServiceProvider
{
    private readonly Dictionary<Type, object> _services = [];

    public MapServiceProvider Add<T>(T service) where T : class
    {
        _services[typeof(T)] = service;
        return this;
    }

    public object? GetService(Type serviceType) => _services.GetValueOrDefault(serviceType);
}
