/// ส่งมอบรถ — เช็คลิสต์ของในรถ + ลายเซ็นลูกค้ารับรถคืน
/// [BIZ] ของที่ไม่ได้คืนต้องมีเหตุผลเสมอ · ส่งมอบแล้วล็อกแก้ไม่ได้
library;

class HandoverChecklistItem {
  const HandoverChecklistItem({
    required this.id,
    required this.itemCode,
    required this.name,
    required this.isReturned,
    this.note,
    this.updatedAt,
  });

  final String id;
  final String itemCode;
  final String name;
  final bool isReturned;
  final String? note;
  final DateTime? updatedAt;

  /// ยังไม่ได้ตัดสินใจ = ยังไม่เคยถูกบันทึกเลย
  bool get isPending => updatedAt == null;

  static HandoverChecklistItem fromJson(Map<String, dynamic> j) => HandoverChecklistItem(
        id: j['id'] as String,
        itemCode: j['itemCode'] as String? ?? '',
        name: j['name'] as String? ?? '',
        isReturned: j['isReturned'] as bool? ?? false,
        note: j['note'] as String?,
        updatedAt: j['updatedAt'] == null
            ? null
            : DateTime.tryParse(j['updatedAt'] as String)?.toLocal(),
      );
}

class Handover {
  const Handover({
    required this.id,
    required this.jobId,
    required this.isLocked,
    this.signatureImagePath,
    this.submittedAt,
    this.submittedByUserName,
    required this.items,
  });

  final String id;
  final String jobId;
  final bool isLocked;
  final String? signatureImagePath;
  final DateTime? submittedAt;
  final String? submittedByUserName;
  final List<HandoverChecklistItem> items;

  int get pendingCount => items.where((i) => i.isPending).length;
  bool get allDecided => items.isNotEmpty && pendingCount == 0;

  static Handover fromJson(Map<String, dynamic> j) => Handover(
        id: j['id'] as String,
        jobId: j['jobId'] as String,
        isLocked: j['isLocked'] as bool? ?? false,
        signatureImagePath: j['signatureImagePath'] as String?,
        submittedAt: j['submittedAt'] == null
            ? null
            : DateTime.tryParse(j['submittedAt'] as String)?.toLocal(),
        submittedByUserName: j['submittedByUserName'] as String?,
        items: ((j['items'] as List<dynamic>?) ?? const [])
            .map((e) => HandoverChecklistItem.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
