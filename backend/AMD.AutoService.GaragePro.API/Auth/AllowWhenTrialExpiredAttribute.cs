namespace AMD.AutoService.GaragePro.API.Auth;

/// <summary>
/// ยกเว้น action/controller จาก <see cref="TrialReadOnlyFilter"/> — คำขอเขียนที่ต้องทำได้แม้อู่อยู่ในช่วงอ่านอย่างเดียว
/// (เช่น ขอต่ออายุ/สนใจแพ็กเกจ) · path <c>/api/v1/auth</c>, <c>/api/v1/public</c>, <c>/api/v1/selfserve</c>,
/// <c>/api/v1/notifications</c> ถูกยกเว้นอยู่แล้วโดยไม่ต้องติดตัวนี้
/// · [SECURITY] ติดเฉพาะ endpoint ที่ไม่สร้าง/แก้ข้อมูลงานของอู่ — ไม่งั้นช่วงอ่านอย่างเดียวไม่มีความหมาย
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
public sealed class AllowWhenTrialExpiredAttribute : Attribute;
