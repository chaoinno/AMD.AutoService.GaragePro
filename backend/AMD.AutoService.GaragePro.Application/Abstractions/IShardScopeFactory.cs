namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// รันงานในนามผู้ใช้/shard ที่กำหนด นอก HTTP request (signup provisioning · hosted job · CLI spadmin · ตัวสร้างข้อมูลตัวอย่าง)
/// · สร้าง DI scope ใหม่ แล้วตั้ง <c>CurrentUserOverride.User</c> ของ scope นั้น <b>ก่อน</b> resolve service อื่นใด —
///   ICurrentUser (และ ServiceDbContext ที่เลือก connection ตาม ShardKey ใน B4) ถูกสร้างครั้งเดียวต่อ scope
///   ถ้า resolve ก่อนตั้งค่าจะได้ shard/ผู้ใช้ผิดทั้ง scope โดยไม่มีอะไรฟ้อง
/// · ใช้คู่กับ <c>ScopedCurrentUser</c> — CurrentUser ของ API โยน exception เมื่อไม่มี HttpContext
/// </summary>
public interface IShardScopeFactory
{
    Task<T> RunAsAsync<T>(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task<T>> work,
        CancellationToken ct = default);

    Task RunAsAsync(ICurrentUser user, Func<IServiceProvider, CancellationToken, Task> work,
        CancellationToken ct = default);
}
