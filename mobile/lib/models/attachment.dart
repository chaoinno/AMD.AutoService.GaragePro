/// ไฟล์แนบของงาน — ลายเซ็น รูปรับรถ รูปตรวจเช็ค รูปก่อน/หลังซ่อม
library;

import 'json.dart';

/// ชนิดไฟล์แนบ — ต้องตรงกับ AllowedKinds ใน AttachmentService ของ API
/// ส่งค่าอื่นจะได้ 422 ATTACHMENT_KIND_INVALID
abstract final class AttachmentKind {
  static const signature = 'signature';
  static const intake = 'intake';
  static const inspection = 'inspection';
  static const repairBefore = 'repair-before';
  static const repairAfter = 'repair-after';
  static const qc = 'qc';
  static const document = 'document';

  /// ชนิดที่ถ่ายจากหน้างานได้ — ใช้เป็นตัวเลือกตอนอัปโหลด
  /// (signature ไม่อยู่ในนี้เพราะมาจากหน้าลายเซ็นเท่านั้น)
  static const capturable = <String, String>{
    intake: 'รูปรับรถ',
    inspection: 'รูปตรวจเช็ค',
    repairBefore: 'รูปก่อนซ่อม',
    repairAfter: 'รูปหลังซ่อม',
    qc: 'รูปตรวจคุณภาพ',
  };

  static const labelsTh = <String, String>{
    signature: 'ลายเซ็น',
    intake: 'รูปรับรถ',
    inspection: 'รูปตรวจเช็ค',
    repairBefore: 'รูปก่อนซ่อม',
    repairAfter: 'รูปหลังซ่อม',
    qc: 'รูปตรวจคุณภาพ',
    document: 'เอกสาร',
  };

  static String labelOf(String kind) => labelsTh[kind] ?? kind;
}

class Attachment {
  Attachment({
    required this.id,
    required this.kind,
    required this.fileName,
    required this.contentType,
    required this.sizeBytes,
    required this.relativePath,
    required this.url,
    required this.uploadedByName,
    required this.uploadedAt,
  });

  final String id;
  final String kind;
  final String fileName;
  final String contentType;
  final int sizeBytes;

  /// path ที่เก็บบน server — ใช้ส่งกลับตอนเซ็น และใช้เปิดไฟล์
  final String relativePath;

  /// URL ที่ API ประกอบไว้ให้แล้ว (relative ต่อ host ของ API)
  final String url;

  final String uploadedByName;
  final DateTime uploadedAt;

  String get kindLabelTh => AttachmentKind.labelOf(kind);

  bool get isImage => contentType.startsWith('image/');

  factory Attachment.fromJson(Map<String, dynamic> j) => Attachment(
        id: j['id'] as String,
        kind: j['kind'] as String? ?? '',
        fileName: j['fileName'] as String? ?? '',
        contentType: j['contentType'] as String? ?? '',
        sizeBytes: jint(j['sizeBytes']),
        relativePath: j['relativePath'] as String? ?? '',
        url: j['url'] as String? ?? '',
        uploadedByName: j['uploadedByName'] as String? ?? '',
        uploadedAt: jdate(j['uploadedAt']) ?? DateTime.now(),
      );
}
