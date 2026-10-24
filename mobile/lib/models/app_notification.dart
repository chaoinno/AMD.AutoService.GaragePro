/// แจ้งเตือน — ตรงกับ NotificationDto ใน Application/Notifications/NotificationService.cs
///
/// API ตัดฟิลด์ที่เป็น null ทิ้ง (WhenWritingNull) ฟิลด์ที่ไม่บังคับจึงต้องอ่านแบบ `as T?` เสมอ
library;

/// ชนิดที่ backend สร้างได้ (`NotificationKinds` ใน Domain/Entities/Notification.cs)
/// — ค่าที่ไม่รู้จักยังแสดงได้ ไม่ทำให้รายการพัง (server เพิ่มชนิดใหม่ก่อนแอปรุ่นใหม่ออกได้)
abstract final class NotificationKind {
  static const chatMention = 'chat.mention';
  static const quotationApproved = 'quotation.approved';
  static const quotationPartial = 'quotation.partial';
  static const quotationAllRejected = 'quotation.all_rejected';
  static const purchasePending = 'purchase.pending';
  static const purchaseApproved = 'purchase.approved';
  static const purchaseReturned = 'purchase.returned';

  /// รถใกล้ครบรอบบริการ (เหลือ ≤ 7 วัน) — แจ้งหน้าร้าน/ผู้จัดการ · `entityType: "ServiceDue"` ไม่มีจ๊อบ · รายงานอยู่บนเว็บเท่านั้น
  static const serviceDue = 'service.due';

  /// ผลตรวจการแจ้งชำระแพ็กเกจ (docs/17 §5.4) · `entityType: "PackagePayment"` ไม่มีจ๊อบ
  static const packagePaymentApproved = 'package.payment_approved';
  static const packagePaymentRejected = 'package.payment_rejected';
}

/// entityType ของแจ้งเตือนรถใกล้ครบรอบบริการ
const serviceDueEntityType = 'ServiceDue';

/// entityType ของแจ้งเตือนการแจ้งชำระแพ็กเกจ
const packagePaymentEntityType = 'PackagePayment';

class AppNotification {
  const AppNotification({
    required this.id,
    required this.kind,
    required this.titleTh,
    this.bodyTh,
    this.jobId,
    required this.entityType,
    this.entityId,
    this.linkHint,
    required this.actorName,
    required this.createdAt,
    required this.createdAtRaw,
    this.readAt,
    this.resolvedAt,
    this.resolvedByName,
  });

  final String id;
  final String kind;
  final String titleTh;
  final String? bodyTh;
  final String? jobId;

  /// 'JobChatMessage' | 'Quotation' | 'PR' | 'PO' | 'PackagePayment'
  final String entityType;
  final String? entityId;

  /// 'chat' | 'quote' บอกว่าควรเปิดส่วนไหนของจ๊อบ · 'package' = หน้าแพ็กเกจ
  final String? linkHint;
  final String actorName;
  final DateTime createdAt;

  /// ค่าดิบสำหรับ cursor `beforeAt` — DB เก็บละเอียด 7 หลักแต่ DateTime ของ Dart เก็บได้แค่ไมโครวินาที
  /// ส่งค่าที่ parse แล้วกลับไปจะได้รายการซ้ำตรงรอยต่อหน้า
  final String createdAtRaw;
  final DateTime? readAt;
  final DateTime? resolvedAt;
  final String? resolvedByName;

  /// มีคนจัดการเรื่องนี้แล้ว (เช่น PO ที่รออนุมัติถูกอนุมัติไปแล้ว) — server ไม่นับเป็นยังไม่อ่าน
  bool get isResolved => resolvedAt != null;
  bool get isRead => readAt != null;
  bool get isUnread => !isRead && !isResolved;

  AppNotification withReadAt(DateTime? value) => AppNotification(
        id: id,
        kind: kind,
        titleTh: titleTh,
        bodyTh: bodyTh,
        jobId: jobId,
        entityType: entityType,
        entityId: entityId,
        linkHint: linkHint,
        actorName: actorName,
        createdAt: createdAt,
        createdAtRaw: createdAtRaw,
        readAt: value,
        resolvedAt: resolvedAt,
        resolvedByName: resolvedByName,
      );

  static AppNotification fromJson(Map<String, dynamic> j) => AppNotification(
        id: j['id'] as String,
        kind: j['kind'] as String? ?? '',
        titleTh: j['titleTh'] as String? ?? '',
        bodyTh: j['bodyTh'] as String?,
        jobId: j['jobId'] as String?,
        entityType: j['entityType'] as String? ?? '',
        entityId: j['entityId'] as String?,
        linkHint: j['linkHint'] as String?,
        actorName: j['actorName'] as String? ?? '',
        createdAt: DateTime.parse(j['createdAt'] as String).toLocal(),
        createdAtRaw: j['createdAt'] as String,
        readAt: _date(j['readAt']),
        resolvedAt: _date(j['resolvedAt']),
        resolvedByName: j['resolvedByName'] as String?,
      );
}

class NotificationPage {
  const NotificationPage(this.items, this.hasMore);
  final List<AppNotification> items;
  final bool hasMore;

  static NotificationPage fromJson(Map<String, dynamic> j) => NotificationPage(
        ((j['items'] as List<dynamic>?) ?? const [])
            .map((e) => AppNotification.fromJson(e as Map<String, dynamic>))
            .toList(),
        j['hasMore'] as bool? ?? false,
      );
}

DateTime? _date(Object? v) => v == null ? null : DateTime.tryParse(v as String)?.toLocal();
