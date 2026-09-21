import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../api/client.dart';
import '../../../core/roles.dart';
import '../data/work_providers.dart';
import 'work_focus_bar.dart';

/// ตัวครอบทั้งแอปที่ทำให้แถบจับเวลาเห็นได้ทุกหน้า
///
/// **ต้อง mount ที่ `MaterialApp.router builder:` ไม่ใช่ที่ `AppShell`** — หน้าที่ช่างใช้จริงตอนทำงาน
/// (`/jobs/:jobId` และลูกๆ ของมัน: chat, intake, qc, payment, handover) ถูก push ด้วย
/// `rootNavigatorKey` ซึ่งอยู่ **นอก** `StatefulShellRoute` ทั้งหมด ถ้าแทรกที่ AppShell แถบจะหายไป
/// พอดีตอนที่ช่างต้องเห็นมากที่สุด
///
/// ใช้ [Column] ไม่ใช่ [Stack] ด้วยเหตุผลสองข้อ:
/// 1. `Stack` จะลอยทับ scrim ของ dialog/bottom sheet ที่ push อยู่ใน Navigator ข้างใต้ builder
///    รวมถึง dialog ยืนยันสลับคันของฟีเจอร์นี้เอง
/// 2. `Column` ทำให้แถบกินพื้นที่จริง ไม่ทับเนื้อหา — ทุกหน้าในแอปใช้ `ListView` ที่ไม่มี bottom
///    padding เผื่อไว้ ถ้าใช้ Stack ต้องไล่เติม padding ทุกหน้า
class WorkFocusHost extends ConsumerWidget {
  const WorkFocusHost({super.key, required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final session = ref.watch(sessionProvider);
    final role = AppRole.parse(session?.user.role);
    final tracking = ref.watch(currentWorkProvider).current != null;

    // คีย์บอร์ดเปิดอยู่ (หน้าแชท/ฟอร์ม) — ซ่อนไปเลยดีกว่าไปสู้กับ resizeToAvoidBottomInset
    // ของ Scaffold ที่อยู่ข้างใน ซึ่งไม่รู้ว่ามีแถบนี้อยู่
    final keyboardOpen = MediaQuery.viewInsetsOf(context).bottom > 0;

    final show = session != null && role.canTrackWorkTime && tracking && !keyboardOpen;
    if (!show) return child;

    return Column(
      children: [
        // แถบกินพื้นที่ safe area ล่างเองแล้ว — ถ้าไม่ตัดของ child ออก หน้าที่มี StickyActionBar
        // หรือ NavigationBar (ซึ่งบวก padding.bottom เองทั้งคู่) จะได้ช่องว่างซ้อนสองชั้น
        Expanded(child: MediaQuery.removePadding(removeBottom: true, context: context, child: child)),
        const WorkFocusBar(),
      ],
    );
  }
}
