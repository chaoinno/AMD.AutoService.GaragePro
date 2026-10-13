namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// ชน unique index/constraint ตอน SaveChanges — repository แปลง SqlException 2601/2627 มาเป็นตัวนี้
/// เพื่อไม่ให้ Application รู้จัก SqlClient (แบบเดียวกับ MasterDataConflictException/WorkIntervalConflictException
/// แต่ไม่ผูกกับโมดูลไหน ใช้ซ้ำได้) · ConstraintName มีไว้บอก log/เทสต์ว่าชนตัวไหน ไม่ใช่ตัวตัดสินตรรกะ
/// (ห้ามแยกเคสด้วยการ parse ข้อความ SQL — ขึ้นกับภาษา/collation ของเซิร์ฟเวอร์) · service ที่จับต้องคืน
/// error code ของโมดูลตัวเองที่ client ลองใหม่ได้ ไม่ปล่อยเป็น 500
/// </summary>
public sealed class UniqueConstraintViolationException(string constraintName, Exception? innerException = null)
    : Exception($"Unique constraint violated: {constraintName}", innerException)
{
    public string ConstraintName { get; } = constraintName;
}
