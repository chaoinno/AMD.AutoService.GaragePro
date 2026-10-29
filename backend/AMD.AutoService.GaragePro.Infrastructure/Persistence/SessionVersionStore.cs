using System.Data;
using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace AMD.AutoService.GaragePro.Infrastructure.Persistence;

/// <summary>
/// svc_UserSessionVersion ผ่าน Dapper ตรง — middleware เรียกทุกคำขอ จึงไม่สร้าง DbContext และไม่ cache
/// (cache ต่อ instance ทำให้ instance อื่นยังรับ token ที่ถูกเพิกถอนแล้วได้)
/// </summary>
public sealed class SessionVersionStore(IServiceDbConnectionResolver connections,
    ILogger<SessionVersionStore> logger) : ISessionVersionStore
{
    /// <summary>
    /// ธง Changing ที่ค้างนานกว่านี้ถือว่าไม่มีผล — process ตายกลางการเปลี่ยนสถานะแล้วไม่มีใครปลดธง
    /// ผู้ใช้จะได้ 503 ทุกคำขอไปตลอด · ปล่อยได้อย่างปลอดภัยเพราะธงกันแค่การออก token ใหม่:
    /// รุ่นที่เพิ่มไปแล้วยังตัด token เดิม และ login ยังอ่านสถานะ Staff จากฐานเดิมอีกรอบเสมอ
    /// · ต้องนานกว่าเวลาเขียนฐานเดิมปกติมาก (รอ lock 10 วิ + เขียน Staff ไม่กี่วินาที)
    /// </summary>
    internal const int StaleChangingSeconds = 120;

    private SqlConnection Open(string shard) => new(connections.Resolve(shard));

    public async Task<SessionVersion> ReadAsync(string shard, long userId, CancellationToken ct = default)
    {
        try
        {
            await using var db = Open(shard);
            return await db.QuerySingleOrDefaultAsync<SessionVersion>(new CommandDefinition("""
                SELECT Version,
                    CONVERT(bit, CASE WHEN Changing = 1 AND UpdatedAt > DATEADD(SECOND, -@stale, SYSUTCDATETIME())
                        THEN 1 ELSE 0 END) Changing
                FROM svc_UserSessionVersion WHERE ShardKey=@shard AND UserId=@userId
                """, new { shard, userId, stale = StaleChangingSeconds }, cancellationToken: ct)) ?? new();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AccessUpdateMetrics.SessionStoreErrors.Add(1, new KeyValuePair<string, object?>("operation", "read"));
            throw new SessionVerificationException(ex);
        }
    }

    public async Task RevokeObservedAsync(string shard, long userId, long observedVersion, CancellationToken ct = default)
    {
        try
        {
            await using var db = Open(shard);
            await db.OpenAsync(ct);
            await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            // UPDLOCK+HOLDLOCK: สอง instance เห็นบัญชีเดียวกันถูกปิดพร้อมกัน ต้องเพิ่มรุ่นครั้งเดียว ไม่ใช่สองครั้ง
            var current = await db.QuerySingleOrDefaultAsync<SessionVersion>(new CommandDefinition(
                "SELECT Version, Changing FROM svc_UserSessionVersion WITH (UPDLOCK,HOLDLOCK) WHERE ShardKey=@shard AND UserId=@userId",
                new { shard, userId }, tx, cancellationToken: ct));
            var revoke = (current?.Version ?? 0) == observedVersion;
            if (revoke)
            {
                if (current is null)
                    await db.ExecuteAsync(new CommandDefinition(
                        "INSERT svc_UserSessionVersion(ShardKey,UserId,Version,Changing,UpdatedAt) VALUES(@shard,@userId,@version,0,SYSUTCDATETIME())",
                        new { shard, userId, version = checked(observedVersion + 1) }, tx, cancellationToken: ct));
                else
                    await db.ExecuteAsync(new CommandDefinition(
                        "UPDATE svc_UserSessionVersion SET Version=Version+1,UpdatedAt=SYSUTCDATETIME() WHERE ShardKey=@shard AND UserId=@userId",
                        new { shard, userId }, tx, cancellationToken: ct));
            }
            await tx.CommitAsync(ct);
            if (revoke)
            {
                AccessUpdateMetrics.RevokedAccounts.Add(1, new KeyValuePair<string, object?>("source", "observed"));
                logger.LogInformation("Observed inactive account; revoked sessions for {Shard}/{UserId}", shard, userId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AccessUpdateMetrics.SessionStoreErrors.Add(1, new KeyValuePair<string, object?>("operation", "observe"));
            throw new SessionVerificationException(ex);
        }
    }

    public async Task<bool> ChangeStaffStatusAsync(string shard, long staffId, IReadOnlyList<long> userIds,
        bool disabling, Func<Task<bool>> change, CancellationToken ct = default)
    {
        // error จากการเขียนฐานเดิมต้องส่งต่อตามจริง — ห่อเป็น SessionVerificationException จะกลายเป็น
        // "ตรวจสอบสิทธิ์ไม่ได้" ทั้งที่สาเหตุคือเขียนข้อมูลพนักงานไม่สำเร็จ
        var legacyFailed = false;
        async Task<bool> TrackedChange()
        {
            try { return await change(); }
            catch { legacyFailed = true; throw; }
        }

        try { return await ChangeStaffStatusCoreAsync(shard, staffId, userIds, disabling, TrackedChange, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException and not StaffStatusBusyException && !legacyFailed)
        {
            AccessUpdateMetrics.SessionStoreErrors.Add(1, new KeyValuePair<string, object?>("operation", "staff_status"));
            throw new SessionVerificationException(ex);
        }
    }

    private async Task<bool> ChangeStaffStatusCoreAsync(string shard, long staffId, IReadOnlyList<long> userIds,
        bool disabling, Func<Task<bool>> change, CancellationToken ct)
    {
        await using var db = Open(shard);
        await db.OpenAsync(ct);
        // lock ระดับ session ครอบทั้งสองฐาน (ServiceDb + Garage เดิม) กันเปิด/ปิดพนักงานคนเดียวกันพร้อมกัน
        // process ตาย = connection หลุด SQL Server ปล่อย lock เอง · ธง Changing ที่ค้างหมดผลเองตาม StaleChangingSeconds
        var resource = $"staff-status:{shard}:{staffId}";
        var acquired = await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Session',@LockTimeout=10000;
            SELECT @result;
            """, new { resource }, cancellationToken: ct));
        if (acquired < 0) throw new StaffStatusBusyException();
        try
        {
            // [SECURITY] เพิ่มรุ่นให้ commit ก่อนเขียนฐานเดิม — ถ้าเขียนฐานเดิมล้ม token เดิมก็ยังถูกตัดแล้ว
            // (ผู้ที่ยัง active แค่ต้อง login ใหม่) ดีกว่าปิดพนักงานสำเร็จแต่ token เดิมยังใช้ได้
            await using (var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable, ct))
            {
                foreach (var userId in userIds.Distinct().Order())
                    await db.ExecuteAsync(new CommandDefinition("""
                        IF NOT EXISTS (SELECT 1 FROM svc_UserSessionVersion WITH (UPDLOCK,HOLDLOCK) WHERE ShardKey=@shard AND UserId=@userId)
                            INSERT svc_UserSessionVersion(ShardKey,UserId,Version,Changing,UpdatedAt) VALUES(@shard,@userId,0,0,SYSUTCDATETIME());
                        UPDATE svc_UserSessionVersion SET Version=Version+@increment,Changing=1,UpdatedAt=SYSUTCDATETIME()
                            WHERE ShardKey=@shard AND UserId=@userId;
                        """, new { shard, userId, increment = disabling ? 1 : 0 }, tx, cancellationToken: ct));
                await tx.CommitAsync(ct);
            }
            if (disabling)
                AccessUpdateMetrics.RevokedAccounts.Add(userIds.Distinct().Count(), new KeyValuePair<string, object?>("source", "staff_status"));
            try { return await change(); }
            finally
            {
                // ปลดธงแม้คำขอถูกยกเลิก (ไม่ส่ง ct) · รุ่นที่เพิ่มไปแล้วไม่ย้อนกลับไม่ว่ากรณีใด
                await db.ExecuteAsync(new CommandDefinition("""
                    UPDATE svc_UserSessionVersion SET Changing=0,UpdatedAt=SYSUTCDATETIME()
                    WHERE ShardKey=@shard AND UserId IN @userIds;
                    """, new { shard, userIds }, cancellationToken: CancellationToken.None));
                logger.LogInformation("Staff status session fence saved for {Shard}/{StaffId}; disabling={Disabling}", shard, staffId, disabling);
            }
        }
        finally
        {
            await db.ExecuteAsync("EXEC sys.sp_releaseapplock @Resource=@resource,@LockOwner='Session'", new { resource });
        }
    }
}
