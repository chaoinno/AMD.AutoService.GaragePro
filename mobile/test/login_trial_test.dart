import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/features/auth/login_page.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// อู่ทดลองใช้ที่ถูกปิดการใช้งาน: login ได้ TRIAL_EXPIRED — ต้องเห็นข้อความของ server + ช่องทางติดต่อ
/// และต้องไม่มีเซสชันค้างในเครื่อง
void main() {
  testWidgets('TRIAL_EXPIRED ที่ login แสดง messageTh ของ server พร้อม LINE @garagepro', (tester) async {
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();

    final client = ApiClient(readToken: () => null);
    client.dio.httpClientAdapter = _LoginLockedAdapter();

    final container = ProviderContainer(overrides: [
      sharedPrefsProvider.overrideWithValue(prefs),
      apiClientProvider.overrideWithValue(client),
    ]);
    addTearDown(container.dispose);

    await tester.pumpWidget(UncontrolledProviderScope(
      container: container,
      child: const MaterialApp(home: LoginPage()),
    ));

    expect(find.textContaining('SP1042.T01'), findsOneWidget); // คำแนะนำรูปแบบรหัสของอู่ที่สมัครเอง

    await tester.enterText(find.byType(TextField).first, 'owner@example.com');
    await tester.enterText(find.byType(TextField).last, 'secret123');
    final submit = find.widgetWithText(FilledButton, 'เข้าสู่ระบบ');
    await tester.ensureVisible(submit); // จอทดสอบ 800×600 — ปุ่มอยู่ใต้ขอบจอ
    await tester.pump();
    await tester.tap(submit);
    // ปุ่มมี spinner ระหว่างส่ง — pumpAndSettle จะ timeout จึง pump ทีละจังหวะ
    for (var i = 0; i < 5; i++) {
      await tester.pump(const Duration(milliseconds: 50));
    }

    expect(find.textContaining(_lockedMessage), findsOneWidget);
    expect(find.text('LINE @garagepro'), findsOneWidget);
    expect(find.text('คัดลอก ID'), findsOneWidget);
    expect(container.read(sessionProvider), isNull);
  });
}

const _lockedMessage = 'ช่วงทดลองใช้สิ้นสุดแล้วและบัญชีถูกปิดการใช้งาน';

class _LoginLockedAdapter implements HttpClientAdapter {
  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream,
      Future<void>? cancelFuture) async {
    return ResponseBody.fromBytes(
      utf8.encode(jsonEncode({
        'success': false,
        'data': null,
        'error': {
          'code': 'TRIAL_EXPIRED',
          'messageTh': '$_lockedMessage — ติดต่อ LINE @garagepro เพื่อสมัครแพ็กเกจหรือขอเปิดใช้งานอีกครั้ง',
        },
        'traceId': 'trace-login',
      })),
      403,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}
