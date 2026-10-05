import 'dart:async';

import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';

/// คอยถาม server ว่าบัญชียังใช้งานได้ไหม แล้วให้ ApiClient ออกจากระบบให้เองถ้าไม่ได้
///
/// [SECURITY] token มือถือไม่หมดอายุแล้ว (คำขอผู้ใช้ 2026-10-05) — การตัดสิทธิ์อยู่ที่ server
/// (`SessionRevocationMiddleware` ตรวจ Staff.Status/สาขา/สิทธิ์ทุกคำขอ) ตัวนี้แค่ทำให้มี "คำขอ" เกิดขึ้น
/// ตอนที่แอปเปิดค้างไว้เฉยๆ ไม่งั้นพนักงานที่ถูกปิดใช้งานจะยังเห็นข้อมูลบนจอจนกว่าจะกดอะไรสักอย่าง
///
/// ตรวจ 3 จังหวะ: ตอนเปิดแอปที่มีเซสชันค้างอยู่ · ทุกครั้งที่กลับมาจาก background · ทุก [_interval]
/// ระหว่างเปิดค้าง — ไม่ตรวจตอนอยู่ background (ไม่มีใครเห็นจอ และเปลืองแบต)
///
/// ไม่ต้องจัดการผลลัพธ์เอง: 401 `AUTH_REQUIRED` ถูก `_handleAuthFailure` ใน api/client.dart รับไป
/// ล้างเซสชัน + แสดงเหตุผลจาก server · error อื่น (เน็ตหลุด/server ล่ม) เงียบไว้ — ไม่ใช่เหตุให้ออกจากระบบ
class SessionGuard extends ConsumerStatefulWidget {
  const SessionGuard({super.key, required this.child});

  final Widget child;

  @override
  ConsumerState<SessionGuard> createState() => _SessionGuardState();
}

class _SessionGuardState extends ConsumerState<SessionGuard> {
  /// server cache ผลตรวจไว้ 60 วิอยู่แล้ว (Jwt:SessionCheckSeconds) — ถี่กว่านี้ไม่ได้ข้อมูลสดขึ้น
  static const _interval = Duration(minutes: 5);

  Timer? _timer;
  AppLifecycleListener? _lifecycle;
  bool _checking = false;

  @override
  void initState() {
    super.initState();
    _lifecycle = AppLifecycleListener(onResume: _onResume, onPause: _stopTimer);
    // เปิดแอปมาพร้อมเซสชันเดิม = จังหวะแรกที่ต้องตรวจ · เข้าสู่ระบบใหม่/ออกจากระบบ = เริ่ม/หยุดนับ
    ref.listenManual(sessionProvider, (previous, next) {
      if (next == null) {
        _stopTimer();
      } else if (previous == null) {
        _startTimer();
        unawaited(_check());
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
    unawaited(_check());
  }

  void _startTimer() {
    _timer?.cancel();
    _timer = Timer.periodic(_interval, (_) => unawaited(_check()));
  }

  void _stopTimer() {
    _timer?.cancel();
    _timer = null;
  }

  Future<void> _check() async {
    if (_checking || ref.read(sessionProvider) == null) return;
    _checking = true;
    try {
      await ref.read(authApiProvider).ping();
    } catch (_) {
      // ดูคอมเมนต์ของคลาส — 401 ถูกจัดการที่ ApiClient แล้ว ที่เหลือไม่ใช่เหตุให้ออกจากระบบ
    } finally {
      _checking = false;
    }
  }

  @override
  Widget build(BuildContext context) => widget.child;
}
