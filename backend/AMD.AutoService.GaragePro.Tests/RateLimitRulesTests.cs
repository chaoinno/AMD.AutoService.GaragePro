using System.Net;
using System.Reflection;
using System.Threading.RateLimiting;
using AMD.AutoService.GaragePro.API;
using AMD.AutoService.GaragePro.API.Controllers;
using AMD.AutoService.GaragePro.API.RateLimiting;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>B3 — กติกาของ rate limiter ชั้น ASP.NET (แผน §3 "Rate-limit policies")</summary>
public sealed class RateLimitRulesTests
{
    // ---------- จัดกลุ่มตาม path ----------

    [Theory]
    [InlineData("POST", "/api/v1/auth/login", RateLimitedRoute.Login)]
    // routing ของ ASP.NET ไม่สนตัวพิมพ์/"/" ท้าย — ถ้า limiter สน ผู้โจมตีเลี่ยงเพดานได้ด้วยการเปลี่ยนตัวพิมพ์
    [InlineData("POST", "/API/V1/AUTH/LOGIN", RateLimitedRoute.Login)]
    [InlineData("POST", "/api/v1/auth/login/", RateLimitedRoute.Login)]
    [InlineData("POST", "/api/v1/auth/google", RateLimitedRoute.Login)]
    [InlineData("GET", "/api/v1/auth/me", RateLimitedRoute.None)]
    [InlineData("POST", "/api/v1/public/signup/email/start", RateLimitedRoute.SignupEntry)]
    [InlineData("POST", "/api/v1/public/signup/google", RateLimitedRoute.SignupEntry)]
    [InlineData("OPTIONS", "/api/v1/public/signup/email/start", RateLimitedRoute.SignupOther)]
    [InlineData("POST", "/api/v1/public/signup/email/verify", RateLimitedRoute.SignupOtp)]
    [InlineData("POST", "/api/v1/public/signup/email/resend", RateLimitedRoute.SignupOtp)]
    [InlineData("GET", "/api/v1/public/signup/config", RateLimitedRoute.SignupOther)]
    [InlineData("GET", "/api/v1/public/signup/provinces", RateLimitedRoute.SignupOther)]
    [InlineData("POST", "/api/v1/public/signup/workspace", RateLimitedRoute.SignupOther)]
    [InlineData("POST", "/api/v1/public/contact-requests", RateLimitedRoute.PublicOther)]
    [InlineData("GET", "/api/v1/public/package-slips/3f2a9c1b", RateLimitedRoute.PublicPackageSlip)]
    [InlineData("GET", "/API/V1/PUBLIC/PACKAGE-SLIPS/x", RateLimitedRoute.PublicPackageSlip)]
    [InlineData("POST", "/api/v1/selfserve/package/payments", RateLimitedRoute.None)]
    [InlineData("GET", "/api/v1/publicity", RateLimitedRoute.None)]
    [InlineData("GET", "/api/v1/jobs", RateLimitedRoute.None)]
    public void Classifies_requests_by_method_and_path(string method, string path, RateLimitedRoute expected) =>
        RateLimitRules.Classify(method, path).Should().Be(expected);

    // ---------- key ต่อ IP ----------

    [Fact]
    public void Client_key_groups_ipv6_by_64_bit_prefix_and_unwraps_mapped_ipv4()
    {
        RateLimitRules.ClientKey(IPAddress.Parse("203.0.113.7")).Should().Be("203.0.113.7");
        RateLimitRules.ClientKey(IPAddress.Parse("::ffff:203.0.113.7")).Should().Be("203.0.113.7");
        RateLimitRules.ClientKey(IPAddress.Parse("2001:db8:1:2:aaaa::1"))
            .Should().Be(RateLimitRules.ClientKey(IPAddress.Parse("2001:db8:1:2:ffff:ffff:ffff:ffff")))
            .And.Be("2001:db8:1:2::/64");
        RateLimitRules.ClientKey(IPAddress.Parse("2001:db8:1:3::1")).Should().NotBe("2001:db8:1:2::/64");
        RateLimitRules.ClientKey(null).Should().Be("unknown");
    }

    // ---------- error code ตอนถูกปฏิเสธ ----------

