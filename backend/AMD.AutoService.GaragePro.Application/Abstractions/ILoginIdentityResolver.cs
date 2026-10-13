namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>ผลการแปลงสิ่งที่ผู้ใช้พิมพ์ในช่อง "รหัสพนักงาน/อีเมล" → (shard, UserName ใน [User]) ที่ใช้ค้นบัญชีจริง</summary>
public sealed record LoginIdentity(string ShardKey, string UserName);

/// <summary>
/// ตัดสินว่า login นี้เป็นของ shard ไหน โดยไม่ให้ client ส่ง shard มาเอง (แผน §3 · B5)
/// · มี <c>@</c> → อีเมลของบัญชี self-serve (sp_Account.NormalizedEmail → LoginUserName) → sp1
/// · รูปแบบ <c>SP0000.X00</c> → sp1 · อื่นๆ → shard เริ่มต้น (db2) = พฤติกรรมเดิม
/// · ฐาน sp_ ล่มต้อง fail-open ไป db2 เพื่อไม่ให้ลูกค้าเดิม login ไม่ได้เพราะส่วนที่ไม่เกี่ยวกับเขา
/// </summary>
public interface ILoginIdentityResolver
{
    Task<LoginIdentity> ResolveAsync(string input, CancellationToken ct = default);
}
