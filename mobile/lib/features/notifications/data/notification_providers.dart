import 'dart:math';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../api/client.dart';
import '../../../app/routes.dart';
import '../../../models/app_notification.dart';

/// จำนวนแจ้งเตือนที่ยังไม่อ่าน — null = ยังไม่รู้ (ยังไม่เคยโหลด/เพิ่งเปลี่ยนเซสชัน)
///
/// ไม่ autoDispose เพราะกระดิ่งอยู่ทุกแท็บและ poller อยู่เหนือ router ตลอดอายุแอป
/// · watch session เพื่อรีเซ็ตเองเมื่อออกจากระบบ/สลับบัญชี ไม่งั้นตัวเลขของคนก่อนค้างบนกระดิ่ง
final unreadNotificationCountProvider =
    NotifierProvider<UnreadNotificationCount, int?>(UnreadNotificationCount.new);

class UnreadNotificationCount extends Notifier<int?> {
  bool _loading = false;

  /// หน้ารายการแจ้งเตือนเปิดอยู่ — ไม่ต้องเด้ง snackbar ซ้ำกับสิ่งที่ผู้ใช้กำลังดูอยู่
  bool pageOpen = false;

  @override
  int? build() {
    ref.watch(sessionProvider.select((s) => s?.accessToken));
    return null;
  }

  /// คืนจำนวนที่ **เพิ่มขึ้น** จากค่าที่รู้ล่าสุด ให้ poller ตัดสินใจเด้ง snackbar
  /// · โหลดครั้งแรก (ยังไม่รู้ค่าเดิม) คืน 0 — เปิดแอปมาแล้วเจอ snackbar ทุกครั้งน่ารำคาญ กระดิ่งบอกพอแล้ว
  /// · error เงียบไว้ (เน็ตหลุด/server ล่ม) — กระดิ่งไม่ใช่เหตุให้รบกวนผู้ใช้ 401 มี ApiClient จัดการแล้ว
  Future<int> refresh() async {
    if (_loading || ref.read(sessionProvider) == null) return 0;
    _loading = true;
    try {
      final previous = state;
      final next = await ref.read(notificationsApiProvider).unreadCount();
      if (!ref.mounted) return 0;
      state = next;
      return previous == null ? 0 : max(0, next - previous);
    } catch (_) {
      return 0;
    } finally {
      _loading = false;
    }
  }

  /// ทุกการกระทำ (อ่าน/ยังไม่อ่าน/อ่านทั้งหมด) คืนจำนวนล่าสุดมาอยู่แล้ว — ใช้ค่านั้นตรงๆ
  /// ตัวเลขที่ลดลงเพราะผู้ใช้กดเองจึงไม่ทำให้ poll รอบถัดไปเข้าใจผิดว่ามีของใหม่
  void setFromServer(int unread) => state = unread;
}

/// ที่หมายเมื่อแตะการ์ด — null = ไม่มีหน้าในแอปให้เปิด (PR/PO อยู่บนเว็บเท่านั้น)
///
/// ใบเสนอราคาเปิดหน้าจ๊อบ ไม่ใช่หน้าอนุมัติ: แจ้งเตือนเกิดตอนลูกค้าเซ็นแล้ว ใบจึงปิดรับการตัดสินใจไปแล้ว
/// ตรงกับเว็บ (`notificationHref` ใน web/src/api/notifications.ts) ที่เปิดการ์ดจ๊อบขั้นเสนอราคา
String? notificationTarget(AppNotification n) {
  final jobId = n.jobId;
  // ผลตรวจการแจ้งชำระแพ็กเกจไม่ผูกจ๊อบ พาไปหน้าแพ็กเกจ (ตรงกับ linkHint "package" ของ server)
  if (n.entityType == packagePaymentEntityType || n.linkHint == 'package') return Routes.package;
  if (n.entityType == 'PR' || n.entityType == 'PO') return null;
  if (jobId == null || jobId.isEmpty) return null;
  if (n.linkHint == 'chat') return Routes.jobChat(jobId);
  return Routes.job(jobId);
}

/// ตัวเลขบนกระดิ่ง — ไม่ให้ป้ายกว้างจนบังไอคอน
String unreadBadgeLabel(int count) => count > 99 ? '99+' : '$count';
