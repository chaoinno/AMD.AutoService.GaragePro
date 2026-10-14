using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;

namespace AMD.AutoService.GaragePro.Application.Auth;

/// <summary>
/// กฎล้วน (ไม่มี I/O) ที่แปลง <see cref="WorkspacePlan"/> เป็นสิ่งที่ client/token เห็น และตัดสินว่า token เดิมยังใช้ได้ไหม
/// — แยกจาก AuthService ให้เทสต์ได้ตรงๆ และให้ filter ฝั่ง API ใช้กติกาเดียวกัน
/// </summary>
public static class PlanAccess
{
    public const string PlanChangedReason = "แพ็กเกจของอู่เปลี่ยนแล้ว กรุณาเข้าสู่ระบบใหม่";
    public const string PlanLockedReason = "ช่วงทดลองใช้สิ้นสุดแล้ว ติดต่อ " + PlanErrors.ContactLine;

    /// <summary>legacy → null (ไม่ส่งใน JSON เลย · ไม่มี claim) · อื่นๆ → DTO ที่เว็บ/แอปอ่าน</summary>
    public static WorkspacePlanDto? ToDto(WorkspacePlan plan, DateTime nowUtc)
    {
        if (plan.Kind == PlanKind.Legacy) return null;

        return new WorkspacePlanDto(
            Kind: PlanKindNames.ToJsonName(plan.Kind),
            TrialEndsAt: plan.ExpiresAt,
            DaysLeft: DaysLeft(plan.ExpiresAt, nowUtc),
            ReadOnly: plan.IsReadOnly,
            Features: PlanFeatureNames.ToJsonNames(plan.Features),
            // ไม่จำกัดทุกชนิด = ไม่ส่ง (ไม่งั้นได้ {} ว่างๆ ใน JSON เพราะค่า null ถูกตัด)
            Limits: plan.Limits == PlanLimits.Unlimited ? null : plan.Limits);
    }

    /// <summary>รหัสอู่ที่แสดงผล (SP1001) — null เมื่อเป็น legacy หรือไม่มีแถว sp_Workspace</summary>
    public static WorkspaceInfoDto? ToWorkspaceInfo(WorkspacePlan plan, string? branchName) =>
        plan.Kind == PlanKind.Legacy || plan.WorkspaceNo is not int no
            ? null
            : new WorkspaceInfoDto(WorkspaceCode(no), branchName ?? string.Empty);

    public static string WorkspaceCode(int workspaceNo) => $"SP{workspaceNo:D4}";

    /// <summary>
    /// จำนวนวันที่เหลือ นับเป็น "วันตามปฏิทินไทย" หลังวันนี้จนถึงวันสุดท้ายที่ยังใช้งานได้
    /// · วันสุดท้าย = วันที่ (เวลาไทย) ของช่วงก่อนหมดอายุหนึ่ง tick — หมดเที่ยงคืน 14/11 แปลว่าใช้ได้ถึงสิ้นวัน 13/11
    /// · 0 = วันนี้เป็นวันสุดท้าย (เว็บ/แอปแสดง "หมดอายุวันนี้") · ติดลบ = หมดอายุไปแล้วกี่วัน (client ตัดเป็น 0 เอง)
    /// · สมัคร 14/10 10:00 + 30 วัน → วันนั้นได้ 30 · วันที่ 23 ได้ 7 (ตรงกับ docs/12 §6 "วันที่ 23 แถบเปลี่ยนเป็นสีเตือน")
    ///   และไม่ลดลงระหว่างวันเหมือนการปัดเศษชั่วโมง (เว็บ daysLeftOf ใช้ค่านี้ก่อนเสมอ คำนวณเองเฉพาะเมื่อ server ไม่ส่ง)
    /// · null = ไม่มีวันหมดอายุ
    /// </summary>
    public static int? DaysLeft(DateTime? expiresAtUtc, DateTime nowUtc)
    {
        if (expiresAtUtc is not { } end) return null;

        var lastUsableDay = DateOnly.FromDateTime(BranchPackageDates.ToBranchLocal(end.AddTicks(-1)));
        var today = DateOnly.FromDateTime(BranchPackageDates.ToBranchLocal(nowUtc));
        return lastUsableDay.DayNumber - today.DayNumber;
    }

