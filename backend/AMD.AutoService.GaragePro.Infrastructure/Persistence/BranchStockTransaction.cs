using AMD.AutoService.GaragePro.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

/// <summary>ธุรกรรมตัดสต็อกระดับสาขา ใช้ applock เดียวกันระหว่างจัดซื้อและขายหน้าร้าน</summary>
public sealed class BranchStockTransaction(ServiceDbContext db, ICurrentUser user)
{
    public Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct) =>
        db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var resource = $"garagepro:purchasing:{user.ShardKey}:{user.BranchId}";
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                DECLARE @result int;
                EXEC @result = sys.sp_getapplock @Resource={resource}, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
                IF @result < 0 THROW 51001, 'Purchasing lock timeout', 1;", ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return result;
        });
}