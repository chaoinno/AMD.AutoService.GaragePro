using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Domain.Enums;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

public class ScopedCurrentUserTests
{
    [Fact]
    public void Defaults_are_manager_from_system_without_session_or_admin()
    {
        var user = new ScopedCurrentUser("sp1", 1001, 42, "SP1001.M01");

        user.ShardKey.Should().Be("sp1");
        user.BranchId.Should().Be(1001);
        user.UserId.Should().Be(42);
        user.UserName.Should().Be("SP1001.M01");
        user.Role.Should().Be(UserRole.Manager);
        user.Source.Should().Be(EventSource.System);
        user.SessionId.Should().BeNull();
        user.IsAdministrator.Should().BeFalse("sp1 ห้ามมี admin");
        user.StaffId.Should().BeNull();
        user.Plan.Should().BeNull();
        user.PlanExpiresAt.Should().BeNull();
        user.CanSeeCost.Should().BeTrue();
    }

    [Fact]
    public void Explicit_values_are_exposed_through_the_interface()
    {
        var expires = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
        ICurrentUser user = new ScopedCurrentUser("db2", 227, 7, "service",
            role: UserRole.Technician, source: EventSource.Web, staffId: 99,
            plan: PlanKind.Trial, planExpiresAt: expires);

        user.Role.Should().Be(UserRole.Technician);
        user.Source.Should().Be(EventSource.Web);
        user.StaffId.Should().Be(99);
        user.Plan.Should().Be(PlanKind.Trial);
        user.PlanExpiresAt.Should().Be(expires);
        user.CanSeeCost.Should().BeFalse();
    }

    [Theory]
    [InlineData(UserRole.Manager, true)]
    [InlineData(UserRole.Office, false)]
    [InlineData(UserRole.Cashier, false)]
    [InlineData(UserRole.FrontDesk, false)]
    [InlineData(UserRole.Technician, false)]
    [InlineData(UserRole.Lead, false)]
    public void CanSeeCost_follows_the_same_rule_as_the_interface_default(UserRole role, bool expected)
    {
        var user = new ScopedCurrentUser("sp1", 1, 1, "u", role);

        user.CanSeeCost.Should().Be(expected);
        ((ICurrentUser)user).CanSeeCost.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_shard_key_is_rejected(string shardKey)
    {
        var act = () => new ScopedCurrentUser(shardKey, 1, 1, "u");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Existing_stubs_without_plan_members_still_compile_and_report_no_plan()
    {
        ICurrentUser legacyStub = new MinimalStub();

        legacyStub.Plan.Should().BeNull();
        legacyStub.PlanExpiresAt.Should().BeNull();
        legacyStub.StaffId.Should().BeNull();
    }

    /// <summary>รูปแบบเดียวกับ StubCurrentUser ใน 19 ไฟล์เทสต์เดิม — ต้องไม่ต้องแก้เมื่อเพิ่ม default member</summary>
    private sealed class MinimalStub : ICurrentUser
    {
        public long UserId => 1;
        public string UserName => "stub";
        public UserRole Role => UserRole.FrontDesk;
        public string ShardKey => "db2";
        public int BranchId => 227;
        public EventSource Source => EventSource.Web;
        public bool IsAdministrator => false;
        public Guid? SessionId => null;
    }
}
