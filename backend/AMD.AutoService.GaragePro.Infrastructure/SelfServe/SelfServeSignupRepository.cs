using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// sp_* ของ signup ผ่าน <see cref="SelfServeDbContext"/> — อ่านแบบ AsNoTracking ทั้งหมด และล้าง ChangeTracker หลังทุก SaveChanges
/// เพราะ context ตัวเดียวกัน (scoped) อาจถูก WorkspaceProvisioner (S2) ใช้ในคำขอเดียวกัน: entity ที่ค้าง track ไว้จะพาค่าเก่า
/// ไปเขียนทับของ S2 เมื่อใครสักคนเรียก SaveChanges ทีหลัง
/// · การเขียนเฉพาะคอลัมน์ใช้ ExecuteUpdate (คำสั่งเดียว ไม่ต้องเปิด transaction เอง — context นี้เปิด EnableRetryOnFailure)
/// </summary>
public sealed class SelfServeSignupRepository(SelfServeDbContext db) : ISelfServeSignupRepository
{
    public Task<SelfServeAccount?> FindAccountByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
        db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.NormalizedEmail == normalizedEmail, ct);

    public Task<SelfServeAccount?> FindAccountByGoogleSubjectAsync(string subject, CancellationToken ct = default) =>
        db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.GoogleSubject == subject, ct);

    public Task<SelfServeAccount?> FindAccountBySetupTokenAsync(string tokenHash, CancellationToken ct = default)
    {
        var claimedPrefix = tokenHash + ":";
        return db.Accounts.AsNoTracking().FirstOrDefaultAsync(
            a => a.SetupTokenHash == tokenHash || a.SetupTokenHash!.StartsWith(claimedPrefix), ct);
    }

    public async Task AddAccountAsync(SelfServeAccount account, CancellationToken ct = default)
    {
        db.Accounts.Add(account);
        await SaveAsync(ct);
    }

    public async Task UpdateAccountAsync(SelfServeAccount account, CancellationToken ct = default)
    {
        db.Accounts.Update(account);
        await SaveAsync(ct);
    }

    public async Task<bool> TryReplaceSetupTokenHashAsync(
        Guid accountId, string expected, string replacement, CancellationToken ct = default)
    {
        var rows = await db.Accounts
            .Where(a => a.Id == accountId && a.SetupTokenHash == expected)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.SetupTokenHash, replacement), ct);
        return rows == 1;
    }

    public async Task MarkAccountActiveAsync(
        Guid accountId, long userId, string loginUserName, DateTime now, CancellationToken ct = default)
    {
        // ไม่แตะบัญชีที่ถูกระงับ/ลบแล้ว · UserId/LoginUserName ที่ S2 ตั้งไว้แล้วคงค่าเดิม (COALESCE)
        await db.Accounts
            .Where(a => a.Id == accountId
                        && (a.Status == SelfServeAccountStatus.Pending
                            || a.Status == SelfServeAccountStatus.Verified
                            || a.Status == SelfServeAccountStatus.Active))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, SelfServeAccountStatus.Active)
                .SetProperty(a => a.UserId, a => a.UserId ?? userId)
                .SetProperty(a => a.LoginUserName, a => a.LoginUserName ?? loginUserName)
                .SetProperty(a => a.UpdatedAt, now), ct);
    }

    public Task<EmailOtp?> GetOtpAsync(string normalizedEmail, CancellationToken ct = default) =>
        db.EmailOtps.AsNoTracking().FirstOrDefaultAsync(o => o.Email == normalizedEmail, ct);

    public async Task AddOtpAsync(EmailOtp otp, CancellationToken ct = default)
    {
        db.EmailOtps.Add(otp);
        await SaveAsync(ct);
    }

    public async Task UpdateOtpAsync(EmailOtp otp, CancellationToken ct = default)
    {
        db.EmailOtps.Update(otp);
        await SaveAsync(ct);
    }

    public async Task<bool> IsDomainBlockedAsync(string domain, CancellationToken ct = default)
    {
        var candidates = DisposableDomainList.WithParents(domain);
        if (candidates.Count == 0) return false;

        if (await db.BlockedDomains.AsNoTracking().AnyAsync(d => candidates.Contains(d.Domain), ct)) return true;

        // [BIZ] ตารางว่าง = ยังไม่เคยนำเข้ารายชื่อ (S4 BlocklistRefresher) — ใช้รายชื่อที่ฝังมากับ build แทน
        // ถ้าตารางมีข้อมูลแล้วถือว่าตารางเป็นแหล่งความจริง (ทีมงานลบโดเมนออกจากตารางเพื่อปลดบล็อกได้)
        return !await db.BlockedDomains.AsNoTracking().AnyAsync(ct) && DisposableDomainList.Contains(domain);
    }

    public Task<Workspace?> FindWorkspaceAsync(Guid accountId, string signupRequestId, CancellationToken ct = default) =>
        db.Workspaces.AsNoTracking().FirstOrDefaultAsync(
            w => w.OwnerAccountId == accountId && w.SignupRequestId == signupRequestId, ct);

    public Task<bool> IsPhoneUsedByOtherAccountAsync(string phoneE164, Guid accountId, CancellationToken ct = default) =>
        db.Workspaces.AsNoTracking().AnyAsync(
            w => w.Phone == phoneE164 && (w.OwnerAccountId == null || w.OwnerAccountId != accountId), ct);

    public Task SetDuplicatePhoneFlagAsync(Guid workspaceId, CancellationToken ct = default) =>
        db.Workspaces.Where(w => w.Id == workspaceId)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.DuplicatePhoneFlag, true), ct);

    public async Task AddAttemptAsync(SignupAttempt attempt, CancellationToken ct = default)
    {
        db.SignupAttempts.Add(attempt);
        await SaveAsync(ct);
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 } sql)
        {
            throw new UniqueConstraintViolationException(ConstraintName(sql.Message), ex);
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    // ชื่อ index ไว้บอก log เท่านั้น — ไม่ใช้ตัดสินตรรกะ (บทเรียน WorkIntervalRepository)
    private static string ConstraintName(string message)
    {
        foreach (var name in new[]
                 {
                     "UX_sp_Account_NormalizedEmail", "UX_sp_Account_GoogleSubject", "UX_sp_Account_SetupTokenHash",
                     "UX_sp_Account_LoginUserName", "UX_sp_EmailOtp_Email"
                 })
            if (message.Contains(name, StringComparison.Ordinal)) return name;
        return "unknown";
    }
}
