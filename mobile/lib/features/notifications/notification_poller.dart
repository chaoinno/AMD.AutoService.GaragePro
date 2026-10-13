import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../app/navigation.dart';
import '../../app/router.dart';
import '../../app/routes.dart';
import 'data/notification_providers.dart';

/// ถาม server เป็นระยะว่ามีแจ้งเตือนใหม่ไหม — ยังไม่มี push (FCM/APNs) จึงใช้ polling แบบเดียวกับเว็บ
///
/// จังหวะเดียวกับ `SessionGuard`: ตอนมีเซสชัน · กลับจาก background · ทุก [_interval] ระหว่างเปิดค้าง
/// **ไม่ poll ตอนอยู่ background** (ไม่มีใครเห็นจอ และเปลืองแบต/เน็ตมือถือ) — แลกกับการที่ปิดแอปอยู่จะไม่รู้
/// จนกว่าจะเปิดกลับมา ซึ่งเป็นข้อจำกัดที่ยอมรับไว้จนกว่าจะทำ push
///
/// อยู่ที่ `MaterialApp.builder` (เหนือ router) จึงต้อง navigate ผ่าน [routerProvider] ไม่ใช่ context
class NotificationPoller extends ConsumerStatefulWidget {
  const NotificationPoller({super.key, required this.child});

  final Widget child;

  @override
  ConsumerState<NotificationPoller> createState() => _NotificationPollerState();
}

class _NotificationPollerState extends ConsumerState<NotificationPoller> {
  /// เท่ากับเว็บ (NotificationBell.tsx) — server นับด้วย index ถูก ถี่กว่านี้ก็ไม่ได้ทำให้ทันกว่ามาก
  static const _interval = Duration(seconds: 30);

  Timer? _timer;
  AppLifecycleListener? _lifecycle;

  @override
  void initState() {
    super.initState();
    _lifecycle = AppLifecycleListener(onResume: _onResume, onPause: _stopTimer);
    ref.listenManual(sessionProvider, (previous, next) {
      if (next == null) {
        _stopTimer();
      } else if (previous?.accessToken != next.accessToken) {
        _startTimer();
        unawaited(_poll());
      }
    }, fireImmediately: true);
  }

  @override
  void dispose() {
    _stopTimer();
    _lifecycle?.dispose();
    super.dispose();
  }

  void _onResume() {
    if (ref.read(sessionProvider) == null) return;
    _startTimer();
    unawaited(_poll());
  }

  void _startTimer() {
    _timer?.cancel();
    _timer = Timer.periodic(_interval, (_) => unawaited(_poll()));
  }

  void _stopTimer() {
    _timer?.cancel();
    _timer = null;
  }

  Future<void> _poll() async {
    final counter = ref.read(unreadNotificationCountProvider.notifier);
    final added = await counter.refresh();
    if (!mounted || added <= 0 || counter.pageOpen) return;

    // [UI] บอกจำนวนที่เพิ่ง *เข้ามาใหม่* ไม่ใช่ยอดรวม — ยอดรวมอยู่บนกระดิ่งแล้ว
    showAppMessage(
      'มีการแจ้งเตือนใหม่ $added รายการ',
      actionLabel: 'ดู',
      onAction: () => ref.read(routerProvider).push(Routes.notifications),
    );
  }

  @override
  Widget build(BuildContext context) => widget.child;
}
