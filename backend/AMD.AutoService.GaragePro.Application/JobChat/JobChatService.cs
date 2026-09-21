using AMD.AutoService.GaragePro.Application.Abstractions;
using AMD.AutoService.GaragePro.Application.Common;
using AMD.AutoService.GaragePro.Application.Dtos;
using AMD.AutoService.GaragePro.Domain.Entities;

namespace AMD.AutoService.GaragePro.Application.JobChat;

public sealed record JobChatMentionDto(long StaffId, string StaffName);

public sealed record JobChatAttachmentDto(
    Guid Id, string RelativePath, string Url, string ContentType, string FileName);

public sealed record JobChatReplyPreviewDto(Guid Id, string? Body, bool IsDeleted, string CreatedByUserName);

public sealed record JobChatMessageDto(
    Guid Id,
    Guid JobId,
    string? Body,
    bool IsDeleted,
    long CreatedByUserId,
    string CreatedByUserName,
    DateTime CreatedAt,
    JobChatReplyPreviewDto? ReplyTo,
    IReadOnlyList<JobChatMentionDto> Mentions,
    IReadOnlyList<JobChatAttachmentDto> Attachments);

public sealed record JobChatPageDto(IReadOnlyList<JobChatMessageDto> Messages, bool HasMore);

/// <summary>ข้อความล่าสุดของจ๊อบหนึ่ง — client เทียบ MessageId กับ id ที่อ่านถึงแล้วในเครื่องเอง</summary>
public sealed record JobChatLatestDto(Guid JobId, Guid MessageId, DateTime CreatedAt);

/// <summary>ระบุได้แค่ทิศทางเดียวต่อคำขอ — ไม่ส่งเลยคือหน้าล่าสุด</summary>
public sealed record JobChatPageQuery(
    DateTime? BeforeAt = null, Guid? BeforeId = null,
    DateTime? AfterAt = null, Guid? AfterId = null,
    int Take = 50);

public sealed record SendJobChatMessageRequest(
    string? Body,
    Guid? ReplyToMessageId,
    IReadOnlyList<long>? MentionedStaffIds,
    IReadOnlyList<Guid>? AttachmentIds);

public interface IJobChatService
{
    Task<Result<JobChatPageDto>> GetPageAsync(
        Guid jobId, JobChatPageQuery query, CancellationToken ct = default);

    Task<Result<JobChatMessageDto>> SendAsync(
        Guid jobId, SendJobChatMessageRequest request, CancellationToken ct = default);

    Task<Result<JobChatMessageDto>> DeleteAsync(
        Guid jobId, Guid messageId, CancellationToken ct = default);

    /// <summary>ข้อความล่าสุดของหลายจ๊อบในคำขอเดียว — ใช้แสดงจุด "มีข้อความใหม่" บนการ์ดในคิวงาน</summary>
    Task<Result<IReadOnlyList<JobChatLatestDto>>> GetLatestPerJobAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default);
}

