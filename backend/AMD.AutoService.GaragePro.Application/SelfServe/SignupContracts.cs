using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

// สัญญาของ /api/v1/public/signup/* (แผน §3 "Routes ใหม่") — เว็บ (W1) เขียนตามรูปนี้ ห้ามเปลี่ยนชื่อ field
// · ทุก field ของคำขอเป็น nullable: JSON ที่ขาด field ต้องได้ SIGNUP_VALIDATION ที่บอก field ไม่ใช่ 400 ของ model binder

// ---------- คำขอ ----------

/// <param name="RequestId">client สร้างครั้งเดียวต่อการกดส่ง — ส่งซ้ำ (เน็ตหลุด) ได้ผลเดิมโดยไม่ส่งอีเมลซ้ำ</param>
/// <param name="Website">honeypot — คนจริงเว้นว่างเสมอ</param>
/// <param name="Consent">ยอมรับข้อกำหนดการใช้งาน/นโยบายความเป็นส่วนตัว (บังคับ — PDPA docs/12 §7)</param>
public sealed record EmailSignupStartRequest(
    string? RequestId,
    string? Email,
    string? Password,
    string? TurnstileToken,
    string? Website,
    bool Consent);

public sealed record EmailSignupResendRequest(string? Email);

/// <param name="Password">
/// รหัสผ่านเดียวกับที่ส่งใน email/start — [SECURITY] ผูกรหัสผ่านกับการยืนยันอีเมล: ใครก็เรียก email/start ด้วยอีเมลของคนอื่น
/// แล้วทับรหัสผ่านได้ (ยังไม่ได้พิสูจน์เจ้าของอีเมล) ถ้า verify ตรวจแค่ OTP เจ้าของอีเมลที่กรอกรหัสล่าสุดจะได้อู่ที่ล็อกอินด้วย
/// รหัสผ่านของผู้โจมตี · เว็บเก็บค่านี้ไว้ใน state ของหน้าเท่านั้น (ไม่ลง sessionStorage)
/// </param>
public sealed record EmailSignupVerifyRequest(string? Email, string? Code, string? Password);

public sealed record GoogleSignupRequest(string? IdToken, string? TurnstileToken, string? Website);

/// <param name="Size">ขนาดอู่ <c>1-3</c> · <c>4-10</c> · <c>10+</c> (ไม่บังคับ)</param>
/// <param name="SampleData">ใส่ข้อมูลตัวอย่างให้ (แนะนำ) — best effort ล้มแล้วสมัครยังสำเร็จ</param>
public sealed record CreateWorkspaceRequest(
    string? RequestId,
    string? SignupToken,
    string? GarageName,
    string? Phone,
    int? ProvinceId,
    string? Size,
    bool SampleData);

/// <summary>ข้อมูลประกอบจาก HTTP (controller เติม) — ลง sp_SignupAttempt/sp_Account เท่านั้น ไม่ใช้ตัดสินสิทธิ์</summary>
public sealed record SignupClientInfo(string? Ip, string? Country, string? UserAgent)
{
    public static readonly SignupClientInfo Empty = new(null, null, null);
}

// ---------- คำตอบ ----------

/// <param name="TurnstileSiteKey">null = ไม่แสดง widget (ถูกตัดจาก JSON)</param>
public sealed record SignupConfigDto(bool Enabled, bool GoogleEnabled, bool EmailEnabled, string? TurnstileSiteKey);

public sealed record SignupProvinceDto(int Id, string Name);

public sealed record SignupOtpSentDto(int ResendAfterSeconds);

/// <summary>token ใช้ครั้งเดียวสำหรับขั้นตั้งค่าอู่ — อายุ 15 นาที</summary>
public sealed record SignupTokenDto(string SignupToken, DateTime ExpiresAt);

