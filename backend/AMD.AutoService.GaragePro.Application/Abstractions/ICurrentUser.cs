using AMD.AutoService.GaragePro.Domain.Enums;

namespace AMD.AutoService.GaragePro.Application.Abstractions;

/// <summary>
/// บริบทของผู้ใช้ในคำขอปัจจุบัน — shard + สาขา + บทบาท
/// [BIZ] Legacy Id ไม่ unique ข้าม shard จึงต้องพก ShardKey ไปทุกที่ (docs/05 §1)
/// </summary>
public interface ICurrentUser
{
    long UserId { get; }
    string UserName { get; }
    UserRole Role { get; }
    string ShardKey { get; }
    int BranchId { get; }
    EventSource Source { get; }
    bool IsAdministrator { get; }

    /// <summary>รอบกะปัจจุบัน — null เมื่อยังไม่ได้เลือกสาขา/กะ (token ขั้นแรก)</summary>
    Guid? SessionId { get; }

    /// <summary>[BIZ] ต้นทุน/กำไร/คอมมิชชัน เห็นได้เฉพาะผู้จัดการ</summary>
    bool CanSeeCost => Role == UserRole.Manager;
}
