using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// sp_Account / sp_Workspace สำหรับการเข้าสู่ระบบ (B5) — ลงทะเบียนเฉพาะเมื่อตั้ง self-serve (RegisterAuth)
/// · [SECURITY] fail-open ของ login อยู่ที่ LoginIdentityResolver (Application ไม่มี logging) — ที่นี่จึงเขียน log
///   ก่อนโยนต่อ ให้ทีมเห็นว่าฐาน sp_ ล่มทั้งที่ลูกค้าเดิมยังเข้าระบบได้ตามปกติ · ห้าม log อีเมลเต็ม
/// </summary>
public sealed class SelfServeAccountReader(
    SelfServeDbContext db,
    TimeProvider clock,
    ILogger<SelfServeAccountReader>? logger = null) : ISelfServeAccountReader
{
    // host ที่ไม่ได้ลงทะเบียน logging (CLI/เทสต์) ยังใช้ได้ — ไม่ log แทนการ resolve ไม่ได้
    private readonly ILogger _logger = logger ?? NullLogger<SelfServeAccountReader>.Instance;

    public async Task<SelfServeLoginAccount?> FindActiveByNormalizedEmailAsync(string normalizedEmail,
        CancellationToken ct = default)
    {
        try
        {
            return await db.Accounts.AsNoTracking()
                .Where(a => a.NormalizedEmail == normalizedEmail && a.Status == SelfServeAccountStatus.Active)
                .Select(a => new SelfServeLoginAccount(
                    a.Id, a.NormalizedEmail, a.Provider, a.GoogleSubject, a.EmailVerifiedAt != null,
                    a.LoginUserName, a.UserId))
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "อ่าน sp_Account ไม่สำเร็จ login ด้วยอีเมลของอู่ที่สมัครเองจะได้ผลเป็น 'ไม่พบบัญชี' จนกว่าฐานจะกลับมา");
            throw;
        }
    }

    public async Task MarkMobileLoginSeenAsync(int branchId, CancellationToken ct = default)
    {
        try
        {
            var now = clock.GetUtcNow().UtcDateTime;
            // ครั้งแรกเท่านั้น (WHERE ... IS NULL) — ไม่ต้องอ่านก่อนเขียน และ login พร้อมกันหลายเครื่องก็ไม่ทับค่ากัน
            await db.Workspaces
                .Where(w => w.BranchId == branchId && w.MobileLoginSeenAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(w => w.MobileLoginSeenAt, now)
                    .SetProperty(w => w.UpdatedAt, now), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // best effort — checklist ต้อนรับไม่สำคัญกว่าการเข้าแอปได้
            _logger.LogWarning(ex, "บันทึก MobileLoginSeenAt ของสาขา {BranchId} ไม่สำเร็จ", branchId);
        }
    }
}
