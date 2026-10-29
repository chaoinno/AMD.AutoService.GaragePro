namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// รุ่นเซสชันของบัญชี · token ที่ claim <c>session_version</c> ไม่ตรงกับค่านี้ถูกปฏิเสธ (ไม่มีแถว = 0 เท่ากับ token ที่ไม่มี claim)
/// · <see cref="Changing"/> = กำลังเปลี่ยนสถานะพนักงานอยู่ ห้ามออก token ใหม่ชั่วคราว
/// </summary>
public sealed record SessionVersion(long Version = 0, bool Changing = false);

/// <summary>
/// [SECURITY] รุ่นเซสชันแบบถาวร ใช้ร่วมกันทุก API instance และไม่ cache · ไม่แก้ schema ของ Garage เดิม (docs/19)
/// </summary>
public interface ISessionVersionStore
{
    Task<SessionVersion> ReadAsync(string shard, long userId, CancellationToken ct = default);

    /// <summary>ระบบเดิมปิดบัญชีไปแล้ว — เพิ่มรุ่นเฉพาะเมื่อยังเป็นรุ่นเดียวกับ token ที่ตรวจอยู่ (เรียกซ้ำไม่เพิ่มไม่สิ้นสุด)</summary>
    Task RevokeObservedAsync(string shard, long userId, long observedVersion, CancellationToken ct = default);

    /// <summary>
    /// เพิ่มรุ่น (เมื่อปิด) + ตั้ง Changing ให้ทุกบัญชีก่อนเรียก <paramref name="change"/> ที่เขียนฐานเดิม แล้วปลด Changing
    /// · <see cref="StaffStatusBusyException"/> = มีคำขอของพนักงานคนเดียวกันค้างอยู่ · error จาก <paramref name="change"/> ส่งต่อตามจริง
    /// </summary>
    Task<bool> ChangeStaffStatusAsync(string shard, long staffId, IReadOnlyList<long> userIds,
        bool disabling, Func<Task<bool>> change, CancellationToken ct = default);
}

/// <summary>อ่าน/เขียนรุ่นเซสชันไม่ได้ — API ตอบ 503 AUTH_CHECK_UNAVAILABLE และ client ต้องไม่ล้างเซสชัน</summary>
public sealed class SessionVerificationException(Exception inner)
    : Exception("ไม่สามารถตรวจสอบสิทธิ์ได้ชั่วคราว กรุณาลองใหม่", inner);

/// <summary>รอ lock ของพนักงานคนเดียวกันเกินเวลา (มีคนกำลังเปิด/ปิดพนักงานคนนี้อยู่)</summary>
public sealed class StaffStatusBusyException()
    : Exception("กำลังเปลี่ยนสถานะพนักงานคนนี้อยู่ กรุณาลองใหม่อีกครั้ง");
