using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// แพ็กเกจที่เก็บในแถว Branch ของ shard sp1 (คอลัมน์ชุดเดียวกับ legacy: StartPackageDate/PackageDaysAmout/EndPackageDate/Status)
/// · วันที่ใน record นี้แปลงเป็น UTC แล้ว (ดู <see cref="BranchPackageDates"/>) — ในตารางเก็บเป็นเวลาไทยแบบ legacy
/// · [BIZ] <see cref="EndsAtUtc"/> (EndPackageDate) เป็นตัวตัดสินตัวเดียว — ไม่คำนวณจาก Start+Days เพราะเมื่อเปลี่ยนเป็น paid
///   แบบไม่มีกำหนด (EndPackageDate = NULL) ค่า Start/Days ของช่วงทดลองยังค้างอยู่ ถ้าคำนวณจะกลายเป็นหมดอายุทันที
/// </summary>
public sealed record BranchPackage(
    int BranchId,
    int Status,
    DateTime? StartsAtUtc,
    int? PackageDays,
    DateTime? EndsAtUtc);

/// <summary>ส่วนของแถว sp_Workspace ที่ใช้ตัดสินแพ็กเกจ — null = ไม่มีแถว (สาขาที่ไม่ได้มาจากการสมัคร)</summary>
public sealed record WorkspaceSnapshot(
    WorkspacePlanKind Plan,
    WorkspaceStatus Status,
    int WorkspaceNo,
    DateTime? TrialEndsAtUtc);

/// <summary>
/// [BIZ] สัญญาเรื่องเวลาของคอลัมน์แพ็กเกจใน Branch (ทุก WP ที่เขียน/อ่านต้องใช้ตัวนี้):
/// Garage เดิมเก็บ StartPackageDate/EndPackageDate เป็น "เวลาไทย" ชนิด datetime ไม่มีโซน (ดูข้อมูลจริง db2: 2028-01-01 00:00 =
/// เที่ยงคืนเวลาไทย) — shard sp1 ใช้รูปร่างตารางเดียวกันจึงเก็บแบบเดียวกัน เพื่อให้วันนี้และวันที่ย้ายไป db2 (S5)
/// หรือวันที่บังคับแพ็กเกจกับสาขา legacy ใช้กติกาเดียวกันได้
/// · ประเทศไทยไม่มี DST → offset คงที่ +7 ไม่ต้องพึ่ง time zone database ของเครื่อง (container slim ไม่มีให้)
/// </summary>
public static class BranchPackageDates
{
    public static readonly TimeSpan ThaiOffset = TimeSpan.FromHours(7);

    /// <summary>ค่าจากคอลัมน์ Branch (เวลาไทย) → UTC</summary>
    public static DateTime ToUtc(DateTime branchLocal) =>
        DateTime.SpecifyKind(branchLocal - ThaiOffset, DateTimeKind.Utc);

    public static DateTime? ToUtc(DateTime? branchLocal) => branchLocal is { } v ? ToUtc(v) : null;

    /// <summary>UTC → ค่าที่ต้องเขียนลงคอลัมน์ Branch (เวลาไทย, Kind=Unspecified)</summary>
    public static DateTime ToBranchLocal(DateTime utc) =>
        DateTime.SpecifyKind((utc.Kind == DateTimeKind.Local ? utc.ToUniversalTime() : utc) + ThaiOffset,
            DateTimeKind.Unspecified);
}

