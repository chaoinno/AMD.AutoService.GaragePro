using AMD.AutoService.GaragePro.Application.Auth;
using AMD.AutoService.GaragePro.Infrastructure.Auth;
using FluentAssertions;

namespace AMD.AutoService.GaragePro.Tests;

/// <summary>B3 — ตัวนับ login ผิดต่อ username (แผน §3: ผิด 10 ครั้ง/15 นาที → บล็อกจนหมด window)</summary>
public sealed class MemoryLoginAttemptLimiterTests
{
    private const string Key = "db2:employee01";

    [Fact]
    public async Task Blocks_only_after_the_tenth_failure()
    {
        using var limiter = new MemoryLoginAttemptLimiter(new MovableClock());

        for (var i = 0; i < LoginAttemptPolicy.MaxFailures - 1; i++)
            await limiter.RegisterFailureAsync(Key);
        (await limiter.IsBlockedAsync(Key)).Should().BeFalse("ผิด 9 ครั้งยังลองได้");

        await limiter.RegisterFailureAsync(Key);

        (await limiter.IsBlockedAsync(Key)).Should().BeTrue();
    }

    [Fact]
    public async Task Keys_are_counted_independently()
    {
        using var limiter = new MemoryLoginAttemptLimiter(new MovableClock());

        for (var i = 0; i < LoginAttemptPolicy.MaxFailures; i++)
            await limiter.RegisterFailureAsync(Key);

        (await limiter.IsBlockedAsync(Key)).Should().BeTrue();
        (await limiter.IsBlockedAsync("db2:employee02")).Should().BeFalse();
        (await limiter.IsBlockedAsync("db1:employee01")).Should().BeFalse("shard อื่นเป็นคนละบัญชี");
    }

    [Fact]
    public async Task Reset_clears_the_counter()
    {
        using var limiter = new MemoryLoginAttemptLimiter(new MovableClock());
        for (var i = 0; i < LoginAttemptPolicy.MaxFailures; i++)
            await limiter.RegisterFailureAsync(Key);

        await limiter.ResetAsync(Key);

        (await limiter.IsBlockedAsync(Key)).Should().BeFalse();
        // ตัวนับเริ่มจากศูนย์จริง ไม่ใช่แค่ปลดบล็อกครั้งเดียว
        await limiter.RegisterFailureAsync(Key);
        (await limiter.IsBlockedAsync(Key)).Should().BeFalse();
    }

    [Fact]
    public async Task Block_lifts_when_the_window_that_started_at_the_first_failure_ends()
    {
        var clock = new MovableClock();
        using var limiter = new MemoryLoginAttemptLimiter(clock);

        await limiter.RegisterFailureAsync(Key);                 // window เริ่มที่นี่
        clock.Advance(TimeSpan.FromMinutes(10));
        for (var i = 1; i < LoginAttemptPolicy.MaxFailures; i++)
            await limiter.RegisterFailureAsync(Key);
        (await limiter.IsBlockedAsync(Key)).Should().BeTrue();

        clock.Advance(TimeSpan.FromMinutes(5) - TimeSpan.FromSeconds(1));
        (await limiter.IsBlockedAsync(Key)).Should().BeTrue("ยังไม่ครบ 15 นาทีนับจากครั้งแรก");

        clock.Advance(TimeSpan.FromSeconds(1));
        (await limiter.IsBlockedAsync(Key)).Should().BeFalse();
    }

    // ผิดกระจายเกิน window ต้องไม่สะสมข้ามรอบ — คนพิมพ์ผิดวันละไม่กี่ครั้งต้องไม่โดนล็อก
    [Fact]
    public async Task Failures_from_an_expired_window_do_not_carry_over()
    {
        var clock = new MovableClock();
        using var limiter = new MemoryLoginAttemptLimiter(clock);

        for (var i = 0; i < LoginAttemptPolicy.MaxFailures - 1; i++)
            await limiter.RegisterFailureAsync(Key);
        clock.Advance(LoginAttemptPolicy.Window);

        await limiter.RegisterFailureAsync(Key);

        (await limiter.IsBlockedAsync(Key)).Should().BeFalse();
    }

    // [SECURITY] username สุ่มจำนวนมากต้องไม่กินหน่วยความจำไม่จำกัด — เต็มแล้ว key ใหม่ไม่ถูกนับ (fail-open)
    [Fact]
    public async Task Capacity_is_bounded()
    {
        using var limiter = new MemoryLoginAttemptLimiter(new MovableClock(),
            maxFailures: 1, window: TimeSpan.FromMinutes(15), maxEntries: 2);

        await limiter.RegisterFailureAsync("a");
        await limiter.RegisterFailureAsync("b");
        await limiter.RegisterFailureAsync("c");

        // ไม่ assert a/b — MemoryCache เก็บกวาดส่วนเกินใน background ได้ (สุ่มว่าตัวไหนออก) สิ่งที่รับประกันคือ c ไม่ถูกเก็บ
        (await limiter.IsBlockedAsync("c")).Should().BeFalse();
    }

    [Fact]
    public async Task Concurrent_failures_are_all_counted()
    {
        using var limiter = new MemoryLoginAttemptLimiter(new MovableClock(),
            maxFailures: 200, window: TimeSpan.FromMinutes(15));

        await Task.WhenAll(Enumerable.Range(0, 199).Select(_ => Task.Run(() => limiter.RegisterFailureAsync(Key))));
        (await limiter.IsBlockedAsync(Key)).Should().BeFalse();

        await limiter.RegisterFailureAsync(Key);
        (await limiter.IsBlockedAsync(Key)).Should().BeTrue("นับครบ 200 ไม่หายจากการแข่งกันสร้างตัวนับ");
    }

    [Theory]
    [InlineData("Employee01", "db2:employee01")]
    [InlineData("  employee01\t", "db2:employee01")]
    [InlineData("SP1001.T03", "db2:sp1001.t03")]
    public void Key_is_trimmed_and_lowercased_per_shard(string userName, string expected) =>
        LoginAttemptPolicy.KeyFor("db2", userName).Should().Be(expected);

    private sealed class MovableClock : TimeProvider
    {
        private DateTimeOffset now = new(2026, 10, 14, 3, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }
}
