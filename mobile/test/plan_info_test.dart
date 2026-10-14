import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// แพ็กเกจของอู่ที่สมัครเอง — ทุกฟิลด์ optional เพราะลูกค้าเดิม (legacy) ไม่มีฟิลด์นี้เลย
/// และเซสชันที่บันทึกจากแอปรุ่นก่อนต้องยังโหลดได้ (ไม่งั้นผู้ใช้ทุกคนหลุดจากระบบตอนอัปเดตแอป)
void main() {
  Map<String, dynamic> userJson({Object? plan, Object? workspace}) => {
        'userId': 41,
        'userName': 'SP1042.M01',
        'displayName': 'สมชาย ใจดี',
        'role': 'Manager',
        'roleLabelTh': 'ผู้จัดการสาขา',
        'shardKey': 'sp1',
        'canSeeCost': true,
        'canCloseShift': true,
        'plan': ?plan,
        'workspace': ?workspace,
      };

  final trialJson = {
    'kind': 'trial',
    'trialEndsAt': '2026-11-13T17:00:00Z',
    'daysLeft': 30,
    'readOnly': false,
    'features': <String>[],
    'limits': {'staff': 5, 'catalogItems': 200, 'customers': 200, 'jobs': 100},
  };

  group('PlanInfo.tryParse', () {
    test('อ่านแพ็กเกจทดลองใช้ครบทุกฟิลด์', () {
      final plan = PlanInfo.tryParse(trialJson)!;

      expect(plan.kind, PlanKind.trial);
      expect(plan.trialEndsAt, DateTime.utc(2026, 11, 13, 17));
      expect(plan.daysLeft, 30);
      expect(plan.readOnly, isFalse);
      expect(plan.features, isEmpty);
      expect(plan.limits!.staff, 5);
      expect(plan.limits!.jobs, 100);
      expect(plan.hasFeature(PlanInfo.advancedReports), isFalse);
    });

    test('ไม่มีฟิลด์/รูปร่างผิด = null ไม่โยน exception', () {
      expect(PlanInfo.tryParse(null), isNull);
      expect(PlanInfo.tryParse('trial'), isNull);
      expect(PlanInfo.tryParse(<String, dynamic>{}), isNull);
      expect(PlanInfo.tryParse({'kind': ''}), isNull);
      expect(PlanInfo.tryParse({'kind': 7}), isNull);
    });

    test('ฟิลด์ย่อยขาด/ชนิดผิดได้ค่าเริ่มต้น', () {
      final plan = PlanInfo.tryParse({
        'kind': 'TRIAL',
        'trialEndsAt': 'ไม่ใช่วันที่',
        'daysLeft': '5',
        'readOnly': 'true',
        'features': 'advancedReports',
      })!;

      expect(plan.kind, PlanKind.trial);
      expect(plan.trialEndsAt, isNull);
      expect(plan.daysLeft, isNull);
      expect(plan.readOnly, isFalse);
      expect(plan.features, isEmpty);
      expect(plan.limits, isNull);
    });

    /// [BIZ] server ใหม่กว่าแอปส่งชนิดที่แอปไม่รู้จัก — ต้องไม่ล็อกอะไรและไม่แสดงแถบ (server ตัดสินเอง)
    test('ชนิดที่ไม่รู้จักถือเป็น legacy (เปิดทุกฟีเจอร์)', () {
      final plan = PlanInfo.tryParse({'kind': 'enterprise', 'features': <String>[]})!;

      expect(plan.kind, PlanKind.legacy);
      expect(plan.hasFeature(PlanInfo.advancedReports), isTrue);
      expect(plan.toJson()['kind'], 'enterprise'); // คงค่าดิบไว้
    });

    test('ค่าเวลาที่ไม่มีโซนเวลาคือ UTC', () {
      final plan = PlanInfo.tryParse({'kind': 'trial', 'trialEndsAt': '2026-11-13T17:00:00'})!;
      expect(plan.trialEndsAt, DateTime.utc(2026, 11, 13, 17));
    });

    test('paid/legacy เปิดทุกฟีเจอร์แม้ features ว่าง · trial/suspended เปิดเฉพาะที่ระบุ', () {
      expect(PlanInfo.tryParse({'kind': 'paid'})!.hasFeature('pos'), isTrue);
      expect(PlanInfo.tryParse({'kind': 'legacy'})!.hasFeature('pos'), isTrue);
      expect(
          PlanInfo.tryParse({
            'kind': 'trial',
            'features': ['pos']
          })!
              .hasFeature('pos'),
          isTrue);
      expect(PlanInfo.tryParse({'kind': 'suspended'})!.hasFeature('pos'), isFalse);
    });
  });

  group('WorkspaceInfo / MeResult', () {
    test('อ่าน workspace · ไม่มีเลขอู่ = null', () {
      final ws = WorkspaceInfo.tryParse({'workspaceNo': 'SP1042', 'name': 'อู่ช่างเอก'})!;
      expect(ws.workspaceNo, 'SP1042');
      expect(ws.name, 'อู่ช่างเอก');

      expect(WorkspaceInfo.tryParse({'name': 'ไม่มีเลข'}), isNull);
      expect(WorkspaceInfo.tryParse(null), isNull);
    });

    test('/auth/me อ่าน user.plan ก่อน แล้วค่อย plan ระดับบน', () {
      final fromUser = MeResult.fromJson({
        'user': userJson(plan: trialJson, workspace: {'workspaceNo': 'SP1042', 'name': 'A'}),
        'plan': {'kind': 'paid'},
      });
      expect(fromUser.plan!.kind, PlanKind.trial);
      expect(fromUser.workspace!.workspaceNo, 'SP1042');

      final fromTop = MeResult.fromJson({
        'user': userJson(),
        'plan': {'kind': 'trial', 'readOnly': true},
        'workspace': {'workspaceNo': 'SP1043', 'name': 'B'},
      });
      expect(fromTop.plan!.readOnly, isTrue);
      expect(fromTop.workspace!.workspaceNo, 'SP1043');
    });

    test('ลูกค้าเดิม/คำตอบแปลกได้ "ไม่มีแพ็กเกจ" ไม่ใช่ exception', () {
      expect(MeResult.fromJson({'user': userJson()}).plan, isNull);
      expect(MeResult.fromJson(null).plan, isNull);
      expect(MeResult.fromJson('oops').workspace, isNull);
    });
  });

  group('Session round-trip', () {
    test('แพ็กเกจ/อู่รอดการบันทึกลงเครื่องแล้วโหลดกลับ', () {
      final session = Session(
        accessToken: 'tok',
        expiresAt: DateTime.utc(2036, 1, 1),
        user: AuthUser.fromJson(
            userJson(plan: trialJson, workspace: {'workspaceNo': 'SP1042', 'name': 'อู่ช่างเอก'})),
        branchId: 1001,
        branchName: 'อู่ช่างเอก',
      );

      final restored = Session.fromJson(
          jsonDecode(jsonEncode(session.toJson())) as Map<String, dynamic>);

      expect(restored.user.plan!.kind, PlanKind.trial);
      expect(restored.user.plan!.trialEndsAt, DateTime.utc(2026, 11, 13, 17));
      expect(restored.user.plan!.daysLeft, 30);
      expect(restored.user.plan!.limits!.customers, 200);
      expect(restored.user.workspace!.workspaceNo, 'SP1042');
      expect(jsonEncode(restored.toJson()), jsonEncode(session.toJson()));
    });

    test('เซสชันที่บันทึกจากแอปรุ่นก่อน (ไม่มี plan/workspace) ยังโหลดได้', () {
      final legacy = {
        'accessToken': 'tok',
        'expiresAt': '2036-01-01T00:00:00.000Z',
        'user': {
          'userId': 7,
          'userName': '1234',
          'displayName': 'ช่างบอย',
          'role': 'Technician',
          'roleLabelTh': 'ช่าง',
          'shardKey': 'db2',
          'canSeeCost': false,
          'canCloseShift': false,
          'positionName': null,
          'departmentName': null,
        },
        'branchId': 227,
        'branchName': 'Service Center Demo',
        // key เกินจากรุ่นที่ยังมีขั้นเลือกกะ
        'sessionId': 'x',
        'shiftName': 'กะเช้า',
      };

      final session = Session.fromJson(legacy);
      expect(session.user.plan, isNull);
      expect(session.user.workspace, isNull);
      expect(session.user.toJson().containsKey('plan'), isFalse);
    });

    test('login result อ่าน user.plan มาด้วย', () {
      final result = LoginResult.fromJson({
        'accessToken': 'tok',
        'expiresAt': '2036-01-01T00:00:00Z',
        'user': userJson(plan: {...trialJson, 'readOnly': true}),
        'branchId': 1001,
        'branchName': 'อู่ช่างเอก',
      });
      expect(result.toSession().user.plan!.readOnly, isTrue);
    });
  });

  group('ApiException ของแพ็กเกจ', () {
    test('getter ตามรหัส และไม่ใช่เหตุออกจากระบบ', () {
      final expired = ApiException('TRIAL_EXPIRED', 'ช่วงทดลองใช้สิ้นสุดแล้ว');
      final locked = ApiException('PLAN_FEATURE_LOCKED', 'ฟีเจอร์นี้เปิดในแพ็กเกจเต็ม');
      final limit = ApiException('PLAN_LIMIT_REACHED', 'ครบจำนวนแล้ว');

      expect(expired.isTrialExpired, isTrue);
      expect(locked.isPlanLocked, isTrue);
      expect(limit.isPlanLimit, isTrue);
      for (final e in [expired, locked, limit]) {
        expect(e.isUnauthorized, isFalse);
        expect(e.requiresShift, isFalse);
      }
      expect(expired.isPlanLocked || expired.isPlanLimit, isFalse);
    });
  });

  group('SessionNotifier + แพ็กเกจ', () {
    late ProviderContainer container;
    late SharedPreferences prefs;

    Session trialSession() => Session(
          accessToken: 'tok-1',
          expiresAt: DateTime.utc(2036, 1, 1),
          user: AuthUser.fromJson(userJson(plan: trialJson)),
          branchId: 1001,
          branchName: 'อู่ช่างเอก',
        );

    setUp(() async {
      SharedPreferences.setMockInitialValues({});
      prefs = await SharedPreferences.getInstance();
      container = ProviderContainer(overrides: [sharedPrefsProvider.overrideWithValue(prefs)]);
      await container.read(sessionProvider.notifier).save(trialSession());
    });

    tearDown(() => container.dispose());

    test('อัปเดตแพ็กเกจจาก /auth/me แล้วบันทึกลงเครื่องด้วย', () async {
      final notifier = container.read(sessionProvider.notifier);
      await notifier.updatePlan(PlanInfo.tryParse({...trialJson, 'readOnly': true}),
          const WorkspaceInfo(workspaceNo: 'SP1042', name: 'อู่ช่างเอก'),
          expectedToken: 'tok-1');

      expect(container.read(sessionProvider)!.user.plan!.readOnly, isTrue);
      final stored = jsonDecode(prefs.getString('garagepro.session')!) as Map<String, dynamic>;
      expect(Session.fromJson(stored).user.plan!.readOnly, isTrue);
      expect(Session.fromJson(stored).user.workspace!.workspaceNo, 'SP1042');
    });

    test('คำตอบที่มาช้าหลังสลับบัญชีไม่เขียนทับเซสชันใหม่', () async {
      await container.read(sessionProvider.notifier).updatePlan(
          PlanInfo.tryParse({...trialJson, 'readOnly': true}), null,
          expectedToken: 'tok-ของคนก่อน');

      expect(container.read(sessionProvider)!.user.plan!.readOnly, isFalse);
    });

    test('ค่าเดิมทุกตัวไม่แจ้ง listener', () async {
      var notified = 0;
      container.listen(sessionProvider, (_, _) => notified++);

      await container
          .read(sessionProvider.notifier)
          .updatePlan(PlanInfo.tryParse(trialJson), null, expectedToken: 'tok-1');

      expect(notified, 0);
    });

    /// คำขอเขียนถูกปฏิเสธด้วย TRIAL_EXPIRED → แถบเปลี่ยนเป็นอ่านอย่างเดียวทันที และ**ไม่ออกจากระบบ**
    test('TRIAL_EXPIRED จากคำขอเขียนทำให้แพ็กเกจเป็นอ่านอย่างเดียวโดยไม่ล้างเซสชัน', () async {
      final client = container.read(apiClientProvider);
      client.dio.httpClientAdapter = _ErrorAdapter(403, 'TRIAL_EXPIRED');

      await expectLater(
        client.post<Object?>('/jobs', body: {}),
        throwsA(isA<ApiException>().having((e) => e.isTrialExpired, 'isTrialExpired', isTrue)),
      );
      await Future<void>.delayed(Duration.zero);

      final session = container.read(sessionProvider);
      expect(session, isNotNull);
      expect(session!.user.plan!.readOnly, isTrue);
    });

    test('PLAN_FEATURE_LOCKED ไม่เปลี่ยนแพ็กเกจและไม่ออกจากระบบ', () async {
      final client = container.read(apiClientProvider);
      client.dio.httpClientAdapter = _ErrorAdapter(403, 'PLAN_FEATURE_LOCKED');

      await expectLater(client.get<Object?>('/reports/stock'), throwsA(isA<ApiException>()));
      await Future<void>.delayed(Duration.zero);

      expect(container.read(sessionProvider)!.user.plan!.readOnly, isFalse);
    });
  });
}

/// ตอบ error envelope ตามสถานะที่กำหนด
class _ErrorAdapter implements HttpClientAdapter {
  _ErrorAdapter(this.status, this.code);
  final int status;
  final String code;

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream,
      Future<void>? cancelFuture) async {
    return ResponseBody.fromBytes(
      utf8.encode(jsonEncode({
        'success': false,
        'data': null,
        'error': {'code': code, 'messageTh': 'ข้อความจาก server'},
        'traceId': 't-1',
      })),
      status,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}
