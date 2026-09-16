/// ข้อความแชทในจ๊อบ — ตรงกับ JobChatMessageDto ใน Application/JobChat/JobChatService.cs
library;

class JobChatMention {
  const JobChatMention(this.staffId, this.staffName);
  final int staffId;
  final String staffName;

  static JobChatMention fromJson(Map<String, dynamic> j) =>
      JobChatMention((j['staffId'] as num).toInt(), j['staffName'] as String? ?? '');
}

class JobChatAttachment {
  const JobChatAttachment({
    required this.id,
    required this.relativePath,
    required this.contentType,
    required this.fileName,
  });

  final String id;
  final String relativePath;
  final String contentType;
  final String fileName;

  bool get isImage => contentType.startsWith('image/');

  static JobChatAttachment fromJson(Map<String, dynamic> j) => JobChatAttachment(
        id: j['id'] as String,
        relativePath: j['relativePath'] as String,
        contentType: j['contentType'] as String? ?? '',
        fileName: j['fileName'] as String? ?? '',
      );
}

class JobChatReplyPreview {
  const JobChatReplyPreview({
    required this.id,
    this.body,
    required this.isDeleted,
    required this.createdByUserName,
  });

  final String id;
  final String? body;
  final bool isDeleted;
  final String createdByUserName;

  static JobChatReplyPreview fromJson(Map<String, dynamic> j) => JobChatReplyPreview(
        id: j['id'] as String,
        body: j['body'] as String?,
        isDeleted: j['isDeleted'] as bool? ?? false,
        createdByUserName: j['createdByUserName'] as String? ?? '',
      );
}

class JobChatMessage {
  JobChatMessage({
    required this.id,
    required this.jobId,
    this.body,
    required this.isDeleted,
    required this.createdByUserId,
    required this.createdByUserName,
    required this.createdAt,
    required this.createdAtRaw,
    this.replyTo,
    required this.mentions,
    required this.attachments,
  });

  final String id;
  final String jobId;
  final String? body;
  final bool isDeleted;
  final int createdByUserId;
  final String createdByUserName;
  final DateTime createdAt;

  /// ค่าดิบสำหรับ cursor — เหตุผลเดียวกับ Job.createdAtRaw (ห้ามส่งค่าที่ผ่าน .toLocal() กลับไป)
  final String createdAtRaw;

  final JobChatReplyPreview? replyTo;
  final List<JobChatMention> mentions;
  final List<JobChatAttachment> attachments;

  static JobChatMessage fromJson(Map<String, dynamic> j) => JobChatMessage(
        id: j['id'] as String,
        jobId: j['jobId'] as String,
        body: j['body'] as String?,
        isDeleted: j['isDeleted'] as bool? ?? false,
        createdByUserId: (j['createdByUserId'] as num?)?.toInt() ?? 0,
        createdByUserName: j['createdByUserName'] as String? ?? '',
        createdAt: DateTime.parse(j['createdAt'] as String).toLocal(),
        createdAtRaw: j['createdAt'] as String,
        replyTo: j['replyTo'] == null
            ? null
            : JobChatReplyPreview.fromJson(j['replyTo'] as Map<String, dynamic>),
        mentions: ((j['mentions'] as List<dynamic>?) ?? const [])
            .map((e) => JobChatMention.fromJson(e as Map<String, dynamic>))
            .toList(),
        attachments: ((j['attachments'] as List<dynamic>?) ?? const [])
            .map((e) => JobChatAttachment.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class JobChatPage {
  const JobChatPage(this.messages, this.hasMore);
  final List<JobChatMessage> messages;
  final bool hasMore;

  static JobChatPage fromJson(Map<String, dynamic> j) => JobChatPage(
        ((j['messages'] as List<dynamic>?) ?? const [])
            .map((e) => JobChatMessage.fromJson(e as Map<String, dynamic>))
            .toList(),
        j['hasMore'] as bool? ?? false,
      );
}
