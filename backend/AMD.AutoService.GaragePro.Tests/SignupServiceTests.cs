using System.Text.Json;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Contact;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class SignupServiceTests
{
    // ---------------------------------------------------------------- fakes

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
        public void Advance(TimeSpan by) => Now += by;
    }

    /// <summary>เร็วกว่า PBKDF2 210,000 รอบ — เทสต์ตรรกะ ไม่ใช่ความแข็งแรงของ hash</summary>
    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => "h$" + password;
        public bool Verify(string stored, string input) => stored == "h$" + input;
        public bool IsHashed(string? stored) => stored?.StartsWith("h$", StringComparison.Ordinal) == true;
    }

    /// <summary>คืนสำเนาทุกครั้ง (เหมือน AsNoTracking) — service ต้องเขียนกลับผ่าน Update* เอง ไม่งั้นค่าไม่ถูกบันทึก</summary>
    private sealed class FakeRepo : ISelfServeSignupRepository
    {
        public List<SelfServeAccount> Accounts { get; } = [];
        public List<EmailOtp> Otps { get; } = [];
        public List<SignupAttempt> Attempts { get; } = [];
        public List<Workspace> Workspaces { get; } = [];
        public HashSet<string> Blocked { get; } = [];

        private static T Clone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;

        public SelfServeAccount Account(string normalizedEmail) => Accounts.Single(a => a.NormalizedEmail == normalizedEmail);

        public Task<SelfServeAccount?> FindAccountByEmailAsync(string normalizedEmail, CancellationToken ct = default) =>
            Task.FromResult(Accounts.Where(a => a.NormalizedEmail == normalizedEmail).Select(Clone).FirstOrDefault());

        public Task<SelfServeAccount?> FindAccountByGoogleSubjectAsync(string subject, CancellationToken ct = default) =>
            Task.FromResult(Accounts.Where(a => a.GoogleSubject == subject).Select(Clone).FirstOrDefault());

        public Task<SelfServeAccount?> FindAccountBySetupTokenAsync(string tokenHash, CancellationToken ct = default) =>
            Task.FromResult(Accounts
                .Where(a => a.SetupTokenHash == tokenHash || (a.SetupTokenHash?.StartsWith(tokenHash + ":") ?? false))
                .Select(Clone).FirstOrDefault());

        public Task AddAccountAsync(SelfServeAccount account, CancellationToken ct = default)
        {
            if (Accounts.Any(a => a.NormalizedEmail == account.NormalizedEmail))
                throw new UniqueConstraintViolationException("UX_sp_Account_NormalizedEmail");
            Accounts.Add(Clone(account));
            return Task.CompletedTask;
        }

        public Task UpdateAccountAsync(SelfServeAccount account, CancellationToken ct = default)
        {
            Accounts[Accounts.FindIndex(a => a.Id == account.Id)] = Clone(account);
            return Task.CompletedTask;
        }

        public Task<bool> TryReplaceSetupTokenHashAsync(Guid accountId, string expected, string replacement, CancellationToken ct = default)
        {
            var row = Accounts.Single(a => a.Id == accountId);
            if (row.SetupTokenHash != expected) return Task.FromResult(false);
            row.SetupTokenHash = replacement;
            return Task.FromResult(true);
        }

        public Task MarkAccountActiveAsync(Guid accountId, long userId, string loginUserName, DateTime now, CancellationToken ct = default)
        {
            var row = Accounts.Single(a => a.Id == accountId);
            row.Status = SelfServeAccountStatus.Active;
            row.UserId ??= userId;
            row.LoginUserName ??= loginUserName;
            return Task.CompletedTask;
        }

        public Task<EmailOtp?> GetOtpAsync(string normalizedEmail, CancellationToken ct = default) =>
            Task.FromResult(Otps.Where(o => o.Email == normalizedEmail).Select(Clone).FirstOrDefault());

        public Task AddOtpAsync(EmailOtp otp, CancellationToken ct = default)
        {
            if (Otps.Any(o => o.Email == otp.Email)) throw new UniqueConstraintViolationException("UX_sp_EmailOtp_Email");
            Otps.Add(Clone(otp));
            return Task.CompletedTask;
        }

        public Task UpdateOtpAsync(EmailOtp otp, CancellationToken ct = default)
        {
            Otps[Otps.FindIndex(o => o.Id == otp.Id)] = Clone(otp);
            return Task.CompletedTask;
        }

        public Task<bool> IsDomainBlockedAsync(string domain, CancellationToken ct = default) =>
            Task.FromResult(Blocked.Contains(domain));

        public Task<Workspace?> FindWorkspaceAsync(Guid accountId, string signupRequestId, CancellationToken ct = default) =>
            Task.FromResult(Workspaces.Where(w => w.OwnerAccountId == accountId && w.SignupRequestId == signupRequestId)
                .Select(Clone).FirstOrDefault());

        public Task<bool> IsPhoneUsedByOtherAccountAsync(string phoneE164, Guid accountId, CancellationToken ct = default) =>
            Task.FromResult(Workspaces.Any(w => w.Phone == phoneE164 && w.OwnerAccountId != accountId));

        public Task SetDuplicatePhoneFlagAsync(Guid workspaceId, CancellationToken ct = default)
        {
            Workspaces.Single(w => w.Id == workspaceId).DuplicatePhoneFlag = true;
            return Task.CompletedTask;
        }

        public Task AddAttemptAsync(SignupAttempt attempt, CancellationToken ct = default)
        {
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEmail : IEmailSender
    {
        public bool IsConfigured { get; set; } = true;
        public bool Throws { get; set; }
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("provider down");
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTurnstile : ITurnstileVerifier
    {
        public bool Pass { get; set; } = true;
        public int Calls { get; private set; }

        public Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(Pass);
        }
    }

    private sealed class FakeDomains : IEmailDomainChecker
    {
        public EmailDomainVerdict Verdict { get; set; } = EmailDomainVerdict.Ok;
        public Task<EmailDomainVerdict> CheckAsync(string domain, CancellationToken ct = default) => Task.FromResult(Verdict);
    }

    private sealed class FakeGoogle : IGoogleIdTokenValidator
    {
        public GoogleIdentity? Identity { get; set; } =
            new("google-sub-1", "Owner.Eak@gmail.com", true, "เอก", "ใจดี", null);

        public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default) => Task.FromResult(Identity);
    }

    private sealed class FakeNotifier : ISelfServeSalesNotifier
    {
        public bool IsConfigured { get; set; } = true;
        public List<(Guid Key, string Text)> Sent { get; } = [];

        public Task<bool> SendAsync(Guid retryKey, string text, CancellationToken ct)
        {
            Sent.Add((retryKey, text));
            return Task.FromResult(true);
        }
    }

    private sealed class FakeSessions : ISignupSessionIssuer
    {
        public bool Fails { get; set; }
        public List<(string Shard, long UserId)> Calls { get; } = [];

        public Task<Result<LoginResultDto>> IssueAsync(string shardKey, long userId, CancellationToken ct = default)
        {
            Calls.Add((shardKey, userId));
            if (Fails) return Task.FromResult(Result<LoginResultDto>.Fail("AUTH_NOT_IMPLEMENTED", "ยังไม่รองรับ"));
            return Task.FromResult(Result<LoginResultDto>.Ok(new LoginResultDto(
                "access-token", new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc),
                new AuthUserDto(userId, "SP1001.M01", "เจ้าของ", "Manager", "ผู้จัดการ", false, shardKey, 77,
                    null, null, true, true),
                9001, "อู่ช่างเอก", [], false)));
        }
    }

    private sealed class FakeReference : ISignupReferenceData
    {
        public bool Throws { get; set; }

        public Task<IReadOnlyList<SignupProvinceDto>> GetProvincesAsync(CancellationToken ct = default) =>
            Throws
                ? throw new InvalidOperationException("db2 down")
                : Task.FromResult<IReadOnlyList<SignupProvinceDto>>([new(1, "กรุงเทพมหานคร"), new(38, "เชียงใหม่")]);
    }

    /// <summary>จำลอง S2: สร้าง sp_Workspace ใน repo · คำขอเดิมซ้ำคืนผลเดิม</summary>
    private sealed class FakeProvisioner(FakeRepo repo, FixedClock clock) : IWorkspaceProvisioner
    {
        private int _nextNo = 1001;
        public List<ProvisionRequest> Requests { get; } = [];
        public bool Fails { get; set; }
        public bool Throws { get; set; }

        public Task<Result<ProvisionResult>> ProvisionAsync(ProvisionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var ws = repo.Workspaces.SingleOrDefault(w =>
                w.OwnerAccountId == request.AccountId && w.SignupRequestId == request.SignupRequestId);
            if (ws is null)
            {
                ws = new Workspace
                {
                    WorkspaceNo = _nextNo++,
                    Name = request.GarageName,
                    Phone = request.PhoneE164,
                    OwnerAccountId = request.AccountId,
                    SignupRequestId = request.SignupRequestId,
                    Status = WorkspaceStatus.Provisioning,
                    CreatedAt = clock.GetUtcNow().UtcDateTime,
                };
                repo.Workspaces.Add(ws);
            }

            if (Throws) throw new InvalidOperationException("sql down");
            if (Fails)
            {
                ws.ProvisionError = "PROVISION_BRANCH_FAILED";
                return Task.FromResult(Result<ProvisionResult>.Fail("PROVISION_BRANCH_FAILED", "สร้างสาขาไม่ได้"));
            }

            ws.ProvisionError = null;
            ws.Status = WorkspaceStatus.Active;
            ws.BranchId = 9000 + ws.WorkspaceNo - 1000;
            ws.TrialEndsAt = clock.GetUtcNow().UtcDateTime.AddDays(30);
            return Task.FromResult(Result<ProvisionResult>.Ok(new ProvisionResult(
                ws.Id, ws.WorkspaceNo, ws.BranchId.Value, 500 + ws.WorkspaceNo, 77, $"SP{ws.WorkspaceNo}.M01")));
        }
    }

    private sealed class FakeStarter : IStarterDataService
    {
        public bool Throws { get; set; }
        public List<Guid> Loaded { get; } = [];

        public Task<StarterDataResult> LoadAsync(Guid workspaceId, CancellationToken ct = default)
        {
            if (Throws) throw new InvalidOperationException("ftp down");
            Loaded.Add(workspaceId);
            return Task.FromResult(new StarterDataResult(true, [new("SP1001.T01", "Tech-pass-1"), new("SP1001.T02", "Tech-pass-2")], null));
        }

        public Task<Result<bool>> ClearAsync(Guid workspaceId, CancellationToken ct = default) =>
            Task.FromResult(Result<bool>.Ok(true));
    }

    private sealed class FakeLog : ISignupLog
    {
        public List<string> Entries { get; } = [];
        public void Info(string message, params object?[] args) => Entries.Add("info:" + message);
        public void Warning(Exception? exception, string message, params object?[] args) => Entries.Add("warn:" + message);
        public void Error(Exception? exception, string message, params object?[] args) => Entries.Add("error:" + message);
    }

    private sealed class Harness
    {
        public required SignupService Service { get; init; }
        public required FixedClock Clock { get; init; }
        public required FakeRepo Repo { get; init; }
        public required FakeEmail Email { get; init; }
        public required FakeTurnstile Turnstile { get; init; }
        public required FakeDomains Domains { get; init; }
        public required FakeGoogle Google { get; init; }
        public required FakeNotifier Notifier { get; init; }
        public required FakeSessions Sessions { get; init; }
        public required FakeProvisioner Provisioner { get; init; }
        public required FakeStarter Starter { get; init; }
        public required SelfServeOptions Options { get; init; }

        /// <summary>รหัส OTP ล่าสุดที่ส่งจริง (อยู่ในหัวเรื่อง "…: 123456")</summary>
        public string LastCode => Email.Sent.Last(m => m.Tag == "otp").Subject.Split(": ")[^1];
    }

    private static readonly DateTimeOffset Start = new(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);
    private static readonly SignupClientInfo Client = new("203.0.113.9", "TH", "xunit");
    private const string Email = "Owner.Eak@example.com";
    private const string Normalized = "owner.eak@example.com";
    private const string Password = "garage2026";

    private static Harness Create(bool production = false, Action<SelfServeOptions>? configure = null)
    {
        var clock = new FixedClock(Start);
        var repo = new FakeRepo();
        var options = new SelfServeOptions { Enabled = true };
        options.Google.ClientId = "client-id.apps.googleusercontent.com";
        configure?.Invoke(options);

        var email = new FakeEmail();
        var turnstile = new FakeTurnstile();
        var domains = new FakeDomains();
        var google = new FakeGoogle();
        var notifier = new FakeNotifier();
        var sessions = new FakeSessions();
        var provisioner = new FakeProvisioner(repo, clock);
        var starter = new FakeStarter();

        return new Harness
        {
            Clock = clock, Repo = repo, Email = email, Turnstile = turnstile, Domains = domains, Google = google,
            Notifier = notifier, Sessions = sessions, Provisioner = provisioner, Starter = starter, Options = options,
            Service = new SignupService(repo, new FakeHasher(), email, turnstile, domains, google, notifier,
                sessions, new FakeReference(), options, new SignupStash(clock), new FakeLog(), clock,
                provisioner, starter, new SignupHostEnvironment(production)),
        };
    }

    private static EmailSignupStartRequest StartRequest(
        string email = Email, string password = Password, string? requestId = null, string? website = null, bool consent = true) =>
        new(requestId ?? Guid.NewGuid().ToString(), email, password, "turnstile-token", website, consent);

    private static CreateWorkspaceRequest WorkspaceRequest(
        string token, string? requestId = null, string phone = "081-234-5678", bool sampleData = true) =>
        new(requestId ?? "11111111-2222-3333-4444-555555555555", token, "อู่ช่างเอก", phone, 38, "1-3", sampleData);

    private static async Task<string> VerifiedTokenAsync(Harness h, string email = Email)
    {
        (await h.Service.StartEmailAsync(StartRequest(email), Client)).Success.Should().BeTrue();
        var verified = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(email, h.LastCode, Password), Client);
        verified.Success.Should().BeTrue();
        return verified.Data!.SignupToken;
    }

    // ---------------------------------------------------------------- config

    [Fact]
    public void Config_reports_channels_and_site_key()
    {
        var h = Create(configure: o => o.Turnstile.SiteKey = "0x4AAA");

        h.Service.GetConfig().Should().Be(new SignupConfigDto(true, true, true, "0x4AAA"));
    }

    [Fact]
    public void Config_hides_email_in_production_without_a_real_provider_and_everything_when_disabled()
    {
        var production = Create(production: true);
        production.Email.IsConfigured = false;
        production.Service.GetConfig().EmailEnabled.Should().BeFalse();

        var disabled = Create(configure: o => o.Enabled = false);
        disabled.Service.GetConfig().Should().Be(new SignupConfigDto(false, false, false, null));
    }

    [Fact]
    public async Task Start_is_disabled_in_production_when_otp_would_only_go_to_the_log()
    {
        var h = Create(production: true);
        h.Email.IsConfigured = false;

        var result = await h.Service.StartEmailAsync(StartRequest(), Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupDisabledCode);
        h.Email.Sent.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- email/start

    [Fact]
    public async Task Start_creates_pending_account_with_hashed_password_and_sends_otp()
    {
        var h = Create();

        var result = await h.Service.StartEmailAsync(StartRequest(), Client);

        result.Success.Should().BeTrue();
        result.Data!.ResendAfterSeconds.Should().Be(SignupService.ResendAfterSeconds);
        var account = h.Repo.Account(Normalized);
        account.Status.Should().Be(SelfServeAccountStatus.Pending);
        account.Email.Should().Be(Email);
        account.PasswordHash.Should().Be("h$" + Password);
        account.CreatedIp.Should().Be(Client.Ip);
        h.Email.Sent.Should().ContainSingle(m => m.Tag == "otp" && m.To == Email);
        h.LastCode.Should().MatchRegex("^[0-9]{6}$");
        h.Repo.Otps.Single().CodeHash.Should().Be("h$" + h.LastCode, "เก็บแค่ hash ของรหัส");
        h.Repo.Attempts.Should().ContainSingle(a =>
            a.Step == SignupService.StepEmailStart && a.Outcome == "ok" && a.EmailDomain == "example.com"
            && a.Ip == Client.Ip && a.Country == "TH" && a.AccountId == account.Id);
    }

    [Fact]
    public async Task Honeypot_is_silently_accepted_without_side_effects()
    {
        var h = Create();

        var result = await h.Service.StartEmailAsync(StartRequest(website: "http://spam"), Client);

        result.Success.Should().BeTrue();
        h.Turnstile.Calls.Should().Be(0);
        h.Repo.Accounts.Should().BeEmpty();
        h.Email.Sent.Should().BeEmpty();
        h.Repo.Attempts.Should().ContainSingle(a => a.Outcome == SignupService.OutcomeHoneypot);
    }

    [Fact]
    public async Task Bot_check_failure_is_rejected_and_logged()
    {
        var h = Create();
        h.Turnstile.Pass = false;

        var result = await h.Service.StartEmailAsync(StartRequest(), Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupBotCheckFailedCode);
        h.Repo.Accounts.Should().BeEmpty();
        h.Repo.Attempts.Should().ContainSingle(a => a.Outcome == SelfServeErrors.SignupBotCheckFailedCode);
    }

    [Theory]
    [InlineData("not-an-email", Password, true, "email")]
    [InlineData(Email, "short1", true, "password")]
    [InlineData(Email, "onlyletters", true, "password")]
    [InlineData(Email, "1234567890", true, "password")]
    [InlineData(Email, Password, false, "consent")]
    public async Task Start_validates_email_password_and_consent(string email, string password, bool consent, string field)
    {
        var h = Create();

        var result = await h.Service.StartEmailAsync(StartRequest(email, password, consent: consent), Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupValidationCode);
        result.Error.Field.Should().Be(field);
        h.Email.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Disposable_domains_are_rejected_from_the_blocklist_or_the_checker()
    {
        var h = Create();
        h.Repo.Blocked.Add("mailinator.com");

        var blocked = await h.Service.StartEmailAsync(StartRequest("someone@mailinator.com"), Client);
        blocked.Error!.Code.Should().Be(SelfServeErrors.SignupEmailDisposableCode);
        blocked.Error.MessageTh.Should().Be("ไม่รองรับอีเมลชั่วคราว กรุณาใช้อีเมลที่ใช้งานจริง");

        h.Domains.Verdict = EmailDomainVerdict.Disposable;
        (await h.Service.StartEmailAsync(StartRequest(), Client)).Error!.Code
            .Should().Be(SelfServeErrors.SignupEmailDisposableCode);

        h.Email.Sent.Should().BeEmpty();
        h.Repo.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Domain_without_mail_server_is_rejected_but_dns_failure_is_allowed()
    {
        var h = Create();
        h.Domains.Verdict = EmailDomainVerdict.NoMailServer;

        var nx = await h.Service.StartEmailAsync(StartRequest("owner@no-such-domain-xyz.com"), Client);
        nx.Error!.Code.Should().Be(SelfServeErrors.SignupEmailDomainInvalidCode);

        h.Domains.Verdict = EmailDomainVerdict.Unknown;
        (await h.Service.StartEmailAsync(StartRequest(), Client)).Success.Should().BeTrue();
        h.Email.Sent.Should().ContainSingle(m => m.Tag == "otp");
    }

    [Fact]
    public async Task Existing_active_account_gets_the_same_200_and_an_account_exists_email_instead_of_an_otp()
    {
        var h = Create();
        h.Repo.Accounts.Add(new SelfServeAccount
        {
            NormalizedEmail = Normalized, Email = Email, Status = SelfServeAccountStatus.Active,
            PasswordHash = "h$old-password1", CreatedAt = Start.UtcDateTime,
        });

        var result = await h.Service.StartEmailAsync(StartRequest(password: "new-password9"), Client);

        result.Success.Should().BeTrue();
        result.Data!.ResendAfterSeconds.Should().Be(SignupService.ResendAfterSeconds);
        h.Email.Sent.Should().ContainSingle().Which.Tag.Should().Be("account-exists");
        h.Email.Sent.Should().NotContain(m => m.Tag == "otp");
        var account = h.Repo.Account(Normalized);
        account.Status.Should().Be(SelfServeAccountStatus.Active, "บัญชีที่มีอยู่ต้องไม่ถูกแตะ");
        account.PasswordHash.Should().Be("h$old-password1");
        h.Repo.Attempts.Should().ContainSingle(a => a.Outcome == SignupService.OutcomeAccountExists);

        // [SECURITY] verify ตอบเหมือนอีเมลใหม่ (รหัสผิด เหลืออีก N ครั้ง) — ไม่เปิดเผยว่ามีบัญชีแล้ว
        var verify = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, "123456", "new-password9"), Client);
        verify.Error!.Code.Should().Be(SelfServeErrors.SignupOtpInvalidCode);
        verify.Error.MessageTh.Should().Contain("ลองได้อีก 4 ครั้ง");
    }

    [Fact]
    public async Task Replaying_the_same_start_request_does_not_send_again_or_hit_turnstile()
    {
        var h = Create();
        var request = StartRequest(requestId: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        (await h.Service.StartEmailAsync(request, Client)).Success.Should().BeTrue();
        h.Clock.Advance(TimeSpan.FromSeconds(20));

        var replay = await h.Service.StartEmailAsync(request, Client);

        replay.Success.Should().BeTrue();
        replay.Data!.ResendAfterSeconds.Should().Be(40);
        h.Turnstile.Calls.Should().Be(1);
        h.Email.Sent.Should().ContainSingle();
    }

    // ---------------------------------------------------------------- OTP throttle

    [Fact]
    public async Task Resend_waits_60_seconds_and_allows_three_sends_per_hour()
    {
        var h = Create();
        (await h.Service.StartEmailAsync(StartRequest(), Client)).Success.Should().BeTrue();

        h.Clock.Advance(TimeSpan.FromSeconds(30));
        var tooSoon = await h.Service.ResendEmailAsync(new EmailSignupResendRequest(Email), Client);
        tooSoon.Error!.Code.Should().Be(SelfServeErrors.SignupOtpThrottledCode);
        SignupErrors.StatusCodeFor(tooSoon.Error.Code).Should().Be(429);
        SignupErrors.RetryAfterSeconds(tooSoon.Error).Should().Be(30);

        h.Clock.Advance(TimeSpan.FromSeconds(31));
        (await h.Service.ResendEmailAsync(new EmailSignupResendRequest(Email), Client)).Success.Should().BeTrue();
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        (await h.Service.ResendEmailAsync(new EmailSignupResendRequest(Email), Client)).Success.Should().BeTrue();

        h.Clock.Advance(TimeSpan.FromSeconds(61));
        var fourth = await h.Service.ResendEmailAsync(new EmailSignupResendRequest(Email), Client);
        fourth.Error!.Code.Should().Be(SelfServeErrors.SignupOtpThrottledCode, "ส่งครบ 3 ครั้งในชั่วโมงเดียวแล้ว");
        SignupErrors.RetryAfterSeconds(fourth.Error).Should().BeGreaterThan(60);
        h.Email.Sent.Count(m => m.Tag == "otp").Should().Be(3);

        h.Clock.Advance(TimeSpan.FromHours(1));
        (await h.Service.ResendEmailAsync(new EmailSignupResendRequest(Email), Client)).Success.Should().BeTrue();
        h.Email.Sent.Count(m => m.Tag == "otp").Should().Be(4);
    }

    [Fact]
    public async Task Resend_for_an_email_that_never_started_answers_ok_without_sending()
    {
        var h = Create();

        var result = await h.Service.ResendEmailAsync(new EmailSignupResendRequest("nobody@example.com"), Client);

        result.Success.Should().BeTrue();
        h.Email.Sent.Should().BeEmpty();
        h.Repo.Otps.Should().BeEmpty();
    }

    [Fact]
    public async Task Email_provider_failure_returns_a_retryable_error()
    {
        var h = Create();
        h.Email.Throws = true;

        var result = await h.Service.StartEmailAsync(StartRequest(), Client);

        result.Error!.Code.Should().Be(SignupErrors.EmailSendFailedCode);
        SignupErrors.StatusCodeFor(result.Error.Code).Should().Be(503);
    }

    // ---------------------------------------------------------------- email/verify

    [Fact]
    public async Task Wrong_codes_count_down_then_lock_even_for_the_right_code()
    {
        var h = Create();
        (await h.Service.StartEmailAsync(StartRequest(), Client)).Success.Should().BeTrue();
        var code = h.LastCode;
        var wrong = code == "000000" ? "111111" : "000000";

        for (var left = 4; left >= 1; left--)
        {
            var attempt = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, wrong, Password), Client);
            attempt.Error!.Code.Should().Be(SelfServeErrors.SignupOtpInvalidCode);
            attempt.Error.MessageTh.Should().Contain($"ลองได้อีก {left} ครั้ง");
        }

        var fifth = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, wrong, Password), Client);
        fifth.Error!.Code.Should().Be(SelfServeErrors.SignupOtpLockedCode);

        var right = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, code, Password), Client);
        right.Error!.Code.Should().Be(SelfServeErrors.SignupOtpLockedCode);
        h.Repo.Account(Normalized).Status.Should().Be(SelfServeAccountStatus.Pending);
    }

    [Fact]
    public async Task Expired_code_is_rejected()
    {
        var h = Create();
        (await h.Service.StartEmailAsync(StartRequest(), Client)).Success.Should().BeTrue();
        h.Clock.Advance(TimeSpan.FromMinutes(SignupService.OtpMinutes) + TimeSpan.FromSeconds(1));

        var result = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, h.LastCode, Password), Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupOtpExpiredCode);
    }

    [Fact]
    public async Task Verify_issues_a_single_use_token_and_stores_only_its_hash()
    {
        var h = Create();
        (await h.Service.StartEmailAsync(StartRequest(), Client)).Success.Should().BeTrue();
        var code = h.LastCode;

        var verified = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(" " + Email.ToUpperInvariant(), code, Password), Client);

        verified.Success.Should().BeTrue();
        var token = verified.Data!.SignupToken;
        token.Length.Should().BeGreaterThanOrEqualTo(40);
        verified.Data.ExpiresAt.Should().Be(Start.UtcDateTime + SignupService.SignupTokenLifetime);
        var account = h.Repo.Account(Normalized);
        account.Status.Should().Be(SelfServeAccountStatus.Verified);
        account.EmailVerifiedAt.Should().Be(Start.UtcDateTime);
        account.SetupTokenHash.Should().Be(SignupService.HashToken(token)).And.NotContain(token);

        // รหัสเดิมใช้ซ้ำไม่ได้
        var again = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, code, Password), Client);
        again.Error!.Code.Should().Be(SelfServeErrors.SignupOtpExpiredCode);
    }

    // ---------------------------------------------------------------- password ผูกกับการยืนยันอีเมล

    [Fact]
    public async Task Attacker_restarting_with_another_password_cannot_bind_it_to_the_owners_verification()
    {
        // [SECURITY] ใครก็ email/start ด้วยอีเมลของคนอื่นได้ (ยังไม่ได้พิสูจน์เจ้าของ) และ start ล่าสุดทับรหัสผ่านของแถว Pending —
        // ถ้า verify ดูแค่ OTP เจ้าของอีเมลที่กรอกรหัสล่าสุดจะเปิดอู่ที่ล็อกอินด้วยรหัสผ่านของผู้โจมตี
        const string victimPassword = "victim-pass1";
        const string attackerPassword = "attacker123";
        var h = Create();
        (await h.Service.StartEmailAsync(StartRequest(password: victimPassword), Client)).Success.Should().BeTrue();

        h.Clock.Advance(TimeSpan.FromSeconds(61));
        (await h.Service.StartEmailAsync(StartRequest(password: attackerPassword), Client)).Success.Should().BeTrue();
        h.Repo.Account(Normalized).PasswordHash.Should().Be("h$" + attackerPassword, "start ล่าสุดยังทับรหัสผ่านได้ตามเดิม");
        var newestCode = h.LastCode;

        // เจ้าของอีเมลกรอกรหัสล่าสุด (ที่ผู้โจมตีสั่งส่ง) พร้อมรหัสผ่านของตัวเอง → ไม่ผ่าน และไม่มี token ให้เปิดอู่
        var victim = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, newestCode, victimPassword), Client);
        victim.Error!.Code.Should().Be(SignupErrors.PasswordMismatchCode);
        victim.Error.Field.Should().Be("password");
        victim.Error.MessageTh.Should().Be("รหัสผ่านไม่ตรงกับที่ตั้งไว้ตอนเริ่มสมัคร กรุณาตรวจสอบหรือเริ่มสมัครใหม่");
        SignupErrors.StatusCodeFor(victim.Error.Code).Should().Be(422);
        var pending = h.Repo.Account(Normalized);
        pending.Status.Should().Be(SelfServeAccountStatus.Pending);
        pending.SetupTokenHash.Should().BeNull();
        h.Repo.Otps.Single().Attempts.Should().Be(1, "รหัสผ่านผิดนับเป็นการลองผิดหนึ่งครั้ง");

        // ผู้โจมตีไม่มีรหัสจากกล่องจดหมายของเจ้าของ — เห็นแค่ "รหัสไม่ถูกต้อง"
        var wrongCode = newestCode == "000000" ? "111111" : "000000";
        var attacker = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, wrongCode, attackerPassword), Client);
        attacker.Error!.Code.Should().Be(SelfServeErrors.SignupOtpInvalidCode);

        // เจ้าของเริ่มสมัครใหม่ → รหัสผ่านของตัวเองกลับมา แล้วยืนยันผ่าน เปิดอู่ด้วยรหัสผ่านของเจ้าของ
        h.Clock.Advance(TimeSpan.FromSeconds(61));
        (await h.Service.StartEmailAsync(StartRequest(password: victimPassword), Client)).Success.Should().BeTrue();
        var verified = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, h.LastCode, victimPassword), Client);
        verified.Success.Should().BeTrue();
        (await h.Service.CreateWorkspaceAsync(WorkspaceRequest(verified.Data!.SignupToken), Client)).Success.Should().BeTrue();
        h.Provisioner.Requests.Single().OwnerStoredPassword.Should().Be("h$" + victimPassword);
    }

    [Fact]
    public async Task Password_mismatch_counts_down_and_locks_like_a_wrong_code()
    {
        var h = Create();
        (await h.Service.StartEmailAsync(StartRequest(), Client)).Success.Should().BeTrue();
        var code = h.LastCode;

        for (var left = 4; left >= 1; left--)
        {
            var attempt = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, code, "not-mine99"), Client);
            attempt.Error!.Code.Should().Be(SignupErrors.PasswordMismatchCode);
            attempt.Error.Details.Should().BeEquivalentTo(new { attemptsLeft = left });
        }

        var fifth = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, code, "not-mine99"), Client);
        fifth.Error!.Code.Should().Be(SelfServeErrors.SignupOtpLockedCode);

        var right = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, code, Password), Client);
        right.Error!.Code.Should().Be(SelfServeErrors.SignupOtpLockedCode, "ล็อกแล้วต้องขอรหัสใหม่ แม้รหัสและรหัสผ่านถูก");
        h.Repo.Account(Normalized).Status.Should().Be(SelfServeAccountStatus.Pending);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Verify_requires_the_password_without_spending_an_attempt(string? password)
    {
        var h = Create();
        (await h.Service.StartEmailAsync(StartRequest(), Client)).Success.Should().BeTrue();

        var result = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest(Email, h.LastCode, password), Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupValidationCode);
        result.Error.Field.Should().Be("password");
        h.Repo.Otps.Single().Attempts.Should().Be(0);
    }

    [Fact]
    public async Task Wrong_password_with_a_wrong_code_never_reveals_whether_the_email_has_an_account()
    {
        // [SECURITY] ตรวจ OTP ก่อนรหัสผ่าน — คนที่ไม่มีรหัสจากอีเมลได้คำตอบเดียวกันทั้งอีเมลใหม่และอีเมลที่มีบัญชีแล้ว
        // (บัญชีที่มีอยู่ไม่เก็บรหัสผ่านจาก start ถ้าตรวจรหัสผ่านก่อนจะตอบต่างกันแล้วไล่เช็คอีเมลได้)
        var h = Create();
        h.Repo.Accounts.Add(new SelfServeAccount
        {
            NormalizedEmail = "taken@example.com", Email = "taken@example.com", Status = SelfServeAccountStatus.Active,
            PasswordHash = "h$old-password1", CreatedAt = Start.UtcDateTime,
        });
        (await h.Service.StartEmailAsync(StartRequest("taken@example.com", "probe-pass1"), Client)).Success.Should().BeTrue();
        (await h.Service.StartEmailAsync(StartRequest("fresh@example.com", "probe-pass1"), Client)).Success.Should().BeTrue();
        var wrongCode = h.LastCode == "000000" ? "111111" : "000000";

        var taken = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest("taken@example.com", wrongCode, "other-pass2"), Client);
        var fresh = await h.Service.VerifyEmailAsync(new EmailSignupVerifyRequest("fresh@example.com", wrongCode, "other-pass2"), Client);

        taken.Error!.Code.Should().Be(SelfServeErrors.SignupOtpInvalidCode);
        fresh.Error!.Code.Should().Be(SelfServeErrors.SignupOtpInvalidCode);
        taken.Error.MessageTh.Should().Be(fresh.Error.MessageTh);
    }

    // ---------------------------------------------------------------- google

    [Fact]
    public async Task Google_signup_creates_a_verified_account_without_password()
    {
        var h = Create();

        var result = await h.Service.GoogleAsync(new GoogleSignupRequest("id-token", "turnstile", null), Client);

        result.Success.Should().BeTrue();
        var account = h.Repo.Account("ownereak@gmail.com");
        account.Provider.Should().Be(SelfServeAccountProvider.Google);
        account.GoogleSubject.Should().Be("google-sub-1");
        account.Status.Should().Be(SelfServeAccountStatus.Verified);
        account.EmailVerifiedAt.Should().NotBeNull();
        account.PasswordHash.Should().BeNull();
        account.SetupTokenHash.Should().Be(SignupService.HashToken(result.Data!.SignupToken));
        h.Email.Sent.Should().BeEmpty("Google ยืนยันอีเมลแล้ว ไม่ต้องส่ง OTP");
    }

    [Fact]
    public async Task Google_signup_for_an_existing_active_account_says_so()
    {
        var h = Create();
        h.Repo.Accounts.Add(new SelfServeAccount
        {
            NormalizedEmail = "ownereak@gmail.com", Status = SelfServeAccountStatus.Active, CreatedAt = Start.UtcDateTime,
        });

        var result = await h.Service.GoogleAsync(new GoogleSignupRequest("id-token", "turnstile", null), Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupAccountExistsCode);
        SignupErrors.StatusCodeFor(result.Error.Code).Should().Be(409);
    }

    [Fact]
    public async Task Google_rejects_invalid_tokens_and_unverified_emails()
    {
        var h = Create();
        h.Google.Identity = null;
        (await h.Service.GoogleAsync(new GoogleSignupRequest("bad", "t", null), Client)).Error!.Code
            .Should().Be(SelfServeErrors.SignupGoogleInvalidCode);

        h.Google.Identity = new GoogleIdentity("sub-2", "someone@gmail.com", false, null, null, null);
        (await h.Service.GoogleAsync(new GoogleSignupRequest("unverified", "t", null), Client)).Error!.Code
            .Should().Be(SelfServeErrors.SignupGoogleInvalidCode);

        h.Repo.Accounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Google_signup_discards_a_password_set_by_an_unverified_email_start()
    {
        // [SECURITY] ใครก็ email/start ด้วยอีเมลคนอื่นได้ — รหัสผ่านนั้นต้องไม่ติดไปกับอู่ที่เจ้าของอีเมลเปิดด้วย Google
        var h = Create();
        (await h.Service.StartEmailAsync(StartRequest("Owner.Eak@gmail.com", "attacker123"), Client)).Success.Should().BeTrue();

        var google = await h.Service.GoogleAsync(new GoogleSignupRequest("id-token", "t", null), Client);
        google.Success.Should().BeTrue();
        h.Repo.Account("ownereak@gmail.com").PasswordHash.Should().BeNull();

        var workspace = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(google.Data!.SignupToken), Client);
        workspace.Success.Should().BeTrue();
        var provision = h.Provisioner.Requests.Single();
        provision.OwnerStoredPassword.Should().StartWith("h$").And.NotBe("h$attacker123");
        provision.OwnerFirstName.Should().Be("เอก", "ชื่อเจ้าของมาจาก Google");
        provision.OwnerLastName.Should().Be("ใจดี");
        h.Notifier.Sent.Single().Text.Should().Contain("ช่องทาง: Google");
    }

    // ---------------------------------------------------------------- workspace

    [Fact]
    public async Task Workspace_provisions_loads_samples_notifies_sales_and_returns_a_session()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);

        var result = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token), Client);

        result.Success.Should().BeTrue();
        var data = result.Data!;
        data.WorkspaceNo.Should().Be("SP1001");
        data.AccessToken.Should().Be("access-token");
        data.BranchId.Should().Be(9001);
        data.SampleTechnicians.Should().HaveCount(2);

        var request = h.Provisioner.Requests.Single();
        request.PhoneE164.Should().Be("+66812345678");
        request.OwnerStoredPassword.Should().Be("h$" + Password, "เจ้าของเข้าด้วยรหัสผ่านที่ตั้งตอนสมัคร");
        request.OwnerEmail.Should().Be(Email);
        request.OwnerFirstName.Should().Be("Owner.Eak");
        request.ProvinceId.Should().Be(38);
        request.Size.Should().Be("1-3");
        request.LoadSampleData.Should().BeTrue();

        h.Starter.Loaded.Should().ContainSingle();
        h.Sessions.Calls.Should().ContainSingle().Which.Should().Be(("sp1", 500 + 1001L));
        var account = h.Repo.Account(Normalized);
        account.Status.Should().Be(SelfServeAccountStatus.Active);
        account.LoginUserName.Should().Be("SP1001.M01");

        var (key, text) = h.Notifier.Sent.Single();
        key.Should().Be(h.Repo.Workspaces.Single().Id, "retry key = workspace id");
        text.Should().StartWith("🆕 สมัครทดลองใช้ ServicePro")
            .And.Contain("อู่: อู่ช่างเอก (SP1001)")
            .And.Contain("โทร: 081-234-5678")
            .And.Contain("จังหวัด: เชียงใหม่")
            .And.Contain("ขนาด: 1-3 ช่าง")
            .And.Contain($"อีเมล: {Email}")
            .And.Contain("ช่องทาง: อีเมล · ข้อมูลตัวอย่าง: ใส่")
            .And.NotContain("⚠️");

        var welcome = h.Email.Sent.Single(m => m.Tag == "welcome");
        welcome.To.Should().Be(Email);
        welcome.TextBody.Should().Contain("SP1001").And.Contain("SP1001.T01").And.Contain("Tech-pass-1");
        h.Repo.Attempts.Should().Contain(a => a.Step == SignupService.StepWorkspace && a.Outcome == "ok");
    }

    [Fact]
    public async Task Replaying_the_same_token_and_request_reissues_the_session_without_provisioning_again()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);
        var request = WorkspaceRequest(token);
        (await h.Service.CreateWorkspaceAsync(request, Client)).Success.Should().BeTrue();

        var replay = await h.Service.CreateWorkspaceAsync(request, Client);

        replay.Success.Should().BeTrue();
        replay.Data!.WorkspaceNo.Should().Be("SP1001");
        replay.Data.SampleTechnicians.Should().BeNull("รหัสผ่านเก็บเป็น hash คืนซ้ำไม่ได้ (อยู่ในอีเมลต้อนรับแล้ว)");
        h.Provisioner.Requests.Should().ContainSingle();
        h.Notifier.Sent.Should().ContainSingle();
        h.Email.Sent.Count(m => m.Tag == "welcome").Should().Be(1);
        h.Sessions.Calls.Should().HaveCount(2);
        h.Repo.Attempts.Should().Contain(a => a.Outcome == SignupService.OutcomeReplay);
    }

    [Fact]
    public async Task Token_cannot_open_a_second_workspace_with_another_request_id()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);
        (await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token), Client)).Success.Should().BeTrue();

        var second = await h.Service.CreateWorkspaceAsync(
            WorkspaceRequest(token, requestId: "99999999-8888-7777-6666-555555555555"), Client);

        second.Error!.Code.Should().Be(SelfServeErrors.SignupTokenInvalidCode);
        SignupErrors.StatusCodeFor(second.Error.Code).Should().Be(401);
        h.Provisioner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Token_can_be_reclaimed_by_a_new_request_after_provisioning_failed()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);
        h.Provisioner.Fails = true;

        var failed = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token), Client);
        failed.Error!.Code.Should().Be(SelfServeErrors.SignupProvisionFailedCode);
        SignupErrors.StatusCodeFor(failed.Error.Code).Should().Be(503);
        h.Repo.Account(Normalized).Status.Should().Be(SelfServeAccountStatus.Verified);

        h.Provisioner.Fails = false;
        var retried = await h.Service.CreateWorkspaceAsync(
            WorkspaceRequest(token, requestId: "77777777-6666-5555-4444-333333333333"), Client);

        retried.Success.Should().BeTrue();
        h.Provisioner.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Provisioning_exception_returns_provision_failed()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);
        h.Provisioner.Throws = true;

        var result = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token), Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupProvisionFailedCode);
        h.Sessions.Calls.Should().BeEmpty();
        h.Notifier.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Session_failure_after_provisioning_returns_provision_failed()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);
        h.Sessions.Fails = true;

        var result = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token), Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupProvisionFailedCode);
        h.Repo.Attempts.Should().Contain(a => a.Reason == "session:AUTH_NOT_IMPLEMENTED");
    }

    [Fact]
    public async Task Duplicate_phone_is_flagged_for_sales_but_never_blocks()
    {
        var h = Create();
        h.Repo.Workspaces.Add(new Workspace
        {
            WorkspaceNo = 900, Name = "อู่เดิม", Phone = "+66812345678", OwnerAccountId = Guid.NewGuid(),
            Status = WorkspaceStatus.Active,
        });
        var token = await VerifiedTokenAsync(h);

        var result = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token, phone: "+66 81 234 5678"), Client);

        result.Success.Should().BeTrue();
        h.Repo.Workspaces.Single(w => w.Name == "อู่ช่างเอก").DuplicatePhoneFlag.Should().BeTrue();
        h.Notifier.Sent.Single().Text.Should().EndWith("⚠️ เบอร์นี้เคยสมัครแล้ว");
    }

    [Fact]
    public async Task Starter_data_failure_does_not_fail_signup()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);
        h.Starter.Throws = true;

        var result = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token), Client);

        result.Success.Should().BeTrue();
        result.Data!.SampleTechnicians.Should().BeNull();
        h.Notifier.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Without_sample_data_the_starter_is_not_called()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);

        var result = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token, sampleData: false), Client);

        result.Success.Should().BeTrue();
        h.Starter.Loaded.Should().BeEmpty();
        h.Notifier.Sent.Single().Text.Should().Contain("ข้อมูลตัวอย่าง: ไม่ใส่");
    }

    [Fact]
    public async Task Expired_or_unknown_tokens_are_rejected()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);

        (await h.Service.CreateWorkspaceAsync(WorkspaceRequest(SignupService.NewSignupToken()), Client)).Error!.Code
            .Should().Be(SelfServeErrors.SignupTokenInvalidCode);

        h.Clock.Advance(SignupService.SignupTokenLifetime + TimeSpan.FromSeconds(1));
        (await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token), Client)).Error!.Code
            .Should().Be(SelfServeErrors.SignupTokenInvalidCode);
        h.Provisioner.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Typed_owner_name_beats_the_email_local_part_and_splits_first_and_last_name()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);

        var result = await h.Service.CreateWorkspaceAsync(
            WorkspaceRequest(token) with { OwnerName = "  สมชาย   ใจดี มาก " }, Client);

        result.Success.Should().BeTrue();
        var request = h.Provisioner.Requests.Single();
        request.OwnerFirstName.Should().Be("สมชาย");
        request.OwnerLastName.Should().Be("ใจดี มาก");
    }

    [Theory]
    [InlineData("ก")]
    public async Task Too_short_owner_name_is_rejected_before_touching_the_token(string ownerName)
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);

        var result = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token) with { OwnerName = ownerName }, Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupValidationCode);
        result.Error.Field.Should().Be("ownerName");
        h.Provisioner.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData("0212", "phone")]
    [InlineData("0812345678", "size")]
    public async Task Workspace_validates_fields_before_touching_the_token(string phone, string field)
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);
        var request = WorkspaceRequest(token, phone: phone) with { Size = field == "size" ? "huge" : "1-3" };

        var result = await h.Service.CreateWorkspaceAsync(request, Client);

        result.Error!.Code.Should().Be(SelfServeErrors.SignupValidationCode);
        result.Error.Field.Should().Be(field);
        h.Repo.Account(Normalized).SetupTokenHash.Should().NotContain(":", "token ยังไม่ถูกจอง");
    }

    [Fact]
    public async Task Unknown_province_is_rejected()
    {
        var h = Create();
        var token = await VerifiedTokenAsync(h);

        var result = await h.Service.CreateWorkspaceAsync(WorkspaceRequest(token) with { ProvinceId = 999 }, Client);

        result.Error!.Field.Should().Be("provinceId");
    }

    [Fact]
    public async Task Disabled_signup_rejects_every_step()
    {
        var h = Create(configure: o => o.Enabled = false);

        (await h.Service.StartEmailAsync(StartRequest(), Client)).Error!.Code.Should().Be(SelfServeErrors.SignupDisabledCode);
        (await h.Service.GoogleAsync(new GoogleSignupRequest("t", "t", null), Client)).Error!.Code
            .Should().Be(SelfServeErrors.SignupDisabledCode);
        (await h.Service.CreateWorkspaceAsync(WorkspaceRequest("x"), Client)).Error!.Code
            .Should().Be(SelfServeErrors.SignupDisabledCode);
        (await h.Service.GetProvincesAsync()).Error!.Code.Should().Be(SelfServeErrors.SignupDisabledCode);
        SignupErrors.StatusCodeFor(SelfServeErrors.SignupDisabledCode).Should().Be(503);
    }
}