    /// <summary>403 TRIAL_EXPIRED ที่คำขอเขียนในช่วงอ่านอย่างเดียว — ข้อความแยก trial กับแพ็กเกจที่ชำระเงินแล้ว</summary>
    public static ApiError ReadOnlyError(WorkspacePlan plan, int trialDays) =>
        plan.Kind == PlanKind.Paid ? SelfServeAuthErrors.PaidPlanReadOnly() : PlanErrors.TrialExpired(trialDays);

    /// <summary>ค่า claim plan_exp (unix seconds) — ปัดลงเป็นวินาทีเหมือนที่ JWT เก็บ</summary>
    public static long? ToUnixSeconds(DateTime? utc) =>
        utc is { } v ? new DateTimeOffset(DateTime.SpecifyKind(v, DateTimeKind.Utc)).ToUnixTimeSeconds() : null;

    /// <summary>
    /// ตัดสิน token ของอู่ที่สมัครเองเทียบกับนโยบายปัจจุบัน — null = ยังใช้ได้ · มีค่า = ข้อความไทยให้เข้าสู่ระบบใหม่
    /// · legacy (นโยบายเป็น Legacy และ token ไม่มี claim หรือเป็น legacy) → null เสมอ = พฤติกรรมเดิม
    /// · ถูกล็อก (เลยช่วงอ่านอย่างเดียว / ระงับ) → <see cref="PlanLockedReason"/>
    /// · ชนิดแพ็กเกจใน claim ไม่ตรง → <see cref="PlanChangedReason"/>
    /// · สถานะ "อ่านอย่างเดียวตามเวลา" ที่ claim plan_exp บอก ไม่ตรงกับนโยบาย (ทีมงานเลื่อนวัน/ตัดวันหลังออก token)
    ///   → <see cref="PlanChangedReason"/> — เทียบที่ระดับวินาทีทั้งสองฝั่ง และเทียบเฉพาะส่วนที่ขึ้นกับเวลา:
    ///   การหมดอายุตามธรรมชาติไม่เตะใครออก (claim กับนโยบายเปลี่ยนพร้อมกัน) และการต่ออายุก่อนหมดก็ไม่เตะ
    ///   (ทั้งคู่ยังเขียนได้) — ช่างที่จำรหัสสุ่มไม่ได้จะไม่ถูกบังคับ login ใหม่โดยไม่จำเป็น
    ///   ส่วนอ่านอย่างเดียวที่ไม่ขึ้นกับเวลา (เช่นย้ายไปสาขาจริงแล้ว) ไม่อยู่ใน claim — TrialReadOnlyFilter ถามนโยบายเองทุกครั้ง
    /// </summary>
    public static string? RevocationReason(WorkspacePlan current, PlanKind? claimKind, DateTime? claimExpiresAtUtc, DateTime nowUtc)
    {
        if (current.Kind == PlanKind.Legacy && claimKind is null or PlanKind.Legacy) return null;
        if (current.IsLocked) return PlanLockedReason;
        if (claimKind != current.Kind) return PlanChangedReason;

        var claimReadOnly = ToUnixSeconds(claimExpiresAtUtc) is { } claimEnd && Seconds(nowUtc) >= claimEnd;
        var policyReadOnly = ToUnixSeconds(current.ExpiresAt) is { } policyEnd && Seconds(nowUtc) >= policyEnd;
        return claimReadOnly == policyReadOnly ? null : PlanChangedReason;
    }

    private static long Seconds(DateTime utc) => ToUnixSeconds(utc)!.Value;
}
