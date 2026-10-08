import 'api_client.dart';
import '../models/handover.dart';

class HandoverApi {
  HandoverApi(this._c);
  final ApiClient _c;

  /// เปิดหน้าส่งมอบครั้งแรกจะสร้างเช็คลิสต์ของในรถให้เอง
  Future<Handover> get(String jobId) async {
    final data = await _c.get<Map<String, dynamic>>('/jobs/$jobId/handover');
    return Handover.fromJson(data);
  }

  /// [BIZ] ไม่คืนต้องมีเหตุผล (HANDOVER_NOTE_REQUIRED)
  Future<HandoverChecklistItem> saveItem(String jobId, String itemId,
      {required bool isReturned, String? note}) async {
    final data = await _c.put<Map<String, dynamic>>(
      '/jobs/$jobId/handover/items/$itemId',
      body: {'isReturned': isReturned, 'note': note},
    );
    return HandoverChecklistItem.fromJson(data);
  }

  /// เลขไมล์ตอนส่งมอบ + นัดครั้งถัดไป (ไมล์ + จำนวนเดือน — server คำนวณวันที่ให้) — ต้องบันทึกก่อนเซ็น
  Future<Handover> saveServiceInfo(String jobId,
      {required int mileageAtHandover, required int nextServiceMileage, required int nextServiceMonths}) async {
    final data = await _c.put<Map<String, dynamic>>('/jobs/$jobId/handover/service-info', body: {
      'mileageAtHandover': mileageAtHandover,
      'nextServiceMileage': nextServiceMileage,
      'nextServiceMonths': nextServiceMonths,
    });
    return Handover.fromJson(data);
  }

  /// ต้องตัดสินใจครบทุกรายการ บันทึกไมล์/นัดครั้งถัดไป และมีลายเซ็นก่อน — ส่งแล้วล็อก
  Future<Handover> submit(String jobId, String signatureAttachmentPath) async {
    final data = await _c.put<Map<String, dynamic>>('/jobs/$jobId/handover/submit',
        body: {'signatureAttachmentPath': signatureAttachmentPath});
    return Handover.fromJson(data);
  }
}