    [Fact]
    public void Login_rejections_use_auth_rate_limited_whether_from_the_policy_or_the_global_hourly_cap()
    {
        var byPolicy = RateLimitRules.DescribeRejection(RateLimitPolicies.AuthLogin, "POST", "/api/v1/auth/login", TimeSpan.FromSeconds(10));
        var byPath = RateLimitRules.DescribeRejection(null, "POST", "/api/v1/auth/login", null);

        byPolicy.Should().Be(new RateLimitRejection("AUTH_RATE_LIMITED", "พยายามเข้าสู่ระบบถี่เกินไป กรุณารอสักครู่แล้วลองใหม่", 10));
        byPath.Code.Should().Be("AUTH_RATE_LIMITED");
        byPath.RetryAfterSeconds.Should().Be(60, "ไม่มี metadata ให้ใช้ความยาว window ของ policy");
    }

    [Theory]
    [InlineData(null, "POST", "/api/v1/public/signup/email/start")]
    [InlineData(null, "GET", "/api/v1/public/signup/config")]
    [InlineData(RateLimitPolicies.PublicSignup, "POST", "/api/v1/public/signup/google")]
    [InlineData(RateLimitPolicies.PublicOtp, "POST", "/api/v1/public/signup/email/verify")]
    public void Signup_rejections_use_signup_rate_limited_with_the_line_contact(string? policy, string method, string path)
    {
        var rejection = RateLimitRules.DescribeRejection(policy, method, path, TimeSpan.FromHours(1));

        rejection.Code.Should().Be("SIGNUP_RATE_LIMITED");
        rejection.MessageTh.Should().Contain("ส่งคำขอสมัครถี่เกินไป").And.Contain("LINE @garagepro").And.Contain("60 นาที");
        rejection.RetryAfterSeconds.Should().Be(3600);
    }

    [Fact]
    public void Existing_purchasing_pin_and_contact_rejections_keep_their_codes()
    {
        var pin = RateLimitRules.DescribeRejection(RateLimitPolicies.PurchasingPin, "POST", "/api/v1/purchase-orders/x/approve", null);
        var contact = RateLimitRules.DescribeRejection(RateLimitPolicies.PublicContact, "POST", "/api/v1/public/contact-requests", null);
        var contactGlobal = RateLimitRules.DescribeRejection(null, "POST", "/api/v1/public/contact-requests", TimeSpan.FromMinutes(59.5));

        pin.Code.Should().Be("PURCHASING_PIN_RATE_LIMIT");
        pin.RetryAfterSeconds.Should().Be(60);
        contact.Code.Should().Be("RATE_LIMITED");
        contact.MessageTh.Should().Contain("LINE @garagepro");
        contact.RetryAfterSeconds.Should().Be(600);
        contactGlobal.RetryAfterSeconds.Should().Be(3570, "metadata ปัดขึ้นเป็นวินาที");
    }

    [Fact]
    public void Login_endpoint_uses_the_auth_login_policy()
    {
        var attribute = typeof(AuthController).GetMethod(nameof(AuthController.Login))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();

        attribute!.PolicyName.Should().Be(RateLimitPolicies.AuthLogin);
    }

    // ---------- global limiter จริง ----------

    [Fact]
    public void Signup_entry_allows_three_per_hour_per_ip_without_blocking_the_rest_of_the_flow()
    {
        using var limiter = RateLimitingSetup.BuildGlobalLimiter();
        const string ip = "198.51.100.1";

        for (var i = 0; i < 3; i++)
            Acquire(limiter, "POST", "/api/v1/public/signup/email/start", ip).Should().BeTrue();

        using (var fourth = limiter.AttemptAcquire(Request("POST", "/api/v1/public/signup/google", ip)))
        {
            fourth.IsAcquired.Should().BeFalse();
            fourth.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter).Should().BeTrue();
            retryAfter.Should().BeGreaterThan(TimeSpan.Zero);
        }

