using System.Text.RegularExpressions;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;

namespace AMD.AutoService.GaragePro.Application.AppUpdates;

public sealed record AppUpdatePolicy(string Platform, bool Enabled, string LatestVersion, string MinimumVersion,
    string Message, long Revision, string StoreUrl);
public sealed record SaveAppUpdatePolicy(bool Enabled, string LatestVersion, string MinimumVersion,
    string Message, long Revision, bool StoreAvailableConfirmed = false);
public sealed record AppUpdateHistory(Guid Id, string Platform, long Revision, string EditedByShard,
    long EditedByUserId, string EditedByName, DateTime EditedAt, AppUpdatePolicy Before, AppUpdatePolicy After);

public static partial class AppVersions
{
    [GeneratedRegex(@"^(0|[1-9][0-9]{0,6})\.(0|[1-9][0-9]{0,6})\.(0|[1-9][0-9]{0,6})$")]
    private static partial Regex Pattern();
    public static Version? Parse(string? value) => value is not null && Pattern().IsMatch(value) ? Version.Parse(value) : null;
    public static bool IsPlatform(string platform) => platform is "ios" or "android";
    public static string StoreUrl(string platform) => platform == "ios"
        ? "https://apps.apple.com/th/app/servicepro/id6815111962"
        : "https://play.google.com/store/apps/details?id=net.garagepro.gpservice";
    public static AppUpdatePolicy Default(string platform) => new(platform, false, "1.4.0", "0.0.0", "มีเวอร์ชันใหม่พร้อมใช้งาน กรุณาอัปเดตแอป", 0, StoreUrl(platform));
}

public interface IAppUpdateRepository
{
    Task<AppUpdatePolicy> ReadAsync(string platform, CancellationToken ct);
    Task<bool> SaveAsync(AppUpdatePolicy before, AppUpdatePolicy after, ICurrentUser actor, CancellationToken ct);
    Task<IReadOnlyList<AppUpdateHistory>> HistoryAsync(string platform, CancellationToken ct);
}

public sealed class AppUpdateService(IAppUpdateRepository repository, ICurrentUser currentUser, ILegacyUserReader users)
{
    public Task<AppUpdatePolicy> ReadAsync(string platform, CancellationToken ct) => repository.ReadAsync(platform, ct);

    // นโยบายมีผลทุกอู่ — ต้องอ่านสถานะผู้ดูแลจากฐานเดิมสดทุกครั้ง ถูกถอดสิทธิ์แล้วต้องแก้ไม่ได้ทันที ไม่ใช่เชื่อ claim
    public async Task<bool> CanManageAsync(CancellationToken ct) => currentUser.IsAdministrator &&
        (await users.FindByIdAsync(currentUser.ShardKey, currentUser.UserId, ct))?.IsAdministrator == true;

    public async Task<Result<AppUpdatePolicy>> SaveAsync(string platform, SaveAppUpdatePolicy input, CancellationToken ct)
    {
        if (!await CanManageAsync(ct)) return Result<AppUpdatePolicy>.Fail("APP_UPDATE_FORBIDDEN", "เฉพาะผู้ดูแลระบบเท่านั้นที่จัดการเวอร์ชันแอปได้");
        if (!AppVersions.IsPlatform(platform)) return Result<AppUpdatePolicy>.Fail("APP_UPDATE_PLATFORM", "รองรับเฉพาะ iOS และ Android");
        var latest = AppVersions.Parse(input.LatestVersion);
        var minimum = AppVersions.Parse(input.MinimumVersion);
        if (latest is null || minimum is null || minimum > latest)
            return Result<AppUpdatePolicy>.Fail("APP_UPDATE_VERSION", "ระบุเวอร์ชันแบบ 1.2.3 และเวอร์ชันขั้นต่ำต้องไม่สูงกว่าเวอร์ชันล่าสุด");
        if (string.IsNullOrWhiteSpace(input.Message) || input.Message.Length > 1000 || input.Revision < 0)
            return Result<AppUpdatePolicy>.Fail("APP_UPDATE_INVALID", "ระบุข้อความไม่เกิน 1,000 ตัวอักษรและ revision ที่ถูกต้อง");
        var before = await repository.ReadAsync(platform, ct);
        if (before.Revision != input.Revision)
            return Result<AppUpdatePolicy>.Fail("APP_UPDATE_CONFLICT", "มีผู้ดูแลแก้ไขข้อมูลแล้ว กรุณาโหลดข้อมูลล่าสุด");
        if (input.Enabled && minimum > new Version(0, 0, 0) &&
            (!before.Enabled || minimum > AppVersions.Parse(before.MinimumVersion)!) && !input.StoreAvailableConfirmed)
            return Result<AppUpdatePolicy>.Fail("APP_UPDATE_CONFIRM_STORE", "ยืนยันก่อนว่าเวอร์ชันขั้นต่ำดาวน์โหลดจาก Store ได้แล้ว");
        var after = new AppUpdatePolicy(platform, input.Enabled, input.LatestVersion, input.MinimumVersion,
            input.Message.Trim(), checked(before.Revision + 1), AppVersions.StoreUrl(platform));
        return await repository.SaveAsync(before, after, currentUser, ct)
            ? Result<AppUpdatePolicy>.Ok(after)
            : Result<AppUpdatePolicy>.Fail("APP_UPDATE_CONFLICT", "มีผู้ดูแลแก้ไขข้อมูลแล้ว กรุณาโหลดข้อมูลล่าสุด");
    }

    public Task<IReadOnlyList<AppUpdateHistory>> HistoryAsync(string platform, CancellationToken ct) => repository.HistoryAsync(platform, ct);
}
