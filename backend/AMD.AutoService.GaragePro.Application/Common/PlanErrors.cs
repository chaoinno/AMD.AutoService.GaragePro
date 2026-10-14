using System.Globalization;
using AMD.AutoService.GaragePro.Application.Abstractions;

namespace AMD.AutoService.GaragePro.Application.Common;

/// <summary>
/// error ของแพ็กเกจ/ลิมิต (แผน §3) — <c>messageTh</c> พร้อมแสดงผล client ห้ามแปลหรือแต่งใหม่
/// · TRIAL_EXPIRED 403 · PLAN_FEATURE_LOCKED 403 · PLAN_LIMIT_REACHED 422 (<see cref="StatusCodeFor"/> ให้ controller ใช้ map)
/// · <c>Details</c> ใส่ค่าที่ client ใช้ตัดสินใจ (ชื่อฟีเจอร์/ชนิดลิมิต) เพื่อไม่ต้อง parse ข้อความไทย
/// </summary>
public static class PlanErrors
{
    public const string TrialExpiredCode = "TRIAL_EXPIRED";
    public const string FeatureLockedCode = "PLAN_FEATURE_LOCKED";
    public const string LimitReachedCode = "PLAN_LIMIT_REACHED";

    /// <summary>ช่องทางติดต่อฝ่ายขายที่ใช้ในทุกข้อความ — เปลี่ยนที่เดียว</summary>
    public const string ContactLine = "LINE @garagepro";

    /// <summary>403 — คำขอเขียนในช่วงอ่านอย่างเดียว (หลัง EndPackageDate · ยังเข้าสู่ระบบได้)</summary>
    public static ApiError TrialExpired(int trialDays = 30) => new(TrialExpiredCode,
        $"ช่วงทดลองใช้ {trialDays} วันสิ้นสุดแล้ว ดูและพิมพ์ข้อมูลได้ แต่แก้ไขไม่ได้ ติดต่อ {ContactLine} เพื่อสมัครแพ็กเกจ");

    /// <summary>403 ที่ login — เลยช่วงอ่านอย่างเดียว (+ReadOnlyDays) หรือถูกระงับ (<see cref="WorkspacePlan.IsLocked"/>)</summary>
    public static ApiError TrialLocked() => new(TrialExpiredCode,
        $"ช่วงทดลองใช้สิ้นสุดแล้วและบัญชีถูกปิดการใช้งาน ติดต่อ {ContactLine} เพื่อสมัครแพ็กเกจหรือขอเปิดใช้งานอีกครั้ง");

    /// <summary>403 — [RequireFeature] บน controller/action ที่แพ็กเกจไม่มี</summary>
    public static ApiError FeatureLocked(PlanFeature feature) => new(FeatureLockedCode,
        $"ฟีเจอร์นี้เปิดในแพ็กเกจเต็ม ติดต่อ {ContactLine}",
        Details: new
        {
            feature = PlanFeatureNames.ToJsonNames(feature),
            labelTh = PlanFeatureNames.LabelTh(feature)
        });

    /// <summary>422 — <see cref="IWorkspacePolicy.CheckLimitAsync"/> ก่อน insert พนักงาน/สินค้า/ลูกค้า/จ๊อบ</summary>
    public static ApiError LimitReached(PlanLimitKind kind, int max) => new(LimitReachedCode,
        $"ครบจำนวนที่แพ็กเกจทดลองใช้รองรับแล้ว ({PlanLimitNames.LabelTh(kind)} สูงสุด " +
        $"{max.ToString("N0", CultureInfo.InvariantCulture)}) ติดต่อ {ContactLine}",
        Details: new { kind = PlanLimitNames.ToJsonName(kind), max });

    /// <summary>HTTP status ของรหัสในชุดนี้ — null = ไม่ใช่ error ของแพ็กเกจ ให้ controller ใช้ mapping เดิมต่อ</summary>
    public static int? StatusCodeFor(string? code) => code switch
    {
        TrialExpiredCode => 403,
        FeatureLockedCode => 403,
        LimitReachedCode => 422,
        _ => null
    };
}
