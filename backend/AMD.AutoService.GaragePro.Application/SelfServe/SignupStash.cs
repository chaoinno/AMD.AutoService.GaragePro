using System.Collections.Concurrent;

namespace AMD.AutoService.GaragePro.Application.SelfServe;

/// <summary>
/// ที่พักข้อมูลระยะสั้นในหน่วยความจำของ signup (Singleton) — ไม่ใช่แหล่งความจริง หายตอน restart/คนละ instance ได้
/// ทุกผู้ใช้ต้องมีทางสำรองเมื่อหาไม่เจอ:
/// · <c>start:{email}:{requestId}</c> — email/start ที่สำเร็จแล้ว: ส่งคำขอเดิมซ้ำ (เน็ตหลุด) ได้ 200 โดยไม่ส่งอีเมล/ไม่โดน throttle
///   (หาไม่เจอ = ทำแบบคำขอใหม่ → อย่างแย่ได้ SIGNUP_OTP_THROTTLED พร้อมเวลารอ)
/// · <c>owner-name:{accountId}</c> — ชื่อจาก Google ID token ไว้ตั้งชื่อเจ้าของตอนเปิดอู่ (sp_Account ไม่มีคอลัมน์ชื่อ)
///   (หาไม่เจอ = ใช้ส่วนหน้า @ ของอีเมล · เจ้าของแก้ชื่อทีหลังได้ docs/12 §4.1)
/// · Application ไม่มี IMemoryCache จึงเขียนเองแบบเล็กที่สุด: TTL ต่อรายการ + เพดานจำนวน (ล้างของหมดอายุก่อน ไม่พอค่อยล้างทั้งหมด)
/// </summary>
public sealed class SignupStash(TimeProvider clock)
{
    public const int MaxEntries = 5000;

    private readonly ConcurrentDictionary<string, (string Value, DateTimeOffset ExpiresAt)> _items =
        new(StringComparer.Ordinal);

    public void Set(string key, string value, TimeSpan ttl)
    {
        var now = clock.GetUtcNow();
        if (_items.Count >= MaxEntries)
        {
            foreach (var (k, v) in _items)
                if (v.ExpiresAt <= now) _items.TryRemove(k, out _);
            if (_items.Count >= MaxEntries) _items.Clear();
        }

        _items[key] = (value, now + ttl);
    }

    public bool TryGet(string key, out string value)
    {
        value = string.Empty;
        if (!_items.TryGetValue(key, out var item)) return false;
        if (item.ExpiresAt <= clock.GetUtcNow())
        {
            _items.TryRemove(key, out _);
            return false;
        }

        value = item.Value;
        return true;
    }
}