/// <summary>
/// กฎแพ็กเกจของ shard self-serve — ฟังก์ชันล้วน (ไม่มี I/O ไม่มีนาฬิกา) ให้ WorkspacePolicy เรียกพร้อมข้อมูลที่อ่านมา
/// · trial: ปิดฟีเจอร์ขั้นสูงทั้งหมด + ลิมิตตาม SelfServe:Limits
/// · paid: ทุกฟีเจอร์ ไม่จำกัด · ไม่มีวันหมดอายุหรือยังไม่ถึง = เขียนได้
/// · ทั้ง trial และ paid: ถึง EndPackageDate → อ่านอย่างเดียว · ถึง EndPackageDate + Trial:ReadOnlyDays → ล็อก (เข้าสู่ระบบไม่ได้)
/// · ล็อกทันทีเมื่อ: แผน Suspended · workspace Provisioning/Purged · Branch.Status ≠ 1 · ไม่พบ Branch
/// </summary>
public static class PlanPolicy
{
    public static WorkspacePlan Evaluate(
        DateTime nowUtc,
        BranchPackage? branch,
        WorkspaceSnapshot? workspace,
        SelfServeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var trialLimits = options.Limits.ToPlanLimits();
        var workspaceNo = workspace?.WorkspaceNo;

        // [SECURITY] fail closed: ไม่พบสาขา = ไม่มีอะไรให้เปิด (login ของ sp1 จะไม่ผ่านอยู่แล้ว แต่ policy ต้องไม่ตอบว่าใช้ได้)
        if (branch is null)
            return new WorkspacePlan(PlanKind.Trial, null, PlanFeature.None, trialLimits,
                IsReadOnly: true, IsLocked: true, workspaceNo);

        var plan = workspace?.Plan ?? WorkspacePlanKind.Trial;

        // trial ที่ Branch ไม่มีวันหมดอายุ (ข้อมูลผิด) ใช้สำเนาใน sp_Workspace แทน · paid ไม่ใช้สำเนา (นั่นคือวันหมดทดลองเก่า)
        var expiresAt = branch.EndsAtUtc
                        ?? (plan is WorkspacePlanKind.Trial or WorkspacePlanKind.Suspended ? workspace?.TrialEndsAtUtc : null);

        if (plan == WorkspacePlanKind.Suspended)
            return new WorkspacePlan(PlanKind.Suspended, expiresAt, PlanFeature.None, trialLimits,
                IsReadOnly: true, IsLocked: true, workspaceNo);

        var administrativelyLocked =
            branch.Status != 1
            || workspace?.Status is WorkspaceStatus.Provisioning or WorkspaceStatus.Purged;

        // [ASSUME] Converted = ย้ายข้อมูลหลักไปสาขาจริงแล้ว: ยังเปิดดู/พิมพ์ของเดิมได้ (ทุกฟีเจอร์ ไม่จำกัด) แต่เขียนไม่ได้
        // เพราะงานใหม่ต้องเกิดที่สาขาปลายทาง — ล็อกเมื่อถูก purge ตาม PurgeAfter (Status=Purged)
        if (plan == WorkspacePlanKind.Converted)
            return new WorkspacePlan(PlanKind.Paid, null, PlanFeature.All, PlanLimits.Unlimited,
                IsReadOnly: true, IsLocked: administrativelyLocked, workspaceNo);

        // ไม่มีวันหมดอายุเลย (ทั้ง Branch และสำเนาใน sp_Workspace): paid = ไม่มีกำหนด (ตามสัญญา) · trial = [ASSUME] ยังไม่หมด
        // — ตัวเปิดอู่ (S2) เขียน EndPackageDate เสมอ ค่าว่างจึงเป็นข้อมูลผิดที่ทีมงานแก้ได้ด้วย spadmin extend
        var readOnly = expiresAt is { } end && nowUtc >= end;
        var lockedByTime = expiresAt is { } end2 && nowUtc >= end2.AddDays(options.Trial.ReadOnlyDays);
        var locked = administrativelyLocked || lockedByTime;

        return plan == WorkspacePlanKind.Paid
            ? new WorkspacePlan(PlanKind.Paid, expiresAt, PlanFeature.All, PlanLimits.Unlimited,
                IsReadOnly: readOnly || locked, IsLocked: locked, workspaceNo)
            : new WorkspacePlan(PlanKind.Trial, expiresAt, PlanFeature.None, trialLimits,
                IsReadOnly: readOnly || locked, IsLocked: locked, workspaceNo);
    }
}
