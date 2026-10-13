import 'api_client.dart';
import '../models/app_notification.dart';

/// แจ้งเตือนของผู้ใช้ที่เข้าสู่ระบบอยู่ — server คัดตามพนักงาน/บทบาท/สาขาจาก token เอง
/// (NotificationsController) · การกระทำทุกตัวคืนจำนวนยังไม่อ่านล่าสุด ไม่ต้องยิงนับซ้ำ
class NotificationsApi {
  NotificationsApi(this._c);
  final ApiClient _c;

  /// keyset ใหม่→เก่า ย้อนหลัง 30 วัน · `beforeAt` ต้องเป็นค่าดิบจาก [AppNotification.createdAtRaw]
  Future<NotificationPage> list({
    bool unreadOnly = false,
    String? beforeAt,
    String? beforeId,
    int take = 30,
  }) async {
    final data = await _c.get<Map<String, dynamic>>('/notifications', query: {
      if (unreadOnly) 'unreadOnly': true,
      'beforeAt': ?beforeAt,
      'beforeId': ?beforeId,
      'take': take,
    });
    return NotificationPage.fromJson(data);
  }

  Future<int> unreadCount() => _count(_c.get<Map<String, dynamic>>('/notifications/unread-count'));

  /// ทำซ้ำได้ — server เก็บเวลาที่อ่านครั้งแรกไว้
  Future<int> markRead(String id) =>
      _count(_c.post<Map<String, dynamic>>('/notifications/$id/read'));

  Future<int> markUnread(String id) =>
      _count(_c.delete<Map<String, dynamic>>('/notifications/$id/read'));

  Future<int> markAllRead() => _count(_c.post<Map<String, dynamic>>('/notifications/read-all'));

  static Future<int> _count(Future<Map<String, dynamic>> request) async =>
      ((await request)['unread'] as num?)?.toInt() ?? 0;
}
