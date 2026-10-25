import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/auth_api.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/app/navigation.dart';
import 'package:garage_pro_service_ops/app/routes.dart';
import 'package:garage_pro_service_ops/features/shell/branch_switcher.dart';
import 'package:garage_pro_service_ops/features/shell/profile_page.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// สลับสาขาของผู้ดูแลระบบบนมือถือ: เมนูเห็นเฉพาะ admin · สำเร็จแล้วเซสชันเปลี่ยนเป็นสาขา/บัญชีใหม่
Session _session({bool admin = true, PlanInfo? plan, int branchId = 227}) => Session(
      accessToken: 'tok-$branchId',
      expiresAt: DateTime.utc(2036, 1, 1),
      user: AuthUser(
        userId: 1,
        userName: '22701',
        displayName: 'ผู้ดูแล',
        role: 'Manager',
        roleLabelTh: 'ผู้จัดการสาขา',
        shardKey: plan == null ? 'db2' : 'sp1',
        canSeeCost: true,
        canCloseShift: true,
        isAdministrator: admin,
        plan: plan,
      ),
      branchId: branchId,
      branchName: 'Service Center Demo',
    );

class _FakeAuthApi extends AuthApi {
  _FakeAuthApi({this.error}) : super(ApiClient(readToken: () => null));
  final ApiException? error;
  final switched = <int>[];

  @override
  Future<List<SwitchableBranch>> switchableBranches() async => const [
        SwitchableBranch(branchId: 227, name: 'Service Center Demo', isCurrent: true),
        SwitchableBranch(branchId: 310, name: 'สาขารังสิต'),
      ];

  @override
  Future<LoginResult> switchBranch(int branchId) async {
    switched.add(branchId);
    if (error != null) throw error!;
    return LoginResult(
      accessToken: 'tok-$branchId',
      expiresAt: DateTime.utc(2036, 1, 1),
      user: _session().user,
      branchId: branchId,
      branchName: 'สาขารังสิต',
    );
  }
}