/// <summary>
/// แชทผูกกับ job — ไม่จำกัด role เพิ่มเติมนอกจาก [RequireShiftSession] เดิม (ตรงกับ [RISK] เดิมเรื่อง RBAC
/// ของ Job endpoint ที่ยังไม่ผูก role — chat ใช้ gate เดียวกัน ไม่ได้เพิ่มช่องโหว่ใหม่)
/// รูปภาพ reuse ระบบ Attachment เดิมทั้งหมด (Kind="chat", EntityId ผูกกับ Id ข้อความ) ไม่มีคอลัมน์ path ในเอนทิตีนี้เอง
/// </summary>
public sealed class JobChatService(
    IJobChatRepository chats,
    IJobRepository jobs,
    IAttachmentRepository attachments,
    IStaffRepository staffRepo,
    ICurrentUser user,
    TimeProvider clock) : IJobChatService
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// เพดานจำนวนจ๊อบต่อคำขอ — คิวงานโหลดทีละ 25 การ์ด เผื่อไว้พอสำหรับเลื่อนสะสมหลายหน้า
    private const int MaxLatestJobIds = 100;

    public async Task<Result<JobChatPageDto>> GetPageAsync(
        Guid jobId, JobChatPageQuery query, CancellationToken ct = default)
    {
        var jobResult = await ValidateJobScopeAsync(jobId, ct);
        if (!jobResult.Success) return Result<JobChatPageDto>.Fail(jobResult.Error!);

        var take = query.Take is > 0 and <= 200 ? query.Take : 50;

        var page = await chats.GetPageAsync(
            jobId, query.BeforeAt, query.BeforeId, query.AfterAt, query.AfterId, take + 1, ct);

        var hasMore = page.Count > take;
        var messages = hasMore ? page.Take(take).ToList() : page.ToList();

        var attachmentsByMessage = (await attachments.GetForJobAsync(jobId, "chat", ct))
            .Where(a => a.EntityId is not null)
            .GroupBy(a => a.EntityId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(ToAttachmentDto).ToList());

        var dtos = messages.Select(m => ToDto(m, attachmentsByMessage)).ToList();

        return Result<JobChatPageDto>.Ok(new JobChatPageDto(dtos, hasMore));
    }

    public async Task<Result<JobChatMessageDto>> SendAsync(
        Guid jobId, SendJobChatMessageRequest request, CancellationToken ct = default)
    {
        var jobResult = await ValidateJobScopeAsync(jobId, ct);
        if (!jobResult.Success) return Result<JobChatMessageDto>.Fail(jobResult.Error!);

        var body = string.IsNullOrWhiteSpace(request.Body) ? null : request.Body.Trim();
        var attachmentIds = request.AttachmentIds ?? [];
        var mentionedStaffIds = request.MentionedStaffIds ?? [];

        if (body is null && attachmentIds.Count == 0)
            return Result<JobChatMessageDto>.Fail(
                "CHAT_MESSAGE_EMPTY", "กรุณาพิมพ์ข้อความหรือแนบรูปอย่างน้อยหนึ่งอย่าง");

        JobChatMessage? replyTo = null;
        if (request.ReplyToMessageId is { } replyId)
        {
            replyTo = await chats.GetByIdAsync(replyId, ct);
            if (replyTo is null || replyTo.JobId != jobId)
                return Result<JobChatMessageDto>.Fail(
                    "CHAT_REPLY_NOT_FOUND", "ไม่พบข้อความที่ต้องการตอบกลับในงานนี้", nameof(request.ReplyToMessageId));
        }

        var mentions = new List<JobChatMention>();
        foreach (var staffId in mentionedStaffIds.Distinct())
        {
            var staff = await staffRepo.GetAsync(
                new LegacyRequestScope(user.ShardKey, user.BranchId, user.UserId, user.UserName),
                user.IsAdministrator, staffId, ct);

            if (staff is not { IsActive: true } || staff.BranchId != user.BranchId)
                return Result<JobChatMessageDto>.Fail(
                    "CHAT_MENTION_STAFF_INVALID",
                    "ไม่พบพนักงานที่ถูกกล่าวถึง หรือพนักงานถูกปิดใช้งานที่สาขานี้แล้ว", nameof(request.MentionedStaffIds));

            mentions.Add(new JobChatMention
            {
                StaffId = staffId,
                StaffName = $"{staff.FirstName} {staff.LastName}".Trim()
            });
        }

        var linkedAttachments = new List<Attachment>();
        foreach (var attachmentId in attachmentIds.Distinct())
        {
            var attachment = await attachments.GetAsync(attachmentId, ct);
            if (attachment is null || attachment.JobId != jobId || attachment.Kind != "chat" || attachment.EntityId is not null)
                return Result<JobChatMessageDto>.Fail(
                    "CHAT_ATTACHMENT_INVALID",
                    "ไฟล์แนบไม่ถูกต้อง ไม่ใช่ของงานนี้ หรือถูกใช้กับข้อความอื่นไปแล้ว", nameof(request.AttachmentIds));

            linkedAttachments.Add(attachment);
        }

        var message = new JobChatMessage
        {
            JobId = jobId,
            Body = body,
            ReplyToMessageId = replyTo?.Id,
            CreatedByUserId = user.UserId,
            CreatedByUserName = user.UserName,
            CreatedAt = Now,
            Mentions = mentions
        };

        foreach (var attachment in linkedAttachments)
            attachment.EntityId = message.Id;

        await chats.AddAsync(message, ct);
        await chats.AddEventAsync(new ActivityEvent
        {
            JobId = jobId,
            EntityId = message.Id,
            EntityType = nameof(JobChatMessage),
            EventType = "jobchat.message_sent",
            DescriptionTh = "ส่งข้อความในแชทของงาน",
            PerformedByUserId = user.UserId,
            PerformedByName = user.UserName,
            Source = user.Source,
            OccurredAt = Now
        }, ct);
        await chats.SaveChangesAsync(ct);

        var attachmentDtos = linkedAttachments.Select(ToAttachmentDto).ToList();
        return Result<JobChatMessageDto>.Ok(ToDto(message, replyTo, attachmentDtos));
    }

    public async Task<Result<JobChatMessageDto>> DeleteAsync(
        Guid jobId, Guid messageId, CancellationToken ct = default)
    {
        var jobResult = await ValidateJobScopeAsync(jobId, ct);
        if (!jobResult.Success) return Result<JobChatMessageDto>.Fail(jobResult.Error!);

        var message = await chats.GetByIdAsync(messageId, ct);
        if (message is null || message.JobId != jobId)
            return Result<JobChatMessageDto>.Fail("CHAT_MESSAGE_NOT_FOUND", "ไม่พบข้อความนี้ในงานนี้");

        if (message.CreatedByUserId != user.UserId)
            return Result<JobChatMessageDto>.Fail("CHAT_FORBIDDEN", "ลบได้เฉพาะข้อความของตัวเองเท่านั้น");

        message.IsDeleted = true;
        message.DeletedAt = Now;
        await chats.SaveChangesAsync(ct);

        return Result<JobChatMessageDto>.Ok(ToDto(message, new Dictionary<Guid, List<JobChatAttachmentDto>>()));
    }

    /// <summary>
    /// [BIZ] ไม่ตรวจสิทธิ์ทีละจ๊อบเหมือน endpoint อื่นของแชท เพราะจะกลายเป็น N คิวรีซึ่งเป็นสิ่งที่
    /// endpoint นี้ตั้งใจกำจัด — สโคป shard/สาขาถูกบังคับในคิวรีแทน จ๊อบนอกสาขาจึงหายไปเงียบๆ
    /// (ไม่ใช่ 403) ซึ่งปลอดภัยกว่าเพราะไม่บอกใบ้ว่าจ๊อบนั้นมีอยู่จริงหรือไม่
    /// </summary>
    public async Task<Result<IReadOnlyList<JobChatLatestDto>>> GetLatestPerJobAsync(
        IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default)
    {
        if (jobIds.Count > MaxLatestJobIds)
            return Result<IReadOnlyList<JobChatLatestDto>>.Fail(
                "CHAT_VALIDATION",
                $"ขอข้อความล่าสุดได้ครั้งละไม่เกิน {MaxLatestJobIds} งาน", nameof(jobIds));

        var latest = await chats.GetLatestPerJobAsync(user.ShardKey, user.BranchId, jobIds, ct);

        return Result<IReadOnlyList<JobChatLatestDto>>.Ok(
            latest.Select(x => new JobChatLatestDto(x.JobId, x.MessageId, x.CreatedAt)).ToList());
    }

    private async Task<Result<bool>> ValidateJobScopeAsync(Guid jobId, CancellationToken ct)
    {
        var job = await jobs.GetAsync(jobId, ct);
        if (job is null)
            return Result<bool>.Fail("JOB_NOT_FOUND", $"ไม่พบงานเลขที่ {jobId}");

        if (job.BranchId != user.BranchId || job.LegacyShardKey != user.ShardKey)
            return Result<bool>.Fail("JOB_OTHER_BRANCH", "งานนี้อยู่คนละสาขากับที่คุณเข้าใช้งานอยู่");

        return Result<bool>.Ok(true);
    }

    private static JobChatAttachmentDto ToAttachmentDto(Attachment a) => new(
        a.Id, a.RelativePath, $"/api/v1/attachments/file?path={Uri.EscapeDataString(a.RelativePath)}",
        a.ContentType, a.FileName);

    private static JobChatMessageDto ToDto(
        JobChatMessage m, IReadOnlyDictionary<Guid, List<JobChatAttachmentDto>> attachmentsByMessage) => new(
        m.Id, m.JobId, m.IsDeleted ? null : m.Body, m.IsDeleted, m.CreatedByUserId, m.CreatedByUserName, m.CreatedAt,
        m.ReplyToMessage is null ? null : new JobChatReplyPreviewDto(
            m.ReplyToMessage.Id, m.ReplyToMessage.IsDeleted ? null : m.ReplyToMessage.Body,
            m.ReplyToMessage.IsDeleted, m.ReplyToMessage.CreatedByUserName),
        m.Mentions.Select(x => new JobChatMentionDto(x.StaffId, x.StaffName)).ToList(),
        attachmentsByMessage.TryGetValue(m.Id, out var list) ? list : []);

    private static JobChatMessageDto ToDto(
        JobChatMessage m, JobChatMessage? replyTo, IReadOnlyList<JobChatAttachmentDto> attachmentDtos) => new(
        m.Id, m.JobId, m.Body, m.IsDeleted, m.CreatedByUserId, m.CreatedByUserName, m.CreatedAt,
        replyTo is null ? null : new JobChatReplyPreviewDto(
            replyTo.Id, replyTo.IsDeleted ? null : replyTo.Body, replyTo.IsDeleted, replyTo.CreatedByUserName),
        m.Mentions.Select(x => new JobChatMentionDto(x.StaffId, x.StaffName)).ToList(),
        attachmentDtos);
}
