import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/app/routes.dart';
import 'package:garage_pro_service_ops/features/shell/profile_page.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:go_router/go_router.dart';

/// ปุ่ม "แพ็กเกจและการชำระเงิน" ในโปรไฟล์ต้องมีสำหรับทุกบัญชีของ ServicePro (docs/17 D9):
/// อู่ที่สมัครเอง (sp1) และสาขา GaragePro กลุ่ม Service ใน db2 ที่ไม่มีแพ็กเกจในเซสชัน
void main() {
  Future<void> pumpProfile(WidgetTester tester, {required bool selfServe}) async {
    final router = GoRouter(
      initialLocation: Routes.profile,
      routes: [
        GoRoute(path: Routes.profile, builder: (_, _) => const ProfilePage()),
        GoRoute(path: Routes.package, builder: (_, _) => const Scaffold(body: Text('หน้าแพ็กเกจ'))),
      ],
    );
    addTearDown(router.dispose);

    await tester.pumpWidget(ProviderScope(
      overrides: [sessionProvider.overrideWith(() => _FakeSession(selfServe: selfServe))],
      child: MaterialApp.router(routerConfig: router),
    ));
    await tester.pumpAndSettle();
  }

  testWidgets('สาขา GaragePro (ไม่มีแพ็กเกจในเซสชัน) เห็นปุ่ม และแตะแล้วไปหน้าแพ็กเกจ', (tester) async {
    await pumpProfile(tester, selfServe: false);

    expect(find.text('แพ็กเกจและการชำระเงิน'), findsOneWidget);
    expect(find.text('ดูแพ็กเกจและแจ้งชำระเงิน'), findsOneWidget);

    await tester.tap(find.text('แพ็กเกจและการชำระเงิน'));
    await tester.pumpAndSettle();
    expect(find.text('หน้าแพ็กเกจ'), findsOneWidget);
  });

  testWidgets('อู่ที่สมัครเองเห็นสรุปแพ็กเกจจากเซสชันเหมือนเดิม', (tester) async {
    await pumpProfile(tester, selfServe: true);

    expect(find.text('แพ็กเกจและการชำระเงิน'), findsOneWidget);
    expect(find.text('SP1042 · ทดลองใช้ · เหลือ 23 วัน'), findsOneWidget);
  });
}

class _FakeSession extends SessionNotifier {
  _FakeSession({required this.selfServe});
  final bool selfServe;

  @override
  Session? build() => Session(
        accessToken: 'tok',
        expiresAt: DateTime.utc(2036, 1, 1),
        user: AuthUser(
          userId: 1,
          userName: selfServe ? 'SP1042.M01' : '22701',
          displayName: 'ผู้จัดการ',
          role: 'Manager',
          roleLabelTh: 'ผู้จัดการสาขา',
          shardKey: selfServe ? 'sp1' : 'db2',
          canSeeCost: true,
          canCloseShift: true,
          plan: selfServe ? const PlanInfo(kindName: 'trial', daysLeft: 23) : null,
          workspace: selfServe ? const WorkspaceInfo(workspaceNo: 'SP1042', name: 'อู่ช่างเอก') : null,
        ),
        branchId: selfServe ? 1001 : 227,
        branchName: selfServe ? 'อู่ช่างเอก' : 'Service Center Demo',
      );
}
