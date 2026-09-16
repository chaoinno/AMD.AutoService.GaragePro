/// เช็คลิสต์ QC — ตรงกับ QcChecklistDto
/// [BIZ] ผลมีแค่ pending/pass ไม่มี "ไม่ผ่าน" — ถ้ายังไม่เรียบร้อยให้ไปบอกช่างแก้แล้วค่อยกลับมาติ๊ก
library;

class QcChecklistItem {
  const QcChecklistItem({
    required this.id,
    required this.catalogCode,
    required this.name,
    required this.type,
    required this.result,
    this.note,
    this.updatedByUserName,
  });

  final String id;
  final String catalogCode;
  final String name;
  final String type;
  final String result;
  final String? note;
  final String? updatedByUserName;

  bool get passed => result == 'pass';
  bool get isLabor => type == 'labor';

  static QcChecklistItem fromJson(Map<String, dynamic> j) => QcChecklistItem(
        id: j['id'] as String,
        catalogCode: j['catalogCode'] as String? ?? '',
        name: j['name'] as String? ?? '',
        type: j['type'] as String? ?? 'part',
        result: j['result'] as String? ?? 'pending',
        note: j['note'] as String?,
        updatedByUserName: j['updatedByUserName'] as String?,
      );
}

class QcChecklist {
  const QcChecklist({
    required this.id,
    required this.jobId,
    required this.isLocked,
    this.testDriveKm,
    this.testDriveNote,
    this.testDriveRecordedAt,
    required this.items,
  });

  final String id;
  final String jobId;
  final bool isLocked;
  final double? testDriveKm;
  final String? testDriveNote;
  final DateTime? testDriveRecordedAt;
  final List<QcChecklistItem> items;

  bool get allPassed => items.isNotEmpty && items.every((i) => i.passed);
  bool get testDriveRecorded => testDriveRecordedAt != null;
  int get passedCount => items.where((i) => i.passed).length;

  /// ตรงกับ guard QcPassed ที่ server คำนวณ — ใช้บอกเหตุผลที่ปุ่มยังกดไม่ได้เท่านั้น
  bool get readyToPass => allPassed && testDriveRecorded && !isLocked;

  static QcChecklist fromJson(Map<String, dynamic> j) => QcChecklist(
        id: j['id'] as String,
        jobId: j['jobId'] as String,
        isLocked: j['isLocked'] as bool? ?? false,
        testDriveKm: (j['testDriveKm'] as num?)?.toDouble(),
        testDriveNote: j['testDriveNote'] as String?,
        testDriveRecordedAt: j['testDriveRecordedAt'] == null
            ? null
            : DateTime.tryParse(j['testDriveRecordedAt'] as String)?.toLocal(),
        items: ((j['items'] as List<dynamic>?) ?? const [])
            .map((e) => QcChecklistItem.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
