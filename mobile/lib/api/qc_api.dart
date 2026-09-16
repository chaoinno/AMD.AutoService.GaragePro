import 'api_client.dart';
import '../models/qc.dart';

class QcApi {
  QcApi(this._c);
  final ApiClient _c;

  /// สร้าง draft อัตโนมัติจากบรรทัดที่ลูกค้าอนุมัติ — ถ้าไม่มีบรรทัดอนุมัติเลยจะได้ QC_NO_APPROVED_LINES
  Future<QcChecklist> get(String jobId) async {
    final data = await _c.get<Map<String, dynamic>>('/jobs/$jobId/qc-checklist');
    return QcChecklist.fromJson(data);
  }

  /// result: "pending" | "pass" — ไม่มี "ไม่ผ่าน" ในระบบนี้
  Future<QcChecklistItem> saveItem(String jobId, String itemId,
      {required String result, String? note}) async {
    final data = await _c.put<Map<String, dynamic>>(
      '/jobs/$jobId/qc-checklist/items/$itemId',
      body: {'result': result, 'note': note},
    );
    return QcChecklistItem.fromJson(data);
  }

  Future<QcChecklist> saveTestDrive(String jobId, {required double km, required String note}) async {
    final data = await _c.put<Map<String, dynamic>>(
      '/jobs/$jobId/qc-checklist/test-drive',
      body: {'km': km, 'note': note},
    );
    return QcChecklist.fromJson(data);
  }
}
