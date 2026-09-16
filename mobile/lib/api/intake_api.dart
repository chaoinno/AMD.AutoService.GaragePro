import 'api_client.dart';
import '../models/intake.dart';

class IntakeApi {
  IntakeApi(this._c);
  final ApiClient _c;

  /// แคตตาล็อกคงที่ 4 หมวด 20 รายการ — โหลดครั้งเดียวแล้ว cache ได้
  Future<List<IntakeChecklistTemplateItem>> template() async {
    final data = await _c.get<List<dynamic>>('/intake-checklist/template');
    return data
        .map((e) => IntakeChecklistTemplateItem.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<IntakeChecklist> get(String jobId) async {
    final data = await _c.get<Map<String, dynamic>>('/jobs/$jobId/intake-checklist');
    return IntakeChecklist.fromJson(data);
  }

  /// result: "pending" | "ok" | "issue" | "na" — [BIZ] issue/na ต้องมี note เสมอ
  Future<IntakeChecklistItem> saveItem(String jobId, String itemCode,
      {required String result, String? note}) async {
    final data = await _c.put<Map<String, dynamic>>(
      '/jobs/$jobId/intake-checklist/items/$itemCode',
      body: {'result': result, 'note': note},
    );
    return IntakeChecklistItem.fromJson(data);
  }

  /// ส่งแล้วล็อก แก้ไม่ได้อีก — ส่งไม่ได้ถ้ายังมีรายการ pending (INTAKE_INCOMPLETE)
  Future<void> submit(String jobId) =>
      _c.post<Map<String, dynamic>>('/jobs/$jobId/intake-checklist/submit');
}