        Acquire(limiter, "POST", "/api/v1/public/signup/email/start", "198.51.100.2").Should().BeTrue("IP อื่นเป็นคนละถัง");
        Acquire(limiter, "POST", "/api/v1/public/signup/email/verify", ip).Should().BeTrue("ยืนยัน OTP ไม่ใช้โควตาเริ่มสมัคร");
        Acquire(limiter, "POST", "/api/v1/public/signup/workspace", ip).Should().BeTrue();
    }

    [Fact]
    public void Every_signup_request_shares_a_per_ip_backstop()
    {
        using var limiter = RateLimitingSetup.BuildGlobalLimiter();
        const string ip = "198.51.100.3";

        for (var i = 0; i < RateLimitRules.SignupAnyPerHour; i++)
            Acquire(limiter, "GET", "/api/v1/public/signup/provinces", ip).Should().BeTrue();

        Acquire(limiter, "GET", "/api/v1/public/signup/config", ip).Should().BeFalse();
        Acquire(limiter, "GET", "/api/v1/jobs", ip).Should().BeTrue("endpoint อื่นไม่เกี่ยว");
    }

    [Fact]
    public void Otp_allows_ten_per_hour_per_ip()
    {
        using var limiter = RateLimitingSetup.BuildGlobalLimiter();
        const string ip = "198.51.100.4";

        for (var i = 0; i < RateLimitRules.OtpPerHour; i++)
            Acquire(limiter, "POST", "/api/v1/public/signup/email/verify", ip).Should().BeTrue();

        Acquire(limiter, "POST", "/api/v1/public/signup/email/resend", ip).Should().BeFalse();
    }

    [Fact]
    public void Login_is_capped_at_fifty_per_hour_per_ip()
    {
        using var limiter = RateLimitingSetup.BuildGlobalLimiter();
        const string ip = "198.51.100.5";

        for (var i = 0; i < RateLimitRules.LoginPerHour; i++)
            Acquire(limiter, "POST", "/api/v1/auth/login", ip).Should().BeTrue();

        Acquire(limiter, "POST", "/API/v1/auth/login", ip).Should().BeFalse();
        Acquire(limiter, "POST", "/api/v1/auth/login", "198.51.100.6").Should().BeTrue();
    }

    // ถังรวมของฟอร์มติดต่อเป็นแบบเดิม (ไม่แยก IP) แต่ signup ต้องไม่ถูกลากไปตันด้วย
    [Fact]
    public void Contact_form_keeps_the_shared_bucket_and_no_longer_starves_signup()
    {
        using var limiter = RateLimitingSetup.BuildGlobalLimiter();

        for (var i = 0; i < RateLimitRules.PublicGlobalPerHour; i++)
            Acquire(limiter, "POST", "/api/v1/public/contact-requests", $"203.0.113.{i % 250}").Should().BeTrue();

        Acquire(limiter, "POST", "/api/v1/public/contact-requests", "192.0.2.99").Should().BeFalse();
        Acquire(limiter, "POST", "/api/v1/public/signup/email/start", "192.0.2.99").Should().BeTrue();
    }

    // docs/17 §5.3: รูปสลิปที่ LINE ดึงมีถังต่อ IP ของตัวเอง ไม่กินโควตาฟอร์มติดต่อ และไม่ถูกฟอร์มติดต่อลากไปตัน
    [Fact]
    public void Package_slip_links_have_their_own_per_ip_bucket()
    {
        using var limiter = RateLimitingSetup.BuildGlobalLimiter();
        const string ip = "198.51.100.9";

        for (var i = 0; i < RateLimitRules.PackageSlipPerHour; i++)
            Acquire(limiter, "GET", $"/api/v1/public/package-slips/{i}", ip).Should().BeTrue();

        Acquire(limiter, "GET", "/api/v1/public/package-slips/x", ip).Should().BeFalse();
        Acquire(limiter, "GET", "/api/v1/public/package-slips/x", "198.51.100.10").Should().BeTrue("IP อื่นเป็นคนละถัง");
        Acquire(limiter, "POST", "/api/v1/public/contact-requests", ip).Should().BeTrue("ไม่แตะถัง public-global");
    }

    [Fact]
    public void Package_submit_rejection_is_rate_limited_with_an_hour_fallback()
    {
        var rejection = RateLimitRules.DescribeRejection(RateLimitPolicies.PackagePaymentSubmit, "POST",
            "/api/v1/selfserve/package/payments", null);

        rejection.Code.Should().Be("RATE_LIMITED");
        rejection.MessageTh.Should().Contain("แจ้งชำระ");
        rejection.RetryAfterSeconds.Should().Be(3600);
    }

    [Fact]
    public void Package_submit_endpoint_uses_its_policy()
    {
        var attribute = typeof(SelfServePackageController).GetMethod(nameof(SelfServePackageController.Submit))!
            .GetCustomAttribute<EnableRateLimitingAttribute>();
        attribute!.PolicyName.Should().Be(RateLimitPolicies.PackagePaymentSubmit);
        RateLimitRules.PackagePaymentSubmitPerHour.Should().Be(10);
    }

    private static bool Acquire(PartitionedRateLimiter<HttpContext> limiter, string method, string path, string ip)
    {
        using var lease = limiter.AttemptAcquire(Request(method, path, ip));
        return lease.IsAcquired;
    }

    private static HttpContext Request(string method, string path, string ip)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        return context;
    }
}
