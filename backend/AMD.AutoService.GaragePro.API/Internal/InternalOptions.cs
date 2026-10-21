namespace AMD.AutoService.GaragePro.API.Internal;

/// <summary>
/// ค่าตั้งของ API ภายใน (<c>/api/v1/internal/*</c> · docs/17 §8.6) ที่ GaragePro Admin เรียกผ่าน docker network ร่วม
/// · [SECURITY] <see cref="ApiKey"/> อยู่ใน user-secrets / env file เท่านั้น (<c>Internal__ApiKey</c> ← <c>Production:Internal:ApiKey</c>)
///   ต้องเท่ากับ <c>SERVICEPRO_INTERNAL_KEY</c> ของ Admin · ห้ามอยู่ใน Git และห้าม log
/// · ไม่ตั้งหรือสั้นกว่า <see cref="MinKeyLength"/> ตัว = ปิด API ภายในทั้งหมด (ทุก route ตอบ 404 เหมือนไม่มี endpoint)
/// </summary>
public sealed class InternalOptions
{
    public const string SectionName = "Internal";
    public const int MinKeyLength = 32;

    public string? ApiKey { get; set; }

    /// <summary>shard ของสาขา GaragePro กลุ่ม Service ที่ส่งเป็นแถว <c>source: "garagepro"</c></summary>
    public string GarageProShardKey { get; set; } = "db2";

    /// <summary>คีย์ที่ใช้เทียบจริง (ตัดช่องว่างหัวท้ายแบบเดียวกับฝั่ง Admin) · null = API ภายในปิดอยู่</summary>
    public string? EffectiveKey =>
        ApiKey?.Trim() is { Length: >= MinKeyLength } key ? key : null;

    /// <summary>ตั้งไว้แต่สั้นเกิน: ปิดอยู่เหมือนไม่ได้ตั้ง แต่ควรเตือนตอนสตาร์ต (ไม่งั้นคนตั้งจะงงว่าทำไม 404)</summary>
    public bool IsTooShort => !string.IsNullOrWhiteSpace(ApiKey) && EffectiveKey is null;
}
