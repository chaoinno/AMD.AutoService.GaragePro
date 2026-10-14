using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// สมัครทดลองใช้ด้วยตัวเอง (S1 · แผน §4 Wave 3 · docs/12 §3–§4, §7) — ทุก endpoint ใต้ <c>/api/v1/public/signup</c>
///
/// ลำดับ: (อีเมล) start → OTP → verify → signupToken · (Google) ID token → signupToken · แล้ว workspace → เปิดอู่ + session
///
/// · [SECURITY] กัน enumeration: email/start/resend ตอบเหมือนกันทุกกรณี — อีเมลที่มีบัญชีแล้วได้อีเมล "มีบัญชีอยู่แล้ว" แทน OTP
///   (สร้างรหัสที่ไม่ได้ส่งเก็บไว้ด้วย verify จึงตอบ "รหัสไม่ถูกต้อง เหลืออีก N ครั้ง" เหมือนอีเมลใหม่ทุกตัวอักษร)
///   · Google เท่านั้นที่ตอบ SIGNUP_ACCOUNT_EXISTS ได้ เพราะ Google พิสูจน์ความเป็นเจ้าของอีเมลแล้ว
/// · [SECURITY] signupToken: 32 ไบต์สุ่ม เก็บแค่ SHA-256 (hex) ใน sp_Account.SetupTokenHash · อายุ 15 นาที · ใช้เปิดอู่ได้ครั้งเดียว:
///   ตอนเปิดอู่ "จอง" token ให้ requestId นั้นแบบ compare-and-swap (<c>{hash}</c> → <c>{hash}:{requestId}</c>) — คำขอ requestId อื่นใช้ไม่ได้
///   (กันกดซ้ำสองครั้งได้สองอู่) · requestId เดิมส่งซ้ำ = ทำต่อจากที่ค้าง/ได้ผลเดิม (ไม่เปิดอู่ใหม่) · จองแล้วแต่เปิดอู่ล้ม
///   (ProvisionError) ให้ requestId ใหม่ (ผู้ใช้แก้ฟอร์มแล้วกดใหม่) จองต่อได้
/// · [SECURITY] email/verify ต้องได้ทั้ง OTP และรหัสผ่านเดียวกับ email/start — start ทับรหัสผ่านของแถว Pending ได้โดยไม่ต้องพิสูจน์
///   เจ้าของอีเมล ถ้า verify ดูแค่ OTP คนที่ start ทับระหว่างเจ้าของรอรหัสจะได้ผูกรหัสผ่านของตัวเองกับอู่ของเจ้าของอีเมล
/// · ทุกผลลัพธ์ (รวมบอท/ถูกปฏิเสธ) ลง sp_SignupAttempt — ล้มแค่ log ไม่ทำให้คำขอล้ม
/// · ขั้นหลังเปิดอู่ (ข้อมูลตัวอย่าง · LINE ทีมขาย · อีเมลต้อนรับ) เป็น best effort ล้มแล้วการสมัครยังสำเร็จ
/// </summary>
public sealed class SignupService(
    ISelfServeSignupRepository repository,
    IPasswordHasher hasher,
    IEmailSender email,
    ITurnstileVerifier turnstile,
    IEmailDomainChecker domains,
    IGoogleIdTokenValidator google,
    ISelfServeSalesNotifier notifier,
    ISignupSessionIssuer sessions,
    ISignupReferenceData referenceData,
    SelfServeOptions options,
    SignupStash stash,
    ISignupLog log,
    TimeProvider clock,
    IWorkspaceProvisioner? provisioner = null,
    IStarterDataService? starterData = null,
    SignupHostEnvironment? host = null) : ISignupService
{
    // ---------- กติกา (แผน S1 · docs/12 §7) ----------
    public const int OtpLength = 6;
    public const int OtpMinutes = 10;
    public const int OtpMaxAttempts = 5;
    public const int ResendAfterSeconds = 60;
    public const int MaxSendsPerWindow = 3;
    public static readonly TimeSpan SendWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan SignupTokenLifetime = TimeSpan.FromMinutes(15);
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int GarageNameMinLength = 2;
    public const int GarageNameMaxLength = 100;
    public const int OwnerNameMinLength = 2;
    public const int OwnerNameMaxLength = 100;
    public static readonly IReadOnlyList<string> GarageSizes = ["1-3", "4-10", "10+"];

    public const string StepEmailStart = "email.start";
    public const string StepEmailResend = "email.resend";
    public const string StepEmailVerify = "email.verify";
    public const string StepGoogle = "google";
    public const string StepWorkspace = "workspace";

    public const string OutcomeOk = "ok";
    public const string OutcomeHoneypot = "honeypot";
    public const string OutcomeAccountExists = "account-exists";
    public const string OutcomeReplay = "replay";
    public const string OutcomeNoAccount = "no-account";

    private static readonly TimeSpan StashLifetime = TimeSpan.FromMinutes(30);
    private const char ClaimSeparator = ':';

    private readonly SignupHostEnvironment _host = host ?? SignupHostEnvironment.Production;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>อีเมลส่งได้จริง หรือเป็นเครื่อง dev (OTP ลง log) — production ที่ยังไม่มีผู้ให้บริการอีเมลต้องปิดทางอีเมล</summary>
    private bool EmailDeliveryAvailable => email.IsConfigured || !_host.IsProduction;

    public SignupConfigDto GetConfig()
    {
        var enabled = options.Enabled;
        var siteKey = options.Turnstile.SiteKey?.Trim();
        return new SignupConfigDto(
            enabled,
            enabled && !string.IsNullOrWhiteSpace(options.Google.ClientId),
            enabled && EmailDeliveryAvailable,
            string.IsNullOrEmpty(siteKey) ? null : siteKey);
    }

    public async Task<Result<IReadOnlyList<SignupProvinceDto>>> GetProvincesAsync(CancellationToken ct = default)
    {
        if (!options.Enabled) return Result<IReadOnlyList<SignupProvinceDto>>.Fail(SelfServeErrors.SignupDisabled());
        try
        {
            return Result<IReadOnlyList<SignupProvinceDto>>.Ok(await referenceData.GetProvincesAsync(ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.Warning(ex, "signup: อ่านรายชื่อจังหวัดไม่ได้");
            return Result<IReadOnlyList<SignupProvinceDto>>.Fail(SignupErrors.LookupUnavailable());
        }
    }

    // =====================================================================
    // email/start
    // =====================================================================

    public async Task<Result<SignupOtpSentDto>> StartEmailAsync(
        EmailSignupStartRequest request, SignupClientInfo client, CancellationToken ct = default)
    {
        const string step = StepEmailStart;
        if (!options.Enabled || !EmailDeliveryAvailable)
            return await FailAsync<SignupOtpSentDto>(step, SelfServeErrors.SignupDisabled(), client, null, null, ct);

        // [SECURITY] บอทที่กรอก honeypot ได้คำตอบ "สำเร็จ" เหมือนคนจริง — ไม่บอกใบ้ว่าถูกกรอง (แบบฟอร์มติดต่อ)
        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            await RecordAsync(step, OutcomeHoneypot, client, null, null, null, ct);
            return Result<SignupOtpSentDto>.Ok(new SignupOtpSentDto(ResendAfterSeconds));
        }

        var hasEmail = SignupEmail.TryParse(request.Email, out var normalized, out var display, out var domain);
        var requestId = NormalizeRequestId(request.RequestId);

        // ส่งคำขอเดิมซ้ำ (เน็ตหลุดหลังเซิร์ฟเวอร์ส่งอีเมลไปแล้ว) — ต้องตรวจก่อน Turnstile เพราะ token ของ Cloudflare ใช้ได้ครั้งเดียว
        if (hasEmail && requestId is not null && stash.TryGet(StartKey(normalized, requestId), out var sentAtText)
            && long.TryParse(sentAtText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sentAtTicks))
        {
            var remaining = new DateTime(sentAtTicks, DateTimeKind.Utc).AddSeconds(ResendAfterSeconds) - Now;
            return Result<SignupOtpSentDto>.Ok(new SignupOtpSentDto(Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds))));
        }

        if (!await turnstile.VerifyAsync(request.TurnstileToken, client.Ip, ct))
            return await FailAsync<SignupOtpSentDto>(step, SelfServeErrors.SignupBotCheckFailed(), client,
                hasEmail ? domain : null, null, ct);

        if (!hasEmail)
            return await FailAsync<SignupOtpSentDto>(step,
                SelfServeErrors.SignupValidation("กรุณากรอกอีเมลให้ถูกต้อง", "email"), client, null, null, ct);

        var passwordError = ValidatePassword(request.Password);
        if (passwordError is not null)
            return await FailAsync<SignupOtpSentDto>(step, passwordError, client, domain, null, ct);

        if (!request.Consent)
            return await FailAsync<SignupOtpSentDto>(step, SelfServeErrors.SignupValidation(
                "กรุณายอมรับข้อกำหนดการใช้งานและนโยบายความเป็นส่วนตัว", "consent"), client, domain, null, ct);

        var domainError = await CheckDomainAsync(domain, ct);
        if (domainError is not null)
            return await FailAsync<SignupOtpSentDto>(step, domainError, client, domain, null, ct);

        var otp = await repository.GetOtpAsync(normalized, ct);
        var throttle = Throttle(otp);
        if (throttle is not null)
            return await FailAsync<SignupOtpSentDto>(step, throttle, client, domain, null, ct);

        var account = await repository.FindAccountByEmailAsync(normalized, ct);
        var taken = account is not null && IsTaken(account.Status);
        if (!taken)
        {
            var upserted = await UpsertPendingAccountAsync(account, normalized, display, request.Password!, client, ct);
            account = upserted;
            taken = IsTaken(upserted.Status);
        }

        var sent = await IssueCodeAndSendAsync(otp, normalized, display, taken, ct);
        if (sent is not null)
            return await FailAsync<SignupOtpSentDto>(step, sent, client, domain, account?.Id, ct);

        if (requestId is not null)
            stash.Set(StartKey(normalized, requestId), Now.Ticks.ToString(CultureInfo.InvariantCulture), TimeSpan.FromMinutes(OtpMinutes));

        await RecordAsync(step, taken ? OutcomeAccountExists : OutcomeOk, client, domain, account?.Id, null, ct);
        return Result<SignupOtpSentDto>.Ok(new SignupOtpSentDto(ResendAfterSeconds));
    }

    // =====================================================================
    // email/resend
    // =====================================================================

    public async Task<Result<SignupOtpSentDto>> ResendEmailAsync(
        EmailSignupResendRequest request, SignupClientInfo client, CancellationToken ct = default)
    {
        const string step = StepEmailResend;
        if (!options.Enabled || !EmailDeliveryAvailable)
            return await FailAsync<SignupOtpSentDto>(step, SelfServeErrors.SignupDisabled(), client, null, null, ct);

        if (!SignupEmail.TryParse(request.Email, out var normalized, out var display, out var domain))
            return await FailAsync<SignupOtpSentDto>(step,
                SelfServeErrors.SignupValidation("กรุณากรอกอีเมลให้ถูกต้อง", "email"), client, null, null, ct);

        var account = await repository.FindAccountByEmailAsync(normalized, ct);
        if (account is null)
        {
            // ไม่เคยเริ่มสมัคร — ตอบเหมือนส่งแล้ว (ไม่สร้างแถวให้อีเมลที่ใครก็พิมพ์มาได้ · เพดานต่อ IP อยู่ที่ rate limiter)
            await RecordAsync(step, OutcomeNoAccount, client, domain, null, null, ct);
            return Result<SignupOtpSentDto>.Ok(new SignupOtpSentDto(ResendAfterSeconds));
        }

        var otp = await repository.GetOtpAsync(normalized, ct);
        var throttle = Throttle(otp);
        if (throttle is not null)
            return await FailAsync<SignupOtpSentDto>(step, throttle, client, domain, account.Id, ct);

        var taken = IsTaken(account.Status);
        var sent = await IssueCodeAndSendAsync(otp, normalized, account.Email ?? display, taken, ct);
        if (sent is not null)
            return await FailAsync<SignupOtpSentDto>(step, sent, client, domain, account.Id, ct);

        await RecordAsync(step, taken ? OutcomeAccountExists : OutcomeOk, client, domain, account.Id, null, ct);
        return Result<SignupOtpSentDto>.Ok(new SignupOtpSentDto(ResendAfterSeconds));
    }

    // =====================================================================
    // email/verify
    // =====================================================================

    public async Task<Result<SignupTokenDto>> VerifyEmailAsync(
        EmailSignupVerifyRequest request, SignupClientInfo client, CancellationToken ct = default)
    {
        const string step = StepEmailVerify;
        if (!options.Enabled)
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupDisabled(), client, null, null, ct);

        if (!SignupEmail.TryParse(request.Email, out var normalized, out _, out var domain))
            return await FailAsync<SignupTokenDto>(step,
                SelfServeErrors.SignupValidation("กรุณากรอกอีเมลให้ถูกต้อง", "email"), client, null, null, ct);

        var code = new string((request.Code ?? "").Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());
        if (code.Length != OtpLength || !code.All(char.IsAsciiDigit))
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupValidation(
                $"กรุณากรอกรหัสยืนยัน {OtpLength} หลักจากอีเมล", "code"), client, domain, null, ct);

        // ตรวจแค่ว่ามีค่า (ไม่ใช้กติกาความยาว/ตัวอักษรของ email/start) — ค่าที่ไม่ตรงกฎย่อมไม่ตรงกับ hash อยู่แล้ว
        var password = request.Password;
        if (string.IsNullOrEmpty(password) || password.Length > PasswordMaxLength)
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupValidation(
                "กรุณากรอกรหัสผ่านที่ตั้งไว้ตอนเริ่มสมัคร", "password"), client, domain, null, ct);

        var otp = await repository.GetOtpAsync(normalized, ct);
        if (otp is null)
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupOtpInvalid(), client, domain, null, ct);
        if (otp.Attempts >= OtpMaxAttempts)
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupOtpLocked(), client, domain, null, ct);
        if (otp.ConsumedAt is not null || Now >= otp.ExpiresAt)
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupOtpExpired(), client, domain, null, ct);

        var account = await repository.FindAccountByEmailAsync(normalized, ct);
        // [SECURITY] รหัสของอีเมลที่มีบัญชีแล้วไม่เคยถูกส่ง — แม้เดาถูกก็นับเป็นรหัสผิด (ตอบแบบเดียวกับอีเมลใหม่)
        var canVerify = account is { Status: SelfServeAccountStatus.Pending or SelfServeAccountStatus.Verified };
        // [SECURITY] รับรหัส OTP ได้ต่อเมื่อรหัสผ่านตรงกับที่ตั้งตอน email/start ด้วย — กันคนที่ start ด้วยอีเมลคนอื่น
        // แล้วทับรหัสผ่าน (เจ้าของอีเมลกรอกรหัสล่าสุดแล้วได้อู่ที่ล็อกอินด้วยรหัสผ่านของผู้โจมตี) · ลองผิดนับรวมกับรหัสผิด
        // · ตรวจ OTP ก่อนรหัสผ่านโดยตั้งใจ: คนที่ไม่มีรหัสจากอีเมลเห็นแค่ "รหัสไม่ถูกต้อง" เสมอ — ถ้าตรวจรหัสผ่านก่อน
        //   คนนอกจะแยกได้ว่าอีเมลไหนมีบัญชีแล้ว (บัญชีที่มีอยู่ไม่เก็บรหัสผ่านจาก start จึงตอบต่างจากอีเมลใหม่)
        // · Google (ไม่มีรหัสผ่าน) = ไม่ตรงเสมอ — ทางนี้เปิดอู่ด้วยอีเมลไม่ได้จนกว่าจะเริ่มสมัครด้วยอีเมลใหม่
        var codeMatches = canVerify && hasher.Verify(otp.CodeHash, code);
        var passwordMatches = codeMatches
            && account!.PasswordHash is { Length: > 0 } stored && hasher.Verify(stored, password);
        if (!passwordMatches)
        {
            otp.Attempts++;
            await repository.UpdateOtpAsync(otp, ct);
            var left = OtpMaxAttempts - otp.Attempts;
            return await FailAsync<SignupTokenDto>(step,
                left <= 0 ? SelfServeErrors.SignupOtpLocked()
                : codeMatches ? SignupErrors.PasswordMismatch(left)
                : SelfServeErrors.SignupOtpInvalid(left),
                client, domain, account?.Id, ct);
        }

        otp.ConsumedAt = Now;
        await repository.UpdateOtpAsync(otp, ct);

        var token = NewSignupToken();
        account!.EmailVerifiedAt = Now;
        account.Status = SelfServeAccountStatus.Verified;
        account.SetupTokenHash = HashToken(token);
        account.SetupTokenExpiresAt = Now + SignupTokenLifetime;
        account.UpdatedAt = Now;
        await repository.UpdateAccountAsync(account, ct);

        await RecordAsync(step, OutcomeOk, client, domain, account.Id, null, ct);
        return Result<SignupTokenDto>.Ok(new SignupTokenDto(token, account.SetupTokenExpiresAt.Value));
    }

    // =====================================================================
    // google
    // =====================================================================

    public async Task<Result<SignupTokenDto>> GoogleAsync(
        GoogleSignupRequest request, SignupClientInfo client, CancellationToken ct = default)
    {
        const string step = StepGoogle;
        if (!options.Enabled)
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupDisabled(), client, null, null, ct);

        if (!string.IsNullOrWhiteSpace(request.Website))
        {
            // token หลอก — ขั้นเปิดอู่จะตอบ SIGNUP_TOKEN_INVALID เหมือน token หมดอายุทั่วไป
            await RecordAsync(step, OutcomeHoneypot, client, null, null, null, ct);
            return Result<SignupTokenDto>.Ok(new SignupTokenDto(NewSignupToken(), Now + SignupTokenLifetime));
        }

        if (string.IsNullOrWhiteSpace(options.Google.ClientId))
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupGoogleInvalid(), client, null, null, ct,
                "google-not-configured");

        if (!await turnstile.VerifyAsync(request.TurnstileToken, client.Ip, ct))
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupBotCheckFailed(), client, null, null, ct);

        var identity = string.IsNullOrWhiteSpace(request.IdToken) ? null : await google.ValidateAsync(request.IdToken, ct);
        if (identity is null)
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupGoogleInvalid(), client, null, null, ct, "id-token");
        if (!identity.EmailVerified)
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupGoogleInvalid(), client,
                SignupEmail.DomainOf(EmailNormalizer.Normalize(identity.Email)), null, ct, "email-not-verified");
        if (!SignupEmail.TryParse(identity.Email, out var normalized, out var display, out var domain))
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupGoogleInvalid(), client, null, null, ct, "email-format");

        // Google ยืนยันอีเมลแล้ว — ไม่ต้องตรวจ DNS แต่ยังกันโดเมนอีเมลชั่วคราว (บัญชี Google สร้างจากอีเมลภายนอกได้)
        if (await repository.IsDomainBlockedAsync(domain, ct))
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupEmailDisposable(), client, domain, null, ct);

        var account = await repository.FindAccountByGoogleSubjectAsync(identity.Subject, ct)
                      ?? await repository.FindAccountByEmailAsync(normalized, ct);
        if (account is not null && IsTaken(account.Status))
            return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupAccountExists(), client, domain, account.Id, ct);

        var token = NewSignupToken();
        if (account is null)
        {
            account = NewAccount(normalized, display, client);
            ApplyGoogle(account, identity, token, normalized, display);
            try
            {
                await repository.AddAccountAsync(account, ct);
            }
            catch (UniqueConstraintViolationException)
            {
                // ชนกับคำขอที่สร้างบัญชีอีเมลเดียวกันพร้อมกัน — อ่านใหม่แล้วตัดสินตามสถานะจริง
                var existing = await repository.FindAccountByEmailAsync(normalized, ct);
                if (existing is null || IsTaken(existing.Status))
                    return await FailAsync<SignupTokenDto>(step, SelfServeErrors.SignupAccountExists(), client, domain,
                        existing?.Id, ct);
                account = existing;
                ApplyGoogle(account, identity, token, normalized, display);
                await repository.UpdateAccountAsync(account, ct);
            }
        }
        else
        {
            ApplyGoogle(account, identity, token, normalized, display);
            await repository.UpdateAccountAsync(account, ct);
        }

        stash.Set(OwnerNameKey(account.Id), $"{identity.GivenName}\u001F{identity.FamilyName}", StashLifetime);

        await RecordAsync(step, OutcomeOk, client, domain, account.Id, null, ct);
        return Result<SignupTokenDto>.Ok(new SignupTokenDto(token, account.SetupTokenExpiresAt!.Value));
    }

    // =====================================================================
    // workspace
    // =====================================================================

    public async Task<Result<SignupWorkspaceResultDto>> CreateWorkspaceAsync(
        CreateWorkspaceRequest request, SignupClientInfo client, CancellationToken ct = default)
    {
        const string step = StepWorkspace;
        if (!options.Enabled)
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupDisabled(), client, null, null, ct);

        var requestId = NormalizeRequestId(request.RequestId);
        if (requestId is null)
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupValidation(
                "คำขอไม่ถูกต้อง กรุณาโหลดหน้าใหม่แล้วลองอีกครั้ง", "requestId"), client, null, null, ct);

        var garageName = OneLine(request.GarageName);
        if (garageName.Length < GarageNameMinLength)
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupValidation(
                "กรุณากรอกชื่ออู่", "garageName"), client, null, null, ct);
        if (garageName.Length > GarageNameMaxLength)
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupValidation(
                $"ชื่ออู่ยาวเกิน {GarageNameMaxLength} ตัวอักษร", "garageName"), client, null, null, ct);

        if (!ThaiPhone.TryNormalize(request.Phone, out var phone))
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupValidation(
                "กรุณากรอกเบอร์โทรไทยให้ถูกต้อง เช่น 081-234-5678", "phone"), client, null, null, ct);

        var size = string.IsNullOrWhiteSpace(request.Size) ? null : request.Size.Trim();
        if (size is not null && !GarageSizes.Contains(size))
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupValidation(
                "ขนาดอู่ไม่ถูกต้อง", "size"), client, null, null, ct);

        var ownerName = OneLine(request.OwnerName);
        if (ownerName.Length is > 0 and < OwnerNameMinLength)
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupValidation(
                "กรุณากรอกชื่อ-นามสกุลของคุณ", "ownerName"), client, null, null, ct);
        if (ownerName.Length > OwnerNameMaxLength)
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupValidation(
                $"ชื่อ-นามสกุลยาวเกิน {OwnerNameMaxLength} ตัวอักษร", "ownerName"), client, null, null, ct);

        var (provinceOk, provinceName) = await ResolveProvinceAsync(request.ProvinceId, ct);
        if (!provinceOk)
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupValidation(
                "กรุณาเลือกจังหวัดจากรายการ", "provinceId"), client, null, null, ct);

        // ---------- token ----------
        var rawToken = request.SignupToken?.Trim();
        if (!IsTokenShape(rawToken))
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupTokenInvalid(), client, null, null, ct);

        var tokenHash = HashToken(rawToken!);
        var account = await repository.FindAccountBySetupTokenAsync(tokenHash, ct);
        if (account is null
            || account.SetupTokenExpiresAt is not { } tokenExpiresAt || tokenExpiresAt <= Now
            || account.EmailVerifiedAt is null
            || account.Status is not (SelfServeAccountStatus.Verified or SelfServeAccountStatus.Active))
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupTokenInvalid(), client,
                SignupEmail.DomainOf(account?.NormalizedEmail), account?.Id, ct);

        var domain = SignupEmail.DomainOf(account.NormalizedEmail);
        if (!await ClaimTokenAsync(account, tokenHash, requestId, ct))
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupTokenInvalid(), client,
                domain, account.Id, ct, "token-claimed");

        // ---------- คำขอซ้ำหลังสำเร็จ: ออก session ใหม่ ไม่เปิดอู่ซ้ำ ----------
        var existing = await repository.FindWorkspaceAsync(account.Id, requestId, ct);
        if (existing is { Status: WorkspaceStatus.Active or WorkspaceStatus.Expired } && account.UserId is long knownUserId)
        {
            var replay = await IssueSessionAsync(knownUserId, ct);
            if (!replay.Success)
                return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupProvisionFailed(), client,
                    domain, account.Id, ct, replay.Error?.Code);

            await RecordAsync(step, OutcomeReplay, client, domain, account.Id, null, ct);
            return Result<SignupWorkspaceResultDto>.Ok(SignupWorkspaceResultDto.From(
                replay.Data!, SignupMessages.WorkspaceNoText(existing.WorkspaceNo), null));
        }

        if (account.Status == SelfServeAccountStatus.Active)
            // บัญชีนี้มีอู่แล้ว (จาก requestId อื่น) แต่ token ยังถูกจองให้ requestId นี้ — ไม่เปิดอู่ที่สอง
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupTokenInvalid(), client,
                domain, account.Id, ct, "account-active");

        if (provisioner is null)
        {
            log.Error(null, "signup: ไม่มี IWorkspaceProvisioner ลงทะเบียนไว้ เปิดอู่ไม่ได้");
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupProvisionFailed(), client,
                domain, account.Id, ct, "no-provisioner");
        }

        // [BIZ] เบอร์ซ้ำกับอู่ของบัญชีอื่น = ติดธงให้ทีมขายเห็น ไม่บล็อก (docs/12 §7 ชั้น 5)
        var duplicatePhone = await repository.IsPhoneUsedByOtherAccountAsync(phone, account.Id, ct);
        var viaGoogle = account.Provider == SelfServeAccountProvider.Google;
        var ownerEmail = account.Email ?? account.NormalizedEmail;
        var (firstName, lastName) = OwnerName(account, ownerEmail, ownerName);
        // Google ไม่มีรหัสผ่าน — เก็บ hash ของค่าสุ่มที่ไม่มีใครรู้ ([User].Password ห้ามว่าง) · เข้าสู่ระบบผ่าน /auth/google
        var storedPassword = account.PasswordHash ?? hasher.Hash(RandomSecret(24));

        Result<ProvisionResult> provisioned;
        try
        {
            provisioned = await provisioner.ProvisionAsync(new ProvisionRequest(
                account.Id, requestId, garageName, phone, request.ProvinceId, size,
                firstName, lastName, ownerEmail, storedPassword, request.SampleData), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Error(ex, "signup: เปิดอู่ล้ม (account {AccountId}, request {RequestId})", account.Id, requestId);
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupProvisionFailed(), client,
                domain, account.Id, ct, ex.GetType().Name);
        }

        if (!provisioned.Success)
        {
            log.Warning(null, "signup: เปิดอู่ไม่สำเร็จ {Code} (account {AccountId}, request {RequestId})",
                provisioned.Error?.Code, account.Id, requestId);
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupProvisionFailed(), client,
                domain, account.Id, ct, provisioned.Error?.Code);
        }

        var result = provisioned.Data!;
        await repository.MarkAccountActiveAsync(account.Id, result.OwnerUserId, result.OwnerUserName, Now, ct);
        if (duplicatePhone)
            await TryAsync(() => repository.SetDuplicatePhoneFlagAsync(result.WorkspaceId, ct), "ติดธงเบอร์ซ้ำ", result.WorkspaceId);

        var workspaceNo = SignupMessages.WorkspaceNoText(result.WorkspaceNo);
        var technicians = request.SampleData ? await LoadSampleDataAsync(result.WorkspaceId, ct) : [];

        await NotifySalesAsync(result.WorkspaceId, new SignupSalesInfo(
            garageName, workspaceNo, phone, provinceName, size, ownerEmail, viaGoogle, request.SampleData, duplicatePhone), ct);

        var workspace = await TryGetAsync(() => repository.FindWorkspaceAsync(account.Id, requestId, ct));
        // [แก้ตอนรวมงาน] ใช้แม่แบบอีเมลชุดเดียวของ S4 (SelfServeEmails) — ข้อความ/HTML/escape ตรงกันทุกฉบับ
        var trialEndsAt = workspace?.TrialEndsAt ?? Now.AddDays(options.Trial.Days);
        await TryAsync(() => email.SendAsync(SelfServeEmails.Welcome(new WelcomeEmail(
            garageName, result.WorkspaceNo, ownerEmail, viaGoogle, trialEndsAt, options.PublicBaseUrl, technicians)).To(ownerEmail), ct),
            "ส่งอีเมลต้อนรับ", result.WorkspaceId);

        var session = await IssueSessionAsync(result.OwnerUserId, ct);
        if (!session.Success)
        {
            log.Error(null, "signup: เปิดอู่ {WorkspaceNo} แล้วแต่ออก session ไม่ได้ ({Code})", workspaceNo, session.Error?.Code);
            return await FailAsync<SignupWorkspaceResultDto>(step, SelfServeErrors.SignupProvisionFailed(), client,
                domain, account.Id, ct, "session:" + session.Error?.Code);
        }

        await RecordAsync(step, OutcomeOk, client, domain, account.Id, workspaceNo, ct);
        return Result<SignupWorkspaceResultDto>.Ok(SignupWorkspaceResultDto.From(session.Data!, workspaceNo, technicians));
    }

    // =====================================================================
    // ขั้นย่อย
    // =====================================================================

    private Task<Result<Dtos.LoginResultDto>> IssueSessionAsync(long userId, CancellationToken ct) =>
        sessions.IssueAsync(options.ShardKey, userId, ct);

    /// <summary>
    /// จอง token ให้ requestId — true เมื่อ token เป็นของคำขอนี้ (จองได้ หรือจองไว้แล้วโดยคำขอเดิม)
    /// · token ที่ถูกจองโดย requestId อื่นจองต่อได้เฉพาะเมื่อคำขอนั้นยังไม่ได้สร้างอู่ หรือเปิดอู่ล้มแล้ว (ProvisionError)
    /// </summary>
    private async Task<bool> ClaimTokenAsync(SelfServeAccount account, string tokenHash, string requestId, CancellationToken ct)
    {
        var mine = $"{tokenHash}{ClaimSeparator}{requestId}";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var stored = account.SetupTokenHash ?? "";
            if (stored == mine) return true;

            if (stored == tokenHash)
            {
                if (await repository.TryReplaceSetupTokenHashAsync(account.Id, tokenHash, mine, ct)) return true;
            }
            else if (stored.StartsWith(tokenHash + ClaimSeparator, StringComparison.Ordinal))
            {
                var claimedBy = stored[(tokenHash.Length + 1)..];
                var previous = await repository.FindWorkspaceAsync(account.Id, claimedBy, ct);
                var abandoned = previous is null
                                || (previous.Status == WorkspaceStatus.Provisioning && previous.ProvisionError is not null);
                if (!abandoned) return false;
                if (await repository.TryReplaceSetupTokenHashAsync(account.Id, stored, mine, ct)) return true;
            }
            else
            {
                return false;
            }

            // แพ้คำขอที่จองพร้อมกัน — อ่านค่าล่าสุดแล้วตัดสินอีกรอบ
            var reloaded = await repository.FindAccountBySetupTokenAsync(tokenHash, ct);
            if (reloaded is null) return false;
            account = reloaded;
        }

        return false;
    }

    private async Task<SelfServeAccount> UpsertPendingAccountAsync(
        SelfServeAccount? account, string normalized, string display, string password, SignupClientInfo client,
        CancellationToken ct)
    {
        var passwordHash = hasher.Hash(password);
        if (account is null)
        {
            var created = NewAccount(normalized, display, client);
            created.PasswordHash = passwordHash;
            try
            {
                await repository.AddAccountAsync(created, ct);
                return created;
            }
            catch (UniqueConstraintViolationException)
            {
                // อีกคำขอสร้างแถวของอีเมลนี้ไปก่อน — ทำต่อบนแถวนั้น
                account = await repository.FindAccountByEmailAsync(normalized, ct)
                          ?? throw new InvalidOperationException("sp_Account ชน unique แต่หาแถวเดิมไม่เจอ");
                if (IsTaken(account.Status)) return account;
            }
        }

        // [SECURITY] เริ่มใหม่ = ต้องยืนยันอีเมลใหม่ · token เดิม (ถ้ามี) ใช้ไม่ได้อีก · รหัสผ่านเป็นของคำขอล่าสุด
        // (verify ต้องส่งรหัสผ่านนี้คู่กับ OTP — คนที่ start ทับจึงผูกรหัสผ่านของตัวเองกับอู่ของเจ้าของอีเมลไม่ได้)
        account.Provider = SelfServeAccountProvider.Email;
        account.PasswordHash = passwordHash;
        account.Email = display;
        account.Status = SelfServeAccountStatus.Pending;
        account.EmailVerifiedAt = null;
        account.SetupTokenHash = null;
        account.SetupTokenExpiresAt = null;
        account.UpdatedAt = Now;
        await repository.UpdateAccountAsync(account, ct);
        return account;
    }

    /// <summary>ตรวจเพดานการส่ง (≥60 วิระหว่างครั้ง · ≤3 ครั้ง/ชม./อีเมล) — null = ส่งได้</summary>
    private ApiError? Throttle(EmailOtp? otp)
    {
        if (otp is null) return null;
        var now = Now;

        var sinceLast = now - otp.LastSentAt;
        if (sinceLast < TimeSpan.FromSeconds(ResendAfterSeconds))
            return SelfServeErrors.SignupOtpThrottled(CeilSeconds(TimeSpan.FromSeconds(ResendAfterSeconds) - sinceLast));

        if (now - otp.WindowStartedAt < SendWindow && otp.SentCount >= MaxSendsPerWindow)
            return SelfServeErrors.SignupOtpThrottled(CeilSeconds(otp.WindowStartedAt + SendWindow - now));

        return null;
    }

    /// <summary>
    /// สร้างรหัสใหม่ (ทับรหัสเดิม) แล้วส่ง — <paramref name="accountTaken"/> = ส่งอีเมล "มีบัญชีแล้ว" แทน (รหัสถูกสร้างแต่ไม่ถูกส่ง)
    /// · null = สำเร็จ · ไม่งั้นคือ error ที่ต้องตอบ
    /// </summary>
    private async Task<ApiError?> IssueCodeAndSendAsync(
        EmailOtp? otp, string normalized, string to, bool accountTaken, CancellationToken ct)
    {
        var now = Now;
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
        var isNew = otp is null;
        otp ??= new EmailOtp { Email = normalized, CreatedAt = now, WindowStartedAt = now };

        if (now - otp.WindowStartedAt >= SendWindow)
        {
            otp.WindowStartedAt = now;
            otp.SentCount = 0;
        }

        otp.CodeHash = hasher.Hash(code);
        otp.ExpiresAt = now.AddMinutes(OtpMinutes);
        otp.Attempts = 0;
        otp.ConsumedAt = null;
        otp.SentCount++;
        otp.LastSentAt = now;

        try
        {
            if (isNew) await repository.AddOtpAsync(otp, ct);
            else await repository.UpdateOtpAsync(otp, ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // อีกคำขอของอีเมลเดียวกันเพิ่งส่งรหัสไป — ถือว่าโดนเพดาน 60 วิ (ผู้ใช้ได้อีเมลจากคำขอนั้นแล้ว)
            return SelfServeErrors.SignupOtpThrottled(ResendAfterSeconds);
        }

        try
        {
            await email.SendAsync(accountTaken
                ? SelfServeEmails.AccountExists(options.PublicBaseUrl).To(to)
                : SelfServeEmails.Otp(code, OtpMinutes).To(to), ct);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // [SECURITY] ไม่ log อีเมล/รหัส — แค่ประเภทความผิดพลาด
            log.Error(ex, "signup: ส่งอีเมล{Kind}ไม่สำเร็จ", accountTaken ? " (มีบัญชีแล้ว)" : " OTP");
            return SignupErrors.EmailSendFailed();
        }
    }

    private async Task<ApiError?> CheckDomainAsync(string domain, CancellationToken ct)
    {
        if (await repository.IsDomainBlockedAsync(domain, ct)) return SelfServeErrors.SignupEmailDisposable();

        var verdict = await domains.CheckAsync(domain, ct);
        switch (verdict)
        {
            case EmailDomainVerdict.Disposable:
                return SelfServeErrors.SignupEmailDisposable();
            case EmailDomainVerdict.NoMailServer:
                return SelfServeErrors.SignupEmailDomainInvalid();
            case EmailDomainVerdict.Unknown:
                // [BIZ] DNS ของเราล่มต้องไม่ทำให้สมัครไม่ได้ทั้งระบบ — ปล่อยผ่าน (ยังต้องยืนยัน OTP อยู่ดี)
                log.Info("signup: ตรวจโดเมน {Domain} ไม่ได้ (DNS) ปล่อยผ่าน", domain);
                return null;
            default:
                return null;
        }
    }

    private async Task<(bool Ok, string? Name)> ResolveProvinceAsync(int? provinceId, CancellationToken ct)
    {
        if (provinceId is null) return (true, null);
        if (provinceId <= 0) return (false, null);
        try
        {
            var provinces = await referenceData.GetProvincesAsync(ct);
            var match = provinces.FirstOrDefault(p => p.Id == provinceId);
            return match is null ? (false, null) : (true, match.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // อ่านรายชื่อจังหวัดไม่ได้ (db2 ล่ม) — ไม่บล็อกการสมัครเพราะช่องนี้ไม่บังคับ
            log.Warning(ex, "signup: อ่านรายชื่อจังหวัดไม่ได้ ข้ามการตรวจ provinceId {ProvinceId}", provinceId);
            return (true, null);
        }
    }

    private async Task<IReadOnlyList<SampleCredential>> LoadSampleDataAsync(Guid workspaceId, CancellationToken ct)
    {
        if (starterData is null)
        {
            log.Warning(null, "signup: ไม่มี IStarterDataService ข้ามข้อมูลตัวอย่างของ workspace {WorkspaceId}", workspaceId);
            return [];
        }

        try
        {
            var loaded = await starterData.LoadAsync(workspaceId, ct);
            if (!loaded.Loaded)
                log.Warning(null, "signup: ใส่ข้อมูลตัวอย่างไม่ครบ ({Error}) workspace {WorkspaceId}", loaded.Error, workspaceId);
            return loaded.Technicians;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Warning(ex, "signup: ใส่ข้อมูลตัวอย่างล้ม workspace {WorkspaceId} การสมัครยังสำเร็จ", workspaceId);
            return [];
        }
    }

    private async Task NotifySalesAsync(Guid workspaceId, SignupSalesInfo info, CancellationToken ct)
    {
        if (!notifier.IsConfigured)
        {
            log.Info("signup: ไม่ได้ตั้งค่า LINE ข้ามการแจ้งทีมขาย ({WorkspaceNo})", info.WorkspaceNo);
            return;
        }

        try
        {
            // retry key = workspace id — คำขอเดิมที่ทำซ้ำ (resume) ไม่ทำให้กลุ่มได้ข้อความซ้ำ
            if (!await notifier.SendAsync(workspaceId, SignupMessages.SalesLine(info), ct))
                log.Warning(null, "signup: แจ้ง LINE ทีมขายไม่สำเร็จ ({WorkspaceNo})", info.WorkspaceNo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Warning(ex, "signup: แจ้ง LINE ทีมขายล้ม ({WorkspaceNo})", info.WorkspaceNo);
        }
    }

    /// <summary>ชื่อที่กรอกตอนสมัคร → ชื่อจาก Google → ส่วนหน้าของอีเมล · คำแรกเป็นชื่อ ที่เหลือเป็นนามสกุล</summary>
    private (string First, string Last) OwnerName(SelfServeAccount account, string ownerEmail, string? typedName = null)
    {
        if (!string.IsNullOrWhiteSpace(typedName))
        {
            var words = typedName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return (Truncate(words[0], 50)!, Truncate(words.Length > 1 ? words[1] : "", 50) ?? "");
        }

        if (stash.TryGet(OwnerNameKey(account.Id), out var packed))
        {
            var parts = packed.Split('\u001F');
            var first = OneLine(parts[0]);
            var last = parts.Length > 1 ? OneLine(parts[1]) : "";
            if (first.Length > 0) return (Truncate(first, 50)!, Truncate(last, 50) ?? "");
        }

        return (SignupMessages.LocalPart(ownerEmail), "");
    }

    private void ApplyGoogle(SelfServeAccount account, GoogleIdentity identity, string token, string normalized, string display)
    {
        account.Provider = SelfServeAccountProvider.Google;
        account.GoogleSubject = identity.Subject;
        // อีเมลที่ใช้ส่งจดหมายตามอีเมลปัจจุบันของบัญชี Google เฉพาะเมื่อเป็นอีเมลเดียวกับแถวนี้
        // (พบแถวจาก sub แต่ผู้ใช้เปลี่ยนอีเมลใน Google แล้ว — NormalizedEmail เดิมคงไว้ตลอดกาล)
        if (account.NormalizedEmail == normalized) account.Email = display;
        // [SECURITY] ล้างรหัสผ่านที่ตั้งจาก email/start — คนตั้งอาจไม่ใช่เจ้าของอีเมล (ยังไม่ได้ยืนยัน OTP)
        // ถ้าคงไว้ อู่ที่เปิดด้วย Google จะเข้าได้ด้วยรหัสผ่านของคนอื่น
        account.PasswordHash = null;
        account.EmailVerifiedAt = Now;
        account.Status = SelfServeAccountStatus.Verified;
        account.SetupTokenHash = HashToken(token);
        account.SetupTokenExpiresAt = Now + SignupTokenLifetime;
        account.UpdatedAt = Now;
    }

    private SelfServeAccount NewAccount(string normalized, string display, SignupClientInfo client) => new()
    {
        NormalizedEmail = normalized,
        Email = display,
        Provider = SelfServeAccountProvider.Email,
        Status = SelfServeAccountStatus.Pending,
        CreatedAt = Now,
        CreatedIp = Truncate(client.Ip, 64),
        CreatedUserAgent = Truncate(client.UserAgent, 512),
    };

    private async Task<Result<T>> FailAsync<T>(
        string step, ApiError error, SignupClientInfo client, string? domain, Guid? accountId, CancellationToken ct,
        string? reason = null)
    {
        await RecordAsync(step, error.Code, client, domain, accountId, reason, ct);
        return Result<T>.Fail(error);
    }

    private async Task RecordAsync(
        string step, string outcome, SignupClientInfo client, string? domain, Guid? accountId, string? reason,
        CancellationToken ct)
    {
        try
        {
            await repository.AddAttemptAsync(new SignupAttempt
            {
                CreatedAt = Now,
                Step = step,
                Outcome = Truncate(outcome, 60)!,
                Reason = Truncate(reason, 500),
                EmailDomain = Truncate(domain, 255),
                AccountId = accountId,
                Ip = Truncate(client.Ip, 64),
                Country = Truncate(client.Country, 8),
                UserAgent = Truncate(client.UserAgent, 512),
            }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Warning(ex, "signup: บันทึก sp_SignupAttempt ไม่สำเร็จ ({Step} {Outcome})", step, outcome);
        }
    }

    private async Task TryAsync(Func<Task> work, string what, Guid workspaceId)
    {
        try { await work(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Warning(ex, "signup: {What} ไม่สำเร็จ (workspace {WorkspaceId})", what, workspaceId);
        }
    }

    private async Task<T?> TryGetAsync<T>(Func<Task<T?>> work) where T : class
    {
        try { return await work(); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Warning(ex, "signup: อ่านข้อมูลประกอบไม่สำเร็จ");
            return null;
        }
    }

    private static ApiError? ValidatePassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < PasswordMinLength
            || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            return SelfServeErrors.SignupValidation(
                $"รหัสผ่านต้องยาวอย่างน้อย {PasswordMinLength} ตัว และมีทั้งตัวอักษรและตัวเลข", "password");
        if (password.Length > PasswordMaxLength)
            return SelfServeErrors.SignupValidation($"รหัสผ่านยาวเกิน {PasswordMaxLength} ตัวอักษร", "password");
        return null;
    }

    private static bool IsTaken(SelfServeAccountStatus status) =>
        status is SelfServeAccountStatus.Active or SelfServeAccountStatus.Suspended or SelfServeAccountStatus.Purged;

    /// <summary>requestId จาก client — UUID หรือข้อความ [A-Za-z0-9_-] 8–60 ตัว (ยาวสุดต้องใส่ "{hash}:{requestId}" ใน 128 ตัวได้)</summary>
    private static string? NormalizeRequestId(string? requestId)
    {
        var value = requestId?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length is < 8 or > 60) return null;
        return value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? value : null;
    }

    private static bool IsTokenShape(string? token) =>
        token is { Length: >= 40 and <= 100 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static string StartKey(string normalized, string requestId) => $"start:{normalized}:{requestId}";
    private static string OwnerNameKey(Guid accountId) => $"owner-name:{accountId:N}";

    public static string NewSignupToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>SHA-256 (hex ตัวพิมพ์เล็ก) ของ signupToken — ค่าที่เก็บใน sp_Account.SetupTokenHash</summary>
    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string RandomSecret(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        return string.Create(length, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++) span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }

    private static int CeilSeconds(TimeSpan span) => Math.Max(1, (int)Math.Ceiling(span.TotalSeconds));

    private static string OneLine(string? value) =>
        string.Join(' ', (value ?? "").Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();

    private static string? Truncate(string? value, int max) =>
        value is null ? null : value.Length <= max ? value : value[..max];
}
