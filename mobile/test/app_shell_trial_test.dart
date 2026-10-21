import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/app/routes.dart';
import 'package:garage_pro_service_ops/core/roles.dart';
import 'package:garage_pro_service_ops/features/shell/app_shell.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:garage_pro_service_ops/widgets/trial_banner.dart';
import 'package:go_router/go_router.dart';

/// แถบทดลองใช้ใน AppShell — ต้องเห็นทุกแท็บ และไม่ทำให้ AppBar ของแท็บเว้นช่อง status bar ซ้ำ
void main() {
  Widget app(PlanInfo? plan, {String role = 'Manager'}) {
    final router = GoRouter(
      initialLocation: '/b0',
      routes: [
        StatefulShellRoute.indexedStack(
          builder: (_, _, shell) => AppShell(navigationShell: shell),
          branches: [
            for (var i = 0; i < 5; i++)
              StatefulShellBranch(routes: [
                GoRoute(
                  path: '/b$i',
                  builder: (_, _) => Scaffold(appBar: AppBar(title: Text('แท็บ $i'))),
                ),
              ]),
          ],
        ),
        GoRoute(
          path: Routes.package,
          builder: (_, _) => const Scaffold(body: Text('หน้าแพ็กเกจ')),
        ),
      ],
    );

    return ProviderScope(
      overrides: [sessionProvider.overrideWith(() => _FakeSession(role, plan))],
      child: MaterialApp.router(routerConfig: router),
    );
  }

  testWidgets('ลูกค้าเดิมไม่มีแถบ และ AppBar อยู่บนสุดเหมือนเดิม', (tester) async {
    // จำลองจอที่มี status bar/notch — ปัญหาเว้นช่องซ้ำเห็นได้เฉพาะเมื่อ padding บนไม่ใช่ 0
    tester.view.padding = const FakeViewPadding(top: 141); // 47pt × 3
    addTearDown(tester.view.reset);

    await tester.pumpWidget(app(null));
    await tester.pump();

    expect(find.byType(TrialBanner), findsNothing);
    expect(tester.getTopLeft(find.byType(AppBar)).dy, 0);
    // AppBar ยังเว้นช่อง status bar ให้เอง
    expect(tester.getTopLeft(find.text('แท็บ 0')).dy, greaterThan(40));
  });

  testWidgets('อู่ทดลองใช้เห็นแถบบนสุด และ AppBar ต่อใต้แถบโดยไม่เว้นช่อง status bar ซ้ำ',
      (tester) async {
    tester.view.padding = const FakeViewPadding(top: 141);
    addTearDown(tester.view.reset);

    await tester.pumpWidget(app(const PlanInfo(kindName: 'trial', daysLeft: 5)));
    await tester.pump();

    expect(find.text('ทดลองใช้ · เหลือ 5 วัน'), findsOneWidget);

    final bannerBottom = tester.getBottomLeft(find.byType(TrialBanner)).dy;
    final appBarRect = tester.getRect(find.byType(AppBar));
    expect(appBarRect.top, bannerBottom);
    // ไม่มีช่อง status bar ซ้อนใน AppBar = สูงเท่า toolbar ปกติ
    expect(appBarRect.height, kToolbarHeight);
  });

  testWidgets('อ่านอย่างเดียวแสดงแถบแดงในแท็บอื่นด้วย', (tester) async {
    await tester.pumpWidget(app(const PlanInfo(kindName: 'trial', readOnly: true)));
    await tester.pump();

    await tester.tap(find.text('โปรไฟล์'));
    await tester.pump();

    expect(find.text('แท็บ 4'), findsOneWidget);
    expect(find.text('ช่วงทดลองใช้ครบแล้ว ดูข้อมูลได้ แต่แก้ไขไม่ได้'), findsOneWidget);
  });

  testWidgets('แตะแถบทดลองใช้แล้วไปหน้าแพ็กเกจ', (tester) async {
    await tester.pumpWidget(app(const PlanInfo(kindName: 'trial', daysLeft: 5)));
    await tester.pump();

    await tester.tap(find.text('ทดลองใช้ · เหลือ 5 วัน'));
    await tester.pumpAndSettle();

    expect(find.text('หน้าแพ็กเกจ'), findsOneWidget);
  });

  testWidgets('อู่ที่ชำระเงินแล้วและใกล้หมดเห็นแถบเตือน', (tester) async {
    await tester.pumpWidget(app(const PlanInfo(kindName: 'paid', daysLeft: 10)));
    await tester.pump();
    expect(find.text('แพ็กเกจ · เหลือ 10 วัน'), findsOneWidget);
  });

  testWidgets('อู่ที่ชำระเงินแล้วและยังเหลือนานไม่เห็นแถบ', (tester) async {
    await tester.pumpWidget(app(const PlanInfo(kindName: 'paid', daysLeft: 40)));
    await tester.pump();
    expect(find.byType(TrialBanner), findsNothing);
  });

  testWidgets('อู่ที่ถูกระงับแตะแถบไม่ได้ (แจ้งชำระเองไม่ได้)', (tester) async {
    await tester.pumpWidget(app(const PlanInfo(kindName: 'suspended')));
    await tester.pump();

    expect(find.byIcon(Icons.chevron_right), findsNothing);
    await tester.tap(find.textContaining('ถูกระงับ'));
    await tester.pumpAndSettle();
    expect(find.text('หน้าแพ็กเกจ'), findsNothing);
  });

  /// [BIZ] แท็บรายงานยังอยู่สำหรับเจ้าของอู่ทดลองใช้ — "วันนี้" ไม่ใช่รายงานขั้นสูง
  /// ส่วนแท็บย่อยที่ล็อกแสดงกุญแจ + ข้อความ server ภายในหน้ารายงานเอง (ไม่ซ่อนทั้งแท็บ)
  test('แพ็กเกจไม่เปลี่ยนชุดแท็บตามบทบาท', () {
    expect(tabsForRole(AppRole.parse('Manager')).map((t) => t.label), contains('รายงาน'));
    expect(tabsForRole(AppRole.parse('Office')).map((t) => t.label), contains('รายงาน'));
    expect(tabsForRole(AppRole.parse('Technician')).map((t) => t.label), isNot(contains('รายงาน')));
  });
}

class _FakeSession extends SessionNotifier {
  _FakeSession(this.role, this.plan);
  final String role;
  final PlanInfo? plan;

  @override
  Session? build() => Session(
        accessToken: 'tok',
        expiresAt: DateTime.utc(2036, 1, 1),
        user: AuthUser(
          userId: 1,
          userName: 'SP1042.M01',
          displayName: 'เจ้าของอู่',
          role: role,
          roleLabelTh: 'ผู้จัดการสาขา',
          shardKey: 'sp1',
          canSeeCost: true,
          canCloseShift: true,
          plan: plan,
        ),
        branchId: 1001,
        branchName: 'อู่ช่างเอก',
      );
}