void main() {
  group('branchSwitchAvailability', () {
    test('ผู้ใช้ทั่วไปไม่เห็น', () {
      expect(branchSwitchAvailability(_session(admin: false)), BranchSwitchAvailability.hidden);
    });

    test('ผู้ดูแลระบบ GaragePro เดิม (ไม่มีแพ็กเกจ) สลับได้', () {
      expect(branchSwitchAvailability(_session()), BranchSwitchAvailability.available);
    });

    test('อู่ทดลองใช้ที่ไม่มี multiBranch ถูกล็อกพร้อมเหตุผล', () {
      final s = _session(plan: const PlanInfo(kindName: 'trial', features: ['pos']));
      expect(branchSwitchAvailability(s), BranchSwitchAvailability.locked);
    });

    test('แพ็กเกจเต็มสลับได้', () {
      expect(branchSwitchAvailability(_session(plan: const PlanInfo(kindName: 'paid'))),
          BranchSwitchAvailability.available);
    });
  });

  test('AuthUser เก็บ isAdministrator ผ่าน json และเซสชันรุ่นเก่าได้ false', () {
    final user = _session().user;
    expect(AuthUser.fromJson(user.toJson()).isAdministrator, isTrue);

    final old = user.toJson()..remove('isAdministrator');
    expect(AuthUser.fromJson(old).isAdministrator, isFalse);
  });

  test('/auth/me เติม isAdministrator ให้เซสชันรุ่นเก่า โดยไม่ต้องเข้าระบบใหม่', () async {
    expect(MeResult.fromJson({'user': {'isAdministrator': true}}).isAdministrator, isTrue);
    expect(MeResult.fromJson({'user': <String, dynamic>{}}).isAdministrator, isNull);

    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    final container = ProviderContainer(overrides: [sharedPrefsProvider.overrideWithValue(prefs)]);
    addTearDown(container.dispose);

    // เซสชันที่บันทึกจากแอปรุ่นก่อน 1.3.0 — ไม่มีฟิลด์ isAdministrator จึงอ่านได้ false
    final json = _session().toJson();
    (json['user'] as Map<String, dynamic>).remove('isAdministrator');
    final old = Session.fromJson(json);
    await container.read(sessionProvider.notifier).save(old);
    expect(branchSwitchAvailability(container.read(sessionProvider)!), BranchSwitchAvailability.hidden);

    final me = MeResult.fromJson({'user': {'isAdministrator': true}});
    await container.read(sessionProvider.notifier).updatePlan(me.plan, me.workspace,
        expectedToken: old.accessToken, isAdministrator: me.isAdministrator);
    expect(container.read(sessionProvider)!.user.isAdministrator, isTrue);
    expect(branchSwitchAvailability(container.read(sessionProvider)!), isNot(BranchSwitchAvailability.hidden));
  });

  test('filterBranches กรองตามชื่อ ไม่สนตัวพิมพ์', () {
    const all = [
      SwitchableBranch(branchId: 1, name: 'Service Center'),
      SwitchableBranch(branchId: 2, name: 'สาขารังสิต'),
    ];
    expect(filterBranches(all, 'center').map((b) => b.branchId), [1]);
    expect(filterBranches(all, '  ').length, 2);
    expect(filterBranches(all, 'ไม่มี'), isEmpty);
  });

  Future<(ProviderContainer, _FakeAuthApi)> pumpProfile(
    WidgetTester tester, {
    required Session session,
    _FakeAuthApi? api,
  }) async {
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();
    final fake = api ?? _FakeAuthApi();

    final router = GoRouter(
      initialLocation: Routes.profile,
      routes: [
        GoRoute(path: Routes.profile, builder: (_, _) => const ProfilePage()),
        GoRoute(path: Routes.home, builder: (_, _) => const Scaffold(body: Text('หน้าหลัก'))),
        GoRoute(path: Routes.package, builder: (_, _) => const Scaffold(body: Text('แพ็กเกจ'))),
      ],
    );
    addTearDown(router.dispose);

    final container = ProviderContainer(overrides: [
      sharedPrefsProvider.overrideWithValue(prefs),
      authApiProvider.overrideWithValue(fake),
    ]);
    addTearDown(container.dispose);
    await container.read(sessionProvider.notifier).save(session);

    await tester.pumpWidget(UncontrolledProviderScope(
      container: container,
      child: MaterialApp.router(routerConfig: router, scaffoldMessengerKey: scaffoldMessengerKey),
    ));
    await tester.pumpAndSettle();
    return (container, fake);
  }

  testWidgets('ผู้ใช้ทั่วไปไม่เห็นเมนูสลับสาขา', (tester) async {
    await pumpProfile(tester, session: _session(admin: false));
    expect(find.text('สลับสาขา'), findsNothing);
  });

  testWidgets('อู่ทดลองใช้เห็นเมนูแต่กดไม่ได้ และบอกเหตุผล', (tester) async {
    await pumpProfile(
        tester, session: _session(plan: const PlanInfo(kindName: 'trial', features: [])));
    expect(find.text('สลับสาขา'), findsOneWidget);
    expect(find.text(branchSwitchLockedReason), findsOneWidget);

    await tester.tap(find.text('สลับสาขา'));
    await tester.pumpAndSettle();
    expect(find.text('เลือกสาขาที่ต้องการใช้งาน ระบบจะเข้าด้วยบัญชีผู้ดูแลระบบของสาขานั้น'),
        findsNothing);
  });

  testWidgets('ผู้ดูแลระบบสลับสำเร็จ: เซสชันเปลี่ยน กลับหน้าหลัก และแจ้งสาขาใหม่', (tester) async {
    final (container, api) = await pumpProfile(tester, session: _session());

    await tester.tap(find.text('สลับสาขา'));
    await tester.pumpAndSettle();

    // สาขาปัจจุบันมีป้ายและกดไม่ได้
    expect(find.text('สาขาปัจจุบัน'), findsOneWidget);
    await tester.tap(find.text('Service Center Demo').last);
    await tester.pumpAndSettle();
    expect(api.switched, isEmpty);

    await tester.tap(find.text('สาขารังสิต'));
    await tester.pumpAndSettle();

    expect(api.switched, [310]);
    final session = container.read(sessionProvider)!;
    expect(session.branchId, 310);
    expect(session.accessToken, 'tok-310');
    expect(find.text('หน้าหลัก'), findsOneWidget);
    expect(find.textContaining('สลับไปสาขา สาขารังสิต แล้ว'), findsOneWidget);
  });

  testWidgets('สลับไม่สำเร็จ: แสดง messageTh + traceId และไม่เปลี่ยนเซสชัน', (tester) async {
    final api = _FakeAuthApi(
        error: ApiException('AUTH_BRANCH_FORBIDDEN', 'คุณไม่มีบัญชีผู้ดูแลระบบในสาขานี้',
            traceId: 'trace-1'));
    final (container, _) = await pumpProfile(tester, session: _session(), api: api);

    await tester.tap(find.text('สลับสาขา'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('สาขารังสิต'));
    await tester.pumpAndSettle();

    expect(container.read(sessionProvider)!.branchId, 227);
    expect(find.textContaining('คุณไม่มีบัญชีผู้ดูแลระบบในสาขานี้'), findsOneWidget);
    expect(find.textContaining('trace-1'), findsOneWidget);
  });
}
