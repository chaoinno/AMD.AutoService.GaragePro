import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/app/navigation.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:garage_pro_service_ops/features/app_updates/app_update_policy.dart';
import 'package:garage_pro_service_ops/features/app_updates/app_update_controller.dart';
import 'package:garage_pro_service_ops/features/app_updates/app_update_guard.dart';

AppUpdatePolicy policy({
  String latest = '1.10.0',
  String minimum = '0.0.0',
  bool enabled = true,
  String platform = 'android',
}) => AppUpdatePolicy(
  platform: platform,
  enabled: enabled,
  latestVersion: latest,
  minimumVersion: minimum,
  message: 'มีเวอร์ชันใหม่ กรุณาอัปเดต',
  revision: 1,
);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() => SharedPreferences.setMockInitialValues({}));
  test('numeric versions and force precedence', () {
    expect(
      AppVersion.parse('1.9.0').compareTo(AppVersion.parse('1.10.0')),
      lessThan(0),
    );
    expect(policy().isAvailable('1.10.0'), false);
    expect(policy().isAvailable('2.0.0'), false);
    expect(policy(minimum: '1.10.0').isRequired('1.9.0'), true);
    expect(
      policy(enabled: false, minimum: '1.10.0').isRequired('1.9.0'),
      false,
    );
    expect(() => AppVersion.parse('1.2.3+4'), throwsFormatException);
    expect(
      () => AppUpdatePolicy.fromJson({
        ...policy().toJson(),
        'minimumVersion': '2.0.0',
      }),
      throwsFormatException,
    );
  });
  test(
    'checks deduplicate, throttle, and bypass throttle on return from Store',
    () async {
      final prefs = await SharedPreferences.getInstance();
      var calls = 0;
      final first = Completer<AppUpdatePolicy>();
      final controller = AppUpdateController(
        platform: 'android',
        installedVersion: '1.9.0',
        prefs: prefs,
        cacheNamespace: 'test',
        load: () {
          calls++;
          return calls == 1 ? first.future : Future.value(policy());
        },
        openUrl: (_) async => true,
      );
      addTearDown(controller.dispose);
      final pending = controller.check();
      await controller.check(force: true);
      expect(calls, 1);
      first.complete(policy());
      await pending;
      await controller.check();
      expect(calls, 1);
      await controller.openStore();
      await controller.resume();
      expect(calls, 2);
    },
  );
  test(
    '24 hour snooze per version never suppresses mandatory policy; failed checks retain cache',
    () async {
      final prefs = await SharedPreferences.getInstance();
      var now = DateTime.utc(2026, 10, 28);
      var fresh = policy();
      var offline = false;
      final controller = AppUpdateController(
        platform: 'android',
        installedVersion: '1.9.0',
        prefs: prefs,
        cacheNamespace: 'test',
        now: () => now,
        load: () async {
          if (offline) throw StateError('offline');
          return fresh;
        },
        openUrl: (_) async => true,
      );
      addTearDown(controller.dispose);
      await controller.check();
      expect(controller.showPrompt, true);
      await controller.snooze();
      expect(controller.showPrompt, false);
      now = now.add(const Duration(hours: 24));
      expect(controller.showPrompt, true);
      await controller.snooze();
      fresh = policy(minimum: '1.10.0');
      await controller.check(force: true);
      expect(controller.requiredUpdate, true);
      expect(controller.showPrompt, true);
      offline = true;
      await controller.check(force: true);
      expect(controller.requiredUpdate, true);
      final restored = AppUpdateController(
        platform: 'android',
        installedVersion: '1.9.0',
        prefs: prefs,
        cacheNamespace: 'test',
        load: () async => throw StateError('offline'),
        openUrl: (_) async => false,
      );
      addTearDown(restored.dispose);
      expect(restored.requiredUpdate, true);
      offline = false;
      fresh = policy(enabled: false);
      await controller.check(force: true);
      expect(controller.showPrompt, false);
    },
  );
  test(
    'no known policy never invents a block; platform mismatch cannot replace cached policy',
    () async {
      final prefs = await SharedPreferences.getInstance();
      final controller = AppUpdateController(
        platform: 'android',
        installedVersion: '1.9.0',
        prefs: prefs,
        cacheNamespace: 'test',
        load: () async => policy(platform: 'ios', minimum: '1.10.0'),
        openUrl: (_) async => false,
      );
      addTearDown(controller.dispose);
      await controller.check();
      expect(controller.showPrompt, false);
    },
  );
  testWidgets(
    'guard blocks login, removes disabled policy, and preserves other anonymous pages',
    (tester) async {
      final prefs = await SharedPreferences.getInstance();
      var fresh = policy(minimum: '1.10.0');
      final controller = AppUpdateController(
        platform: 'android',
        installedVersion: '1.9.0',
        prefs: prefs,
        cacheNamespace: 'test',
        load: () async => fresh,
        openUrl: (_) async => true,
      );
      addTearDown(controller.dispose);
      await tester.pumpWidget(
        ProviderScope(
          overrides: [
            sharedPrefsProvider.overrideWithValue(prefs),
            appUpdateControllerProvider.overrideWith((ref) async => controller),
          ],
          child: MaterialApp(
            navigatorKey: rootNavigatorKey,
            builder: (_, child) => AppUpdateGuard(child: child!),
            home: const Scaffold(body: Text('login')),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsOneWidget);
      fresh = policy(enabled: false);
      await controller.check(force: true);
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsNothing);
      unawaited(
        rootNavigatorKey.currentState!.push(
          MaterialPageRoute<void>(
            builder: (_) => const Scaffold(body: Text('signup')),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await controller.check(force: true);
      await tester.pumpAndSettle();
      expect(find.text('signup'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );
  testWidgets(
    'logout closes account overlays beneath mandatory update without dismissing the requirement',
    (tester) async {
      final prefs = await SharedPreferences.getInstance();
      var fresh = policy(enabled: false);
      final controller = AppUpdateController(
        platform: 'android',
        installedVersion: '1.9.0',
        prefs: prefs,
        cacheNamespace: 'test',
        load: () async => fresh,
        openUrl: (_) async => true,
      );
      addTearDown(controller.dispose);
      final container = ProviderContainer(
        overrides: [
          sharedPrefsProvider.overrideWithValue(prefs),
          appUpdateControllerProvider.overrideWith((ref) async => controller),
        ],
      );
      addTearDown(container.dispose);
      await container
          .read(sessionProvider.notifier)
          .save(
            Session(
              accessToken: 'test-session',
              expiresAt: DateTime.utc(2036),
              user: AuthUser(
                userId: 1,
                userName: 'test',
                displayName: 'test',
                role: 'Manager',
                roleLabelTh: 'ผู้จัดการ',
                shardKey: 'db2',
                canSeeCost: true,
                canCloseShift: true,
              ),
              branchId: 1,
              branchName: 'test',
            ),
          );
      await tester.pumpWidget(
        UncontrolledProviderScope(
          container: container,
          child: MaterialApp(
            navigatorKey: rootNavigatorKey,
            builder: (_, child) => AppUpdateGuard(child: child!),
            home: const Scaffold(body: Text('home')),
          ),
        ),
      );
      await tester.pumpAndSettle();
      unawaited(
        rootNavigatorKey.currentState!.push(
          MaterialPageRoute<void>(
            builder: (_) => const Scaffold(body: Text('account overlay')),
          ),
        ),
      );
      await tester.pumpAndSettle();
      fresh = policy(minimum: '1.10.0');
      await controller.check(force: true);
      await tester.pumpAndSettle();
      await container.read(sessionProvider.notifier).clear();
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsOneWidget);
      expect(find.text('account overlay', skipOffstage: false), findsNothing);
      fresh = policy(enabled: false);
      await controller.check(force: true);
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsNothing);
      expect(find.text('home'), findsOneWidget);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );
  testWidgets(
    'forced dialog blocks Back and barrier, shows Store error, handles large text',
    (tester) async {
      final prefs = await SharedPreferences.getInstance();
      final controller = AppUpdateController(
        platform: 'android',
        installedVersion: '1.9.0',
        prefs: prefs,
        cacheNamespace: 'test',
        load: () async => policy(minimum: '1.10.0'),
        openUrl: (_) async => false,
      );
      addTearDown(controller.dispose);
      await controller.check();
      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                onPressed: () => showDialog<void>(
                  context: context,
                  barrierDismissible: false,
                  builder: (_) => MediaQuery(
                    data: const MediaQueryData(
                      textScaler: TextScaler.linear(2),
                    ),
                    child: AppUpdateDialog(controller: controller),
                  ),
                ),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('open'));
      await tester.pumpAndSettle();
      expect(find.text('ไว้ภายหลัง'), findsNothing);
      await tester.binding.handlePopRoute();
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsOneWidget);
      await tester.tapAt(const Offset(5, 5));
      await tester.pumpAndSettle();
      expect(find.byType(AlertDialog), findsOneWidget);
      await tester.tap(find.text('อัปเดต'));
      await tester.pumpAndSettle();
      expect(
        find.text('เปิด Store ไม่สำเร็จ กรุณาลองอีกครั้ง'),
        findsOneWidget,
      );
      expect(tester.takeException(), isNull);
    },
  );
}