/// <summary>
/// ผลการเปิดอู่ — field ทุกตัวของ <see cref="LoginResultDto"/> แบบ "แบน" (เว็บเอาไป saveSession ได้ทันที)
/// + <see cref="WorkspaceNo"/> (เช่น SP1042) + รหัสช่างตัวอย่าง (แสดงครั้งเดียว · null เมื่อไม่ได้ใส่ข้อมูลตัวอย่าง
/// หรือเป็นคำขอซ้ำ — รหัสผ่านเก็บเป็น hash จึงคืนซ้ำไม่ได้ แต่อยู่ในอีเมลต้อนรับแล้ว)
/// </summary>
public sealed record SignupWorkspaceResultDto(
    string AccessToken,
    DateTime ExpiresAt,
    AuthUserDto User,
    int BranchId,
    string BranchName,
    IReadOnlyList<BranchOptionDto> Branches,
    bool RequiresShiftSelection,
    string WorkspaceNo,
    IReadOnlyList<SampleCredential>? SampleTechnicians = null)
{
    public static SignupWorkspaceResultDto From(
        LoginResultDto login, string workspaceNo, IReadOnlyList<SampleCredential>? sampleTechnicians) =>
        new(login.AccessToken, login.ExpiresAt, login.User, login.BranchId, login.BranchName, login.Branches,
            login.RequiresShiftSelection, workspaceNo, sampleTechnicians is { Count: > 0 } ? sampleTechnicians : null);
}

/// <summary>
/// สภาพแวดล้อมของ host ที่ signup ต้องรู้ — API ลงทะเบียนจาก IHostEnvironment (SelfServeApi.Signup.cs)
/// · host อื่น (CLI/เทสต์) ไม่ลงทะเบียน → ผู้ใช้ถือว่าเป็น Production (fail-closed: Turnstile/อีเมลไม่ข้าม)
/// </summary>
public sealed record SignupHostEnvironment(bool IsProduction)
{
    public static readonly SignupHostEnvironment Production = new(true);
}

/// <summary>log ของ signup — Application ไม่มี Microsoft.Extensions.Logging จึงผ่าน abstraction นี้ (Infrastructure ห่อ ILogger)</summary>
public interface ISignupLog
{
    void Info(string message, params object?[] args);
    void Warning(Exception? exception, string message, params object?[] args);
    void Error(Exception? exception, string message, params object?[] args);
}

/// <summary>
/// ออก session ให้เจ้าของอู่หลังเปิดอู่ — แยกจาก IAuthService ตัวเต็มเพื่อให้ signup พึ่งแค่เมธอดเดียว
/// (เทสต์ไม่ต้อง implement IAuthService ทั้งก้อน และ B5 เพิ่มเมธอดใน IAuthService ได้โดย fake ของ signup ไม่พัง)
/// </summary>
public interface ISignupSessionIssuer
{
    Task<Result<LoginResultDto>> IssueAsync(string shardKey, long userId, CancellationToken ct = default);
}

/// <summary>ตัวจริง: <see cref="IAuthService.IssueSessionForUserAsync"/> (B5) — รูปเดียวกับ login</summary>
public sealed class AuthServiceSessionIssuer(IAuthService auth) : ISignupSessionIssuer
{
    public Task<Result<LoginResultDto>> IssueAsync(string shardKey, long userId, CancellationToken ct = default) =>
        auth.IssueSessionForUserAsync(shardKey, userId, ct);
}

/// <summary>ข้อมูลอ้างอิงของหน้าสมัคร (จังหวัด) — Infrastructure cache 24 ชม. · id ตรงกับ Garage db2 (synonym ใน sp1)</summary>
public interface ISignupReferenceData
{
    Task<IReadOnlyList<SignupProvinceDto>> GetProvincesAsync(CancellationToken ct = default);
}

public interface ISignupService
{
    SignupConfigDto GetConfig();
    Task<Result<IReadOnlyList<SignupProvinceDto>>> GetProvincesAsync(CancellationToken ct = default);
    Task<Result<SignupOtpSentDto>> StartEmailAsync(EmailSignupStartRequest request, SignupClientInfo client, CancellationToken ct = default);
    Task<Result<SignupOtpSentDto>> ResendEmailAsync(EmailSignupResendRequest request, SignupClientInfo client, CancellationToken ct = default);
    Task<Result<SignupTokenDto>> VerifyEmailAsync(EmailSignupVerifyRequest request, SignupClientInfo client, CancellationToken ct = default);
    Task<Result<SignupTokenDto>> GoogleAsync(GoogleSignupRequest request, SignupClientInfo client, CancellationToken ct = default);
    Task<Result<SignupWorkspaceResultDto>> CreateWorkspaceAsync(CreateWorkspaceRequest request, SignupClientInfo client, CancellationToken ct = default);
}
