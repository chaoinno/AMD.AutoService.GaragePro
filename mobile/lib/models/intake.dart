/// เช็คลิสต์สภาพรถขณะรับ — 4 หมวด 20 รายการ ตรงกับ IntakeChecklistDto
/// [BIZ] ผล issue/na ต้องมี note เสมอ · ส่งแล้วล็อกแก้ไม่ได้
library;

class IntakeChecklistItem {
  const IntakeChecklistItem({
    required this.id,
    required this.itemCode,
    required this.categoryKey,
    required this.labelTh,
    this.hintTh,
    required this.result,
    this.note,
    this.updatedByUserName,
  });

  final String id;
  final String itemCode;
  final String categoryKey;
  final String labelTh;
  final String? hintTh;
  final String result;
  final String? note;
  final String? updatedByUserName;

  bool get isPending => result == 'pending';
  bool get needsNote => result == 'issue' || result == 'na';

  static IntakeChecklistItem fromJson(Map<String, dynamic> j) => IntakeChecklistItem(
        id: j['id'] as String? ?? '',
        itemCode: j['itemCode'] as String,
        categoryKey: j['categoryKey'] as String? ?? '',
        labelTh: j['labelTh'] as String? ?? '',
        hintTh: j['hintTh'] as String?,
        result: j['result'] as String? ?? 'pending',
        note: j['note'] as String?,
        updatedByUserName: j['updatedByUserName'] as String?,
      );
}

class IntakeChecklist {
  const IntakeChecklist({
    required this.id,
    required this.jobId,
    required this.isLocked,
    this.submittedAt,
    required this.items,
  });

  final String id;
  final String jobId;
  final bool isLocked;
  final DateTime? submittedAt;
  final List<IntakeChecklistItem> items;

  int get pendingCount => items.where((i) => i.isPending).length;
  bool get complete => items.isNotEmpty && pendingCount == 0;

  /// หมวดตามลำดับที่ template กำหนด (ไม่เรียงใหม่ — ลำดับมาจากฟอร์มกระดาษต้นฉบับ)
  Map<String, List<IntakeChecklistItem>> get byCategory {
    final grouped = <String, List<IntakeChecklistItem>>{};
    for (final item in items) {
      grouped.putIfAbsent(item.categoryKey, () => []).add(item);
    }
    return grouped;
  }

  static IntakeChecklist fromJson(Map<String, dynamic> j) => IntakeChecklist(
        id: j['id'] as String,
        jobId: j['jobId'] as String,
        isLocked: j['isLocked'] as bool? ?? false,
        submittedAt: j['submittedAt'] == null
            ? null
            : DateTime.tryParse(j['submittedAt'] as String)?.toLocal(),
        items: ((j['items'] as List<dynamic>?) ?? const [])
            .map((e) => IntakeChecklistItem.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class IntakeChecklistTemplateItem {
  const IntakeChecklistTemplateItem({
    required this.categoryKey,
    required this.categoryLabelTh,
    required this.itemCode,
    required this.labelTh,
    this.hintTh,
  });

  final String categoryKey;
  final String categoryLabelTh;
  final String itemCode;
  final String labelTh;
  final String? hintTh;

  static IntakeChecklistTemplateItem fromJson(Map<String, dynamic> j) =>
      IntakeChecklistTemplateItem(
        categoryKey: j['categoryKey'] as String,
        categoryLabelTh: j['categoryLabelTh'] as String? ?? '',
        itemCode: j['itemCode'] as String,
        labelTh: j['labelTh'] as String? ?? '',
        hintTh: j['hintTh'] as String?,
      );
}
