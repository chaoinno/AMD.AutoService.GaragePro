using System.Diagnostics.Metrics;

namespace AMD.AutoService.GaragePro.Application.Common;

/// <summary>
/// ตัวนับสำหรับระบบ monitoring (docs/19) — label มีค่าจำกัดเท่านั้น ห้ามใส่ JWT, user ID หรือข้อความนโยบาย
/// </summary>
public static class AccessUpdateMetrics
{
    public static readonly Meter Meter = new("GaragePro.AccessUpdates", "1.0");
    public static readonly Counter<long> RevokedAccounts = Meter.CreateCounter<long>("garagepro.sessions.revoked");
    public static readonly Counter<long> SessionStoreErrors = Meter.CreateCounter<long>("garagepro.sessions.store_errors");
    public static readonly Counter<long> PolicyReads = Meter.CreateCounter<long>("garagepro.app_updates.reads");
}
