import '../models/service_reminder.dart';
import 'api_client.dart';

class ServiceReminderApi {
  ServiceReminderApi(this._c);
  final ApiClient _c;

  /// รายการนัดบริการตั้งต้นของสาขา (เฉพาะที่เปิดใช้งาน) — ทุกบทบาทอ่านได้
  /// สาขาที่ยังไม่เคยมีรายการจะได้ชุดตั้งต้นให้เองตอนเรียกครั้งแรก
  Future<List<ServiceReminderTemplate>> templates() async {
    final data = await _c.get<List<dynamic>>('/service-reminder-templates');
    return data.map((e) => ServiceReminderTemplate.fromJson(e as Map<String, dynamic>)).toList();
  }
}
