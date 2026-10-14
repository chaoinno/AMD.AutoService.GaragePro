using System.Security.Cryptography;
using AMD.AutoService.GaragePro.Application.SelfServe;
using AMD.AutoService.GaragePro.Infrastructure.SelfServe;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>ตรวจด้วยกุญแจ RSA ที่สร้างในเทสต์ + configuration แบบ static — ไม่ต่อ accounts.google.com</summary>
public sealed class GoogleIdTokenValidatorTests : IDisposable
{
    private const string ClientId = "1234-test.apps.googleusercontent.com";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;

    public GoogleIdTokenValidatorTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "google-test-key" };
    }

    public void Dispose() => _rsa.Dispose();

    private GoogleIdTokenValidator Create(string clientId = ClientId, SecurityKey? publishedKey = null)
    {
        var options = new SelfServeOptions();
        options.Google.ClientId = clientId;
        var config = new OpenIdConnectConfiguration();
        config.SigningKeys.Add(publishedKey ?? _key);
        return new GoogleIdTokenValidator(Options.Create(options), NullLogger<GoogleIdTokenValidator>.Instance,
            new StaticConfigurationManager<OpenIdConnectConfiguration>(config));
    }

    private string Token(
        string issuer = "https://accounts.google.com", string audience = ClientId, object? emailVerified = null,
        DateTime? expires = null, SecurityKey? signingKey = null)
    {
        var now = DateTime.UtcNow;
        var exp = expires ?? now.AddMinutes(30);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = exp.AddMinutes(-60),
            NotBefore = exp.AddMinutes(-60),
            Expires = exp,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = "1098765432101234567890",
                ["email"] = "Owner.Eak@gmail.com",
                ["email_verified"] = emailVerified ?? true,
                ["given_name"] = "เอก",
                ["family_name"] = "ใจดี",
            },
            SigningCredentials = new SigningCredentials(signingKey ?? _key, SecurityAlgorithms.RsaSha256),
        });
    }

    [Theory]
    [InlineData("https://accounts.google.com")]
    [InlineData("accounts.google.com")]
    public async Task Accepts_a_valid_google_token_from_either_issuer_form(string issuer)
    {
        var identity = await Create().ValidateAsync(Token(issuer));

        identity.Should().NotBeNull();
        identity!.Subject.Should().Be("1098765432101234567890");
        identity.Email.Should().Be("Owner.Eak@gmail.com");
        identity.EmailVerified.Should().BeTrue();
        identity.GivenName.Should().Be("เอก");
        identity.FamilyName.Should().Be("ใจดี");
    }

    [Fact]
    public async Task Reports_unverified_email_as_not_verified()
    {
        var identity = await Create().ValidateAsync(Token(emailVerified: false));

        identity.Should().NotBeNull();
        identity!.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_a_token_for_another_client()
    {
        (await Create().ValidateAsync(Token(audience: "someone-else.apps.googleusercontent.com"))).Should().BeNull();
    }

    [Fact]
    public async Task Rejects_a_token_from_another_issuer()
    {
        (await Create().ValidateAsync(Token(issuer: "https://evil.example.com"))).Should().BeNull();
    }

    [Fact]
    public async Task Rejects_an_expired_token()
    {
        (await Create().ValidateAsync(Token(expires: DateTime.UtcNow.AddMinutes(-30)))).Should().BeNull();
    }

    [Fact]
    public async Task Rejects_a_token_signed_by_an_unknown_key()
    {
        using var other = RSA.Create(2048);
        var foreign = new RsaSecurityKey(other) { KeyId = "attacker-key" };

        (await Create().ValidateAsync(Token(signingKey: foreign))).Should().BeNull();
    }

    [Fact]
    public async Task Rejects_everything_when_google_is_not_configured()
    {
        (await Create(clientId: "").ValidateAsync(Token())).Should().BeNull();
        (await Create().ValidateAsync("")).Should().BeNull();
        (await Create().ValidateAsync("not-a-jwt")).Should().BeNull();
    }
}
