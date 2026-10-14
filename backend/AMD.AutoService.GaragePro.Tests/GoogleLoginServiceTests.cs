using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Domain.Entities.SelfServe;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public sealed class GoogleLoginServiceTests
{
    private static readonly GoogleIdentity Verified =
        new("google-sub-1", "Owner.Garage@gmail.com", true, "Owner", "Garage", null);

    [Fact]
    public async Task Missing_validator_means_google_login_is_disabled()
    {
        var auth = new FakeAuthService();
        var service = new GoogleLoginService(new MapServiceProvider(), new FakeAccounts(), auth, "sp1");

        var result = await service.LoginAsync(new GoogleLoginRequest("token"));

        result.Error!.Code.Should().Be(SelfServeAuthErrors.GoogleDisabledCode);
        auth.Issued.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task Blank_token_is_invalid(string? token)
    {
        var result = await Service(new FakeValidator(Verified), new FakeAccounts()).LoginAsync(new GoogleLoginRequest(token));
        result.Error!.Code.Should().Be(SelfServeErrors.SignupGoogleInvalidCode);
    }

    [Fact]
    public async Task Rejected_or_unverified_tokens_are_invalid()
    {
        (await Service(new FakeValidator(null), new FakeAccounts()).LoginAsync(new GoogleLoginRequest("t")))
            .Error!.Code.Should().Be(SelfServeErrors.SignupGoogleInvalidCode);
        (await Service(new FakeValidator(Verified with { EmailVerified = false }), new FakeAccounts())
                .LoginAsync(new GoogleLoginRequest("t")))
            .Error!.Code.Should().Be(SelfServeErrors.SignupGoogleInvalidCode);
    }

    [Fact]
    public async Task Unknown_email_is_not_registered()
    {
        var result = await Service(new FakeValidator(Verified), new FakeAccounts()).LoginAsync(new GoogleLoginRequest("t"));

        result.Error!.Code.Should().Be(SelfServeAuthErrors.GoogleNotRegisteredCode);
        result.Error.MessageTh.Should().Contain("ทดลองใช้ฟรี");
    }

    [Fact]
    public async Task Google_account_signs_in_as_its_sp1_user()
    {
        var auth = new FakeAuthService();
        var accounts = new FakeAccounts
        {
            Account = Account(SelfServeAccountProvider.Google, "google-sub-1", emailVerified: true)
        };

        var result = await new GoogleLoginService(Provider(new FakeValidator(Verified)), accounts, auth, "sp1")
            .LoginAsync(new GoogleLoginRequest(" t "));

        result.Success.Should().BeTrue();
        accounts.LookedUp.Should().Equal("ownergarage@gmail.com");
        auth.Issued.Should().Equal(("sp1", 9001L));
    }

    [Fact]
    public async Task Google_subject_mismatch_is_not_accepted()
    {
        var accounts = new FakeAccounts
        {
            Account = Account(SelfServeAccountProvider.Google, "someone-else", emailVerified: true)
        };

        (await Service(new FakeValidator(Verified), accounts).LoginAsync(new GoogleLoginRequest("t")))
            .Error!.Code.Should().Be(SelfServeAuthErrors.GoogleNotRegisteredCode);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task Email_account_needs_a_verified_email(bool emailVerified, bool succeeds)
    {
        var auth = new FakeAuthService();
        var accounts = new FakeAccounts { Account = Account(SelfServeAccountProvider.Email, null, emailVerified) };

        var result = await new GoogleLoginService(Provider(new FakeValidator(Verified)), accounts, auth, "sp1")
            .LoginAsync(new GoogleLoginRequest("t"));

        result.Success.Should().Be(succeeds);
        if (!succeeds) result.Error!.Code.Should().Be(SelfServeAuthErrors.GoogleNotRegisteredCode);
    }

    [Fact]
    public async Task Account_without_a_user_yet_is_not_registered()
    {
        var accounts = new FakeAccounts
        {
            Account = Account(SelfServeAccountProvider.Google, "google-sub-1", true) with { UserId = null }
        };

        (await Service(new FakeValidator(Verified), accounts).LoginAsync(new GoogleLoginRequest("t")))
            .Error!.Code.Should().Be(SelfServeAuthErrors.GoogleNotRegisteredCode);
    }

    private static GoogleLoginService Service(FakeValidator validator, FakeAccounts accounts) =>
        new(Provider(validator), accounts, new FakeAuthService(), "sp1");

    private static MapServiceProvider Provider(FakeValidator validator) =>
        new MapServiceProvider().Add<IGoogleIdTokenValidator>(validator);

    private static SelfServeLoginAccount Account(SelfServeAccountProvider provider, string? subject, bool emailVerified) =>
        new(Guid.NewGuid(), "ownergarage@gmail.com", provider, subject, emailVerified, "SP1001.M01", 9001);

    private sealed class FakeValidator(GoogleIdentity? identity) : IGoogleIdTokenValidator
    {
        public Task<GoogleIdentity?> ValidateAsync(string idToken, CancellationToken ct = default) => Task.FromResult(identity);
    }

    private sealed class FakeAccounts : ISelfServeAccountReader
    {
        public SelfServeLoginAccount? Account { get; init; }
        public List<string> LookedUp { get; } = [];

        public Task<SelfServeLoginAccount?> FindActiveByNormalizedEmailAsync(string normalizedEmail, CancellationToken ct = default)
        {
            LookedUp.Add(normalizedEmail);
            return Task.FromResult(Account?.NormalizedEmail == normalizedEmail ? Account : null);
        }

        public Task MarkMobileLoginSeenAsync(int branchId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeAuthService : IAuthService
    {
        public List<(string Shard, long UserId)> Issued { get; } = [];

        public Task<Result<LoginResultDto>> IssueSessionForUserAsync(string shardKey, long userId, CancellationToken ct = default)
        {
            Issued.Add((shardKey, userId));
            return Task.FromResult(Result<LoginResultDto>.Ok(new LoginResultDto("token", DateTime.UtcNow.AddHours(12),
                new AuthUserDto(userId, "SP1001.M01", "เจ้าของ", "Manager", "ผู้จัดการสาขา", false, shardKey, 1, null, null,
                    true, true), 5001, "อู่", [], false)));
        }

        public Task<Result<LoginResultDto>> LoginAsync(LoginRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<ShiftOptionDto>>> GetShiftsAsync(int branchId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<ShiftSessionDto>> OpenShiftAsync(OpenShiftRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<bool>> CloseShiftAsync(Guid sessionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<MeDto>> GetMeAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<IReadOnlyList<SwitchableBranchDto>>> GetSwitchableBranchesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result<LoginResultDto>> SwitchBranchAsync(SwitchBranchRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<string?> GetSessionRevocationReasonAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}
