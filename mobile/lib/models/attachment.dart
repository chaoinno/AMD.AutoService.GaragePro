/// ไฟล์แนบของจ๊อบ — ตรงกับ AttachmentDto ใน Application/Attachments/AttachmentService.cs
library;

class Attachment {
  Attachment({
    required this.id,
    required this.kind,
    this.entityId,
    required this.fileName,
    required this.contentType,
    required this.sizeBytes,
    required this.relativePath,
    required this.uploadedByName,
    required this.uploadedAt,
  });

  final String id;
  final String kind;
  final String? entityId;
  final String fileName;
  final String contentType;
  final int sizeBytes;

  /// path ที่ใช้กับ GET /attachments/file?path= — **ต้องแนบ Bearer เสมอ**
  final String relativePath;

  final String uploadedByName;
  final DateTime uploadedAt;

  bool get isImage => contentType.startsWith('image/');

  static Attachment fromJson(Map<String, dynamic> j) => Attachment(
        id: j['id'] as String,
        kind: j['kind'] as String? ?? '',
        entityId: j['entityId'] as String?,
        fileName: j['fileName'] as String? ?? '',
        contentType: j['contentType'] as String? ?? '',
        sizeBytes: (j['sizeBytes'] as num?)?.toInt() ?? 0,
        relativePath: j['relativePath'] as String,
        uploadedByName: j['uploadedByName'] as String? ?? '',
        uploadedAt: DateTime.parse(j['uploadedAt'] as String).toLocal(),
      );
}

/// kind ที่ server ยอมรับ — AttachmentService.AllowedKinds
/// ส่ง kind อื่นจะถูกปฏิเสธด้วย ATTACHMENT_KIND_INVALID
abstract final class AttachmentKind {
  static const signature = 'signature';
  static const intake = 'intake';
  static const inspection = 'inspection';
  static const repairBefore = 'repair-before';
  static const repairAfter = 'repair-after';
  static const qc = 'qc';
  static const document = 'document';
  static const handoverSignature = 'handover-signature';
  static const chat = 'chat';
}

/// ข้อจำกัดฝั่ง server (AttachmentOptions) — ตรวจที่ client ก่อนเพื่อไม่ให้เสียเวลาอัปโหลดฟรี
abstract final class AttachmentLimits {
  static const maxSizeBytes = 15 * 1024 * 1024;
  static const allowedContentTypes = <String>{
    'image/png',
    'image/jpeg',
    'image/webp',
    'application/pdf',
  };
}
