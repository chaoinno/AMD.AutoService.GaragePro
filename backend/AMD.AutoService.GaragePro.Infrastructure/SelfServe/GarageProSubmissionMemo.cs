using System.Collections.Concurrent;
using AMD.AutoService.GaragePro.Application.SelfServe.Package;
using Microsoft.Extensions.Caching.Memory;

namespace AMD.AutoService.GaragePro.Infrastructure.SelfServe;

/// <summary>
/// requestId ของการแจ้งชำระ flow garagepro (docs/17 §8.7.1) ใน IMemoryCache 10 นาที + ล็อกต่อสาขา (singleton)
/// · ล็อกต่อสาขาไม่ถูกลบ: จำนวนสาขาจำกัด (หลักร้อย) และคำขอแจ้งชำระไม่ถี่ (rate limit 10 ครั้ง/ชม. ต่ออู่)
/// · [RISK] อยู่ในหน่วยความจำของ process เดียว · restart แล้วลืม (ยังมีด่านตรวจรายการรอตรวจของระบบเดิมกันซ้อนอยู่)
/// </summary>
public sealed class GarageProSubmissionMemo(IMemoryCache cache) : IGarageProSubmissionMemo
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const string Prefix = "garagepro-package-submit:";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> EnterAsync(string branchKey, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(branchKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        return new Release(gate);
    }

    public GarageProSubmission? Get(string requestKey) =>
        cache.TryGetValue(Prefix + requestKey, out GarageProSubmission? value) ? value : null;

    public void Set(string requestKey, GarageProSubmission value) =>
        cache.Set(Prefix + requestKey, value, Lifetime);

    public void Remove(string requestKey) => cache.Remove(Prefix + requestKey);

    private sealed class Release(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
