import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/features/work/data/work_clock.dart';
import 'package:garage_pro_service_ops/features/work/data/work_providers.dart';
import 'package:garage_pro_service_ops/features/work/widgets/work_focus_host.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:garage_pro_service_ops/models/work_interval.dart';

/// เงื่อนไขว่าแถบจับเวลาจะโผล่หรือไม่ — จุดเดียวที่ตัดสินว่า widget นี้เห็นได้ทั้งแอปหรือไม่เห็นเลย
/// ทดสอบด้วย widget test เพราะ `MediaQuery.viewInsets` (คีย์บอร์ด) จำลองด้วย unit test ไม่ได้
/// และบน simulator ก็กดเปิดคีย์บอร์ดซอฟต์แวร์จากสคริปต์ไม่ได้
void main() {
  Widget host({
    String role = 'Technician',
    bool hasSession = true,
    bool tracking = true,
    double keyboardInset = 0,
  }) {
    final overrides = [
      currentWorkProvider.overrideWith(
        () => _FakeCurrentWork(tracking ? _openInterval : null),
      ),
      // override เสมอแม้กรณีไม่มีเซสชัน ไม่งั้น SessionNotifier ตัวจริงจะไปอ่าน sharedPrefsProvider
      // ซึ่ง throw โดยตั้งใจเมื่อไม่ได้ override ใน main()
      sessionProvider.overrideWith(() => _FakeSession(hasSession ? role : null)),
    ];

    return ProviderScope(
      overrides: overrides,
      child: MediaQuery(
        data: MediaQueryData(viewInsets: EdgeInsets.only(bottom: keyboardInset)),
        child: const Directionality(
          textDirection: TextDirection.ltr,
          child: WorkFocusHost(child: Text('เนื้อหาของหน้า')),
        ),
      ),
    );
  }

  testWidgets('ช่างที่กำลังจับเวลาอยู่เห็นแถบ', (tester) async {
    await tester.pumpWidget(host());
    await tester.pump();

    expect(find.textContaining('กำลังทำ'), findsOneWidget);
    expect(find.text('1ขล-7321', findRichText: true), findsNothing); // อยู่รวมในข้อความเดียว
    expect(find.text('เนื้อหาของหน้า'), findsOneWidget);
  });

  /// คีย์บอร์ดกินครึ่งจอ — ซ่อนแถบดีกว่าไปสู้กับ resizeToAvoidBottomInset ของ Scaffold ข้างใน
  testWidgets('คีย์บอร์ดเปิดอยู่ต้องซ่อนแถบ แต่เนื้อหายังอยู่', (tester) async {
    await tester.pumpWidget(host(keyboardInset: 336));
    await tester.pump();

    expect(find.textContaining('กำลังทำ'), findsNothing);
    expect(find.text('เนื้อหาของหน้า'), findsOneWidget);
  });

  testWidgets('ไม่มีคาบเปิดอยู่ก็ไม่มีแถบ', (tester) async {
    await tester.pumpWidget(host(tracking: false));
    await tester.pump();

    expect(find.textContaining('กำลังทำ'), findsNothing);
  });

  testWidgets('ยังไม่ได้ล็อกอินก็ไม่มีแถบ', (tester) async {
    await tester.pumpWidget(host(hasSession: false, tracking: false));
    await tester.pump();

    expect(find.textContaining('กำลังทำ'), findsNothing);
  });

  /// [BIZ] หัวหน้าช่างจับเวลาไม่ได้ (ยืนยันกับผู้ใช้ 2026-09-21) จึงไม่ควรเห็นแถบและไม่ควรยิง
  /// /work/current ให้เปลืองเน็ตมือถือด้วย
  testWidgets('บทบาทที่จับเวลาไม่ได้ไม่เห็นแถบ', (tester) async {
    for (final role in ['Lead', 'Manager', 'Office', 'Cashier', 'FrontDesk']) {
      await tester.pumpWidget(host(role: role));
      await tester.pump();

      expect(find.textContaining('กำลังทำ'), findsNothing, reason: role);
    }
  });
}

final _openInterval = WorkInterval(
  id: 'i1',
  jobId: 'j1',
  jobNo: 'JB2609180227003',
  vehicleRegistration: '1ขล-7321',
  technicianStaffId: 14806,
  technicianName: 'ช่าง ตรวจสอบ',
  kind: 'work',
  startedAt: DateTime.now(),
  isRework: false,
  isAutoCapped: false,
  isVoided: false,
);

class _FakeCurrentWork extends CurrentWorkController {
  _FakeCurrentWork(this._interval);
  final WorkInterval? _interval;

  @override
  CurrentWorkState build() => CurrentWorkState(
        current: _interval,
        clock: _interval == null
            ? null
            : WorkClock.fromSync(startedAt: _interval.startedAt, serverNow: _interval.startedAt),
      );

  @override
  Future<void> refresh() async {}
}

class _FakeSession extends SessionNotifier {
  _FakeSession(this.role);
  final String? role;

  @override
  Session? build() => role == null
      ? null
      : Session(
        accessToken: 't',
        expiresAt: DateTime.now().add(const Duration(hours: 1)),
        user: AuthUser(
          userId: 7,
          userName: '24052270003',
          displayName: 'ช่าง ตรวจสอบ',
          role: role!,
          roleLabelTh: role!,
          shardKey: 'db2',
          canSeeCost: false,
          canCloseShift: false,
        ),
        branchId: 227,
        branchName: 'Service Center Demo',
      );
}
