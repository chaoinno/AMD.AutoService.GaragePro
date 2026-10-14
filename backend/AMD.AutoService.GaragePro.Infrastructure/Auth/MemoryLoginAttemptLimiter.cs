using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Auth;
using Microsoft.Extensions.Caching.Memory;

namespace AMD.AutoService.GaragePro.Infrastructure.Auth;

/// <summary>
/// ตัวนับ login ผิดต่อ key ในหน่วยความจำ (singleton) — window คงที่นับจากครั้งที่ผิดครั้งแรก:
/// ผิดครบ <see cref="LoginAttemptPolicy.MaxFailures"/> ภายใน window → บล็อกจนกว่า window นั้นจะจบ แล้วเริ่มนับใหม่
///
/// · ใช้ <see cref="MemoryCache"/> ของตัวเอง ไม่ใช่ IMemoryCache ที่แชร์ทั้งแอป เพื่อจำกัดขนาดได้
///   ([SECURITY] ผู้โจมตียิง username สุ่มได้ทีละแถว — ไม่จำกัดคือ memory DoS) โดยไม่บังคับให้ทุก entry ของคนอื่นต้องตั้ง Size
///   เมื่อเต็ม entry ใหม่ไม่ถูกเพิ่ม = ไม่นับ (fail-open) ซึ่งยังมีเพดานต่อ IP ของ rate limiter ครอบอยู่
/// · การตัดสินเวลาใช้ <see cref="TimeProvider"/> (ทดสอบได้) ส่วน expiration ของ cache เป็นแค่การเก็บกวาดหน่วยความจำ
/// · [RISK] อยู่ในหน่วยความจำของ process เดียว — restart แล้วนับใหม่ และถ้าวันหน้ารัน API หลาย instance ตัวนับจะแยกกัน
///   (ตอนนี้ production มี container เดียว) ต้องย้ายไป ServiceDb/Redis ก่อน scale out
/// </summary>
public sealed class MemoryLoginAttemptLimiter : ILoginAttemptLimiter, IDisposable
{
    internal const long DefaultMaxEntries = 100_000;

    private readonly MemoryCache cache;
    private readonly TimeProvider clock;
    private readonly int maxFailures;
    private readonly TimeSpan window;
    private readonly object gate = new();

    public MemoryLoginAttemptLimiter(TimeProvider clock)
        : this(clock, LoginAttemptPolicy.MaxFailures, LoginAttemptPolicy.Window) { }

    internal MemoryLoginAttemptLimiter(
        TimeProvider clock, int maxFailures, TimeSpan window, long maxEntries = DefaultMaxEntries)
    {
        this.clock = clock;
        this.maxFailures = maxFailures;
        this.window = window;
        cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = maxEntries });
    }

    public Task<bool> IsBlockedAsync(string key, CancellationToken ct = default)
    {
        lock (gate)
        {
            var counter = Current(key);
            return Task.FromResult(counter is not null && counter.Failures >= maxFailures);
        }
    }

    public Task RegisterFailureAsync(string key, CancellationToken ct = default)
    {
        // lock ครอบทั้ง อ่าน-เพิ่ม-เขียน: MemoryCache.GetOrCreate ไม่ atomic — สองคำขอพร้อมกันจะสร้างตัวนับทับกันแล้วนับหาย
        // login ผิดเป็นงานความถี่ต่ำ (มีเพดานต่อ IP ครอบ) lock ตัวเดียวพอ
        lock (gate)
        {
            var counter = Current(key);
            if (counter is null)
            {
                counter = new Counter(clock.GetUtcNow() + window);
                cache.Set(key, counter, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = window,
                    Size = 1
                });
            }

            counter.Failures++;
        }

        return Task.CompletedTask;
    }

    public Task ResetAsync(string key, CancellationToken ct = default)
    {
        lock (gate) cache.Remove(key);
        return Task.CompletedTask;
    }

    public void Dispose() => cache.Dispose();

    /// <summary>ตัวนับที่ยังอยู่ใน window — หมด window แล้วลบทิ้ง (cache อาจยังไม่ evict เพราะใช้นาฬิกาจริงคนละตัว)</summary>
    private Counter? Current(string key)
    {
        if (!cache.TryGetValue(key, out Counter? counter) || counter is null) return null;
        if (clock.GetUtcNow() < counter.WindowEndsAt) return counter;

        cache.Remove(key);
        return null;
    }

    private sealed class Counter(DateTimeOffset windowEndsAt)
    {
        public DateTimeOffset WindowEndsAt { get; } = windowEndsAt;
        public int Failures { get; set; }
    }
}
