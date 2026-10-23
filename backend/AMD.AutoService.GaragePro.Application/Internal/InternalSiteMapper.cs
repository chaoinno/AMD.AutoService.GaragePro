using System.Globalization;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Application.SelfServe.Admin;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.Internal;

/// <summary>
/// ข้อมูลดิบ → แถวของ <c>GET /api/v1/internal/sites</c> (docs/17 §8.6) · ฟังก์ชันล้วน (ไม่มี I/O ไม่มีนาฬิกา)
/// · [BIZ] สถานะการใช้งานของอู่ sp1 มาจาก <see cref="PlanPolicy"/> ตัวเดียวกับที่บังคับ API จริง (WorkspacePolicy) จึงตรงกับ
///   สิ่งที่ลูกค้าเจอเสมอ · Admin แสดงตามที่ได้ ไม่คำนวณกฎเอง (กฎหมดอายุใน Admin เองยังไม่ตรงกัน 3 แบบ docs/17 §8.1)
/// · วันเหลือ/วันสุดท้ายใช้ <see cref="PlanAccess.DaysLeft"/> และ <see cref="PackageRenewal.LastUsableDay"/> (ค่าเดียวกับ /auth/me
///   และหน้าแพ็กเกจของลูกค้า) ทั้งสองแหล่ง
/// </summary>
public static class InternalSiteMapper
{
    /// <summary>ช่วงนับ <see cref="InternalSiteDto.JobsLast30Days"/> ย้อนจากเวลาที่เรียก (ไม่ใช่วันปฏิทิน)</summary>
    public const int JobWindowDays = 30;

    public const string LegacyPlan = "legacy";

    /// <summary>
    /// null = ไม่ส่งแถวนี้: อู่ที่ยังไม่มี Branch (ค้างกลาง signup ก่อนสร้างสาขา) ไม่มีคีย์ของแถว (shardKey, branchId)
    /// · อู่ที่ purge แล้วยังมี BranchId เดิม (IDENTITY ไม่นำกลับมาใช้) จึงส่งได้พร้อม <c>usage: "purged"</c>
    /// </summary>
    public static InternalSiteDto? SelfServe(AdminWorkspaceRow row, string shardKey, PackagePaymentSummary? payments,
        SiteJobActivity? jobs, DateTime nowUtc, SelfServeOptions options)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(options);
        if (row.BranchId is not { } branchId) return null;

        return new InternalSiteDto(
            InternalSiteSources.SelfServe,
            shardKey,
            branchId,
            SelfServeText.WorkspaceCode(row.WorkspaceNo),
            row.Name,
            LocalPhone(row.Phone),
            Lower(row.Plan.ToString()),
            Lower(row.Status.ToString()),
            row.Branch is { } branch ? branch.Status == 1 : null,
            row.Branch?.StartsAtUtc ?? row.TrialStartsAt,
            row.EndsAt,
            LastUsable(row.EndsAt),
            PlanAccess.DaysLeft(row.EndsAt, nowUtc),
            SelfServeUsage(row, nowUtc, options),
            payments?.PendingCount ?? 0,
            payments?.LatestSubmittedAt,
            row.ExtensionRequestedAt,
            jobs?.JobsSince ?? 0,
            jobs?.LastJobAt,
            row.CreatedAt);
    }

    public static InternalSiteDto GaragePro(GarageProBranchFacts branch, string shardKey, SiteJobActivity? jobs,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(branch);

        return new InternalSiteDto(
            InternalSiteSources.GaragePro,
            shardKey,
            branch.Id,
            branch.Id.ToString(CultureInfo.InvariantCulture),
            branch.Name.Trim(),
            Blank(branch.Phone),
            LegacyPlan,
            WorkspaceStatus: null,
            branch.Status == 1,
            branch.StartsAtUtc,
            branch.EndsAtUtc,
            LastUsable(branch.EndsAtUtc),
            PlanAccess.DaysLeft(branch.EndsAtUtc, nowUtc),
            InternalSiteUsage.NotEnforced,
            PendingPayments: null,
            LatestPaymentAt: null,
            ExtensionRequestedAt: null,
            jobs?.JobsSince ?? 0,
            jobs?.LastJobAt,
            branch.CreatedAtUtc);
    }

    /// <summary>
    /// purged/provisioning ตามสถานะของ sp_Workspace ก่อน (ไม่มีอะไรให้ประเมิน) · อื่นๆ ถาม <see cref="PlanPolicy"/>:
    /// ล็อก (ระงับ · เลยช่วงอ่านอย่างเดียว · Branch ปิด/ไม่พบ) → locked · เลยวันหมด/ย้ายแล้ว → read_only · ไม่งั้น normal
    /// </summary>
    public static string SelfServeUsage(AdminWorkspaceRow row, DateTime nowUtc, SelfServeOptions options)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Status == WorkspaceStatus.Purged) return InternalSiteUsage.Purged;
        if (row.Status == WorkspaceStatus.Provisioning) return InternalSiteUsage.Provisioning;

        var plan = PlanPolicy.Evaluate(nowUtc, row.Branch,
            new WorkspaceSnapshot(row.Plan, row.Status, row.WorkspaceNo, row.TrialEndsAt), options);
        if (plan.IsLocked) return InternalSiteUsage.Locked;
        return plan.IsReadOnly ? InternalSiteUsage.ReadOnly : InternalSiteUsage.Normal;
    }

    /// <summary>E.164 ของ sp_Workspace (+66812345678) → เลขในประเทศ (0812345678) ให้ตรงรูปกับเบอร์ของสาขา GaragePro</summary>
    public static string? LocalPhone(string? phone)
    {
        var value = Blank(phone);
        return value is not null && value.StartsWith("+66", StringComparison.Ordinal) ? "0" + value[3..] : value;
    }

    private static DateOnly? LastUsable(DateTime? endsAtUtc) =>
        endsAtUtc is { } end ? PackageRenewal.LastUsableDay(end) : null;

    private static string Lower(string value) => value.ToLowerInvariant();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
