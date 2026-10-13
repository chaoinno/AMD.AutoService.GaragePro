import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../../app/routes.dart';
import '../../../core/tokens.dart';
import '../data/notification_providers.dart';

/// กระดิ่งบน AppBar ของทุกแท็บหลัก — ตัวเลขมาจาก [unreadNotificationCountProvider] ที่ poller อัปเดตให้
/// กระดิ่งเองไม่ยิง API (ไม่งั้นทุกแท็บที่ถูกสร้างจะยิงซ้ำกัน)
class NotificationBell extends ConsumerWidget {
  const NotificationBell({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // ยังไม่รู้ค่า (กำลังโหลด/โหลดไม่ผ่าน) = ไม่แสดงตัวเลข — ป้ายหลอกแย่กว่าป้ายที่มาช้า
    final unread = ref.watch(unreadNotificationCountProvider) ?? 0;

    return IconButton(
      tooltip: unread > 0 ? 'การแจ้งเตือน · ยังไม่อ่าน $unread รายการ' : 'การแจ้งเตือน',
      onPressed: () async {
        await context.push(Routes.notifications);
        // อ่าน/สลับสถานะในหน้ารายการแล้ว server คืนตัวเลขล่าสุดมาให้ตลอด แต่กลับจากหน้าปลายทาง
        // (แชท/จ๊อบ) อาจมีเรื่องใหม่เข้ามาระหว่างนั้น — นับใหม่หนึ่งครั้ง
        await ref.read(unreadNotificationCountProvider.notifier).refresh();
      },
      icon: Stack(
        clipBehavior: Clip.none,
        children: [
          Icon(unread > 0 ? Icons.notifications_active_outlined : Icons.notifications_none),
          if (unread > 0)
            Positioned(
              top: -6,
              right: -8,
              child: Container(
                constraints: const BoxConstraints(minWidth: 18),
                padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 1),
                decoration: BoxDecoration(
                  color: T.red600,
                  borderRadius: BorderRadius.circular(9),
                  // ขอบสีเดียวกับ AppBar ให้ป้ายอ่านออกแม้ทับเส้นไอคอน (แบบเดียวกับจุดแชทในหน้าจ๊อบ)
                  border: Border.all(color: T.navy900, width: 1.5),
                ),
                child: Text(
                  unreadBadgeLabel(unread),
                  textAlign: TextAlign.center,
                  // ป้ายเกาะไอคอนขนาดตายตัว — ขยายตามตัวอักษรระบบได้บ้างแต่ไม่ให้ล้นจนบังปุ่มข้างๆ
                  // (tooltip/semantics บอกจำนวนเต็มอยู่แล้ว)
                  textScaler: MediaQuery.textScalerOf(context).clamp(maxScaleFactor: 1.3),
                  style: const TextStyle(
                    color: Colors.white,
                    fontSize: 11,
                    height: 1.3,
                    fontWeight: FontWeight.w700,
                    fontFeatures: [FontFeature.tabularFigures()],
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}
