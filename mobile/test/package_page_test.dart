import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/features/attachments/photo_upload.dart' show PickedImage;
import 'package:garage_pro_service_ops/features/package/package_page.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:garage_pro_service_ops/widgets/common.dart';
import 'package:intl/date_symbol_data_local.dart';

/// หน้าแพ็กเกจกับ API ปลอม (backend ยังไม่มี endpoint จริงตอนเขียน) ยึดรูปร่าง JSON ตาม docs/17 §5.1
void main() {
  setUpAll(() => initializeDateFormatting('th'));

  Map<String, dynamic> envelope(Object? data) => {'success': true, 'data': data, 'traceId': 't-1'};

  final options = [
    {'months': 1, 'price': 590.0, 'previewLastUsableDate': '2026-12-13'},
    {'months': 3, 'price': 1770.0, 'previewLastUsableDate': '2027-02-13'},
  ];

  Map<String, dynamic> pending() => {
        'id': '3f2a9c1b-1111-2222-3333-444455556666',
        'shortId': '3f2a9c1b',
        'status': 'pending',
        'monthCount': 3,
        'amount': 1500.0,
        'expectedAmount': 1770.0,
        'paidAt': '2026-10-21T07:30:00Z',
        'submittedAt': '2026-10-21T07:41:12Z',
        'submittedBy': 'SP1042.M01',
        'slip': {'contentType': 'image/jpeg', 'sizeBytes': 183422},
      };

  Map<String, dynamic> overview({bool canSubmit = true, bool withPending = false}) => {
        'workspaceNo': 'SP1042',
        'plan': 'trial',
        'status': 'active',
        'endsAt': '2026-11-13T17:00:00Z',
        'lastUsableDate': '2026-11-13',
        'daysLeft': 23,
        'readOnly': false,
        'monthOptions': options,
        'paymentAccount': {'bankName': 'กสิกรไทย', 'accountNo': '123-4-56789-0', 'accountName': 'บริษัทตัวอย่าง'},
        'canSubmit': canSubmit,
        if (!canSubmit) 'cannotSubmitCode': 'pending_exists',
        if (!canSubmit) 'cannotSubmitReasonTh': 'มีรายการแจ้งชำระรอตรวจอยู่แล้ว ทีมงานจะตรวจภายใน 1 วันทำการ',
        if (withPending) 'pending': pending(),
        'paidAtMaxPastDays': 60,
      };

  final history = [
    {
      'id': 'aaaaaaaa-0000',
      'status': 'approved',
      'monthCount': 1,
      'paidAt': '2026-09-01T03:00:00Z',
      'periodLastUsableDate': '2026-10-13',
    },
    {
      'id': 'bbbbbbbb-0000',
      'status': 'rejected',
      'monthCount': 1,
      'rejectReason': 'not_received',
      'rejectReasonTh': 'ไม่พบยอดเงินเข้าบัญชี',
      'rejectNote': 'โอนเข้าบัญชีผิด',
    },
  ];

  Future<_RoutingAdapter> pumpPage(
    WidgetTester tester, {
    required Map<String, (int, Object)> routes,
    String role = 'Manager',
    PickedImage? slip,
    Size size = const Size(900, 4000),
  }) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    final adapter = _RoutingAdapter(routes);
    final dio = Dio(BaseOptions(baseUrl: 'http://localhost:5080/api/v1'));
    dio.httpClientAdapter = adapter;
    final client = ApiClient(readToken: () => 'tok', dio: dio);

    await tester.pumpWidget(ProviderScope(
      overrides: [
        sessionProvider.overrideWith(() => _FakeSession(role)),
        apiClientProvider.overrideWithValue(client),
        slipPickerProvider.overrideWithValue((_) async => slip),
      ],
      child: const MaterialApp(home: PackagePage()),
    ));
    await tester.pumpAndSettle();
    return adapter;
  }

  testWidgets('แจ้งชำระได้: เลือกเดือนแล้วเห็นพรีวิววันหมด ปุ่มบอกขั้นถัดไปที่ต้องทำ', (tester) async {
    await pumpPage(tester, routes: {
      'GET /selfserve/package': (200, envelope(overview())),
      'GET /selfserve/package/payments': (200, envelope(history)),
    });

    expect(find.text('SP1042'), findsOneWidget);
    expect(find.text('13 พ.ย. 2026'), findsOneWidget);
    expect(find.text('เหลือ 23 วัน'), findsOneWidget);
    expect(find.text('123-4-56789-0'), findsOneWidget);
    expect(find.text('เลือกจำนวนเดือนที่ต้องการต่ออายุ'), findsOneWidget);

    await tester.tap(find.widgetWithText(ChoiceChip, '3 เดือน'));
    await tester.pump();
    expect(find.text('ถ้าอนุมัติวันนี้ ใช้งานได้ถึง 13 ก.พ. 2027'), findsOneWidget);
    expect(find.text('กรอกยอดเงินที่โอน'), findsOneWidget);

    await tester.tap(find.text('กรอกยอดตามราคา 1,770.00 บาท'));
    await tester.pump();
    expect(find.text('เลือกวันเวลาที่โอนตามสลิป'), findsOneWidget);

    // ปุ่มหลักยังกดไม่ได้จนกว่าจะครบ
    final submit = tester.widget<FilledButton>(
        find.descendant(of: find.byType(StickyActionBar), matching: find.byType(FilledButton)));
    expect(submit.onPressed, isNull);
  });

  testWidgets('ยอดไม่ตรงราคาเตือนแต่ไม่บล็อก', (tester) async {
    await pumpPage(tester, routes: {
      'GET /selfserve/package': (200, envelope(overview())),
      'GET /selfserve/package/payments': (200, envelope(<Object>[])),
    });

    await tester.tap(find.widgetWithText(ChoiceChip, '3 เดือน'));
    await tester.pump();
    await tester.enterText(find.byType(TextField).first, '1500');
    await tester.pump();

    expect(find.textContaining('ยอดไม่ตรงกับราคามาตรฐาน'), findsOneWidget);
    expect(find.text('เลือกวันเวลาที่โอนตามสลิป'), findsOneWidget);
    expect(find.text('ยังไม่มีประวัติการแจ้งชำระ'), findsOneWidget);
  });

  testWidgets('มีรายการรอตรวจ: แสดงเหตุผลจาก server ไม่มีปุ่มแจ้งชำระ · Manager ยกเลิกได้', (tester) async {
    final adapter = await pumpPage(tester, routes: {
      'GET /selfserve/package': (200, envelope(overview(canSubmit: false, withPending: true))),
      'GET /selfserve/package/payments': (200, envelope([pending(), ...history])),
      'POST /selfserve/package/payments/3f2a9c1b-1111-2222-3333-444455556666/cancel':
          (200, envelope({...pending(), 'status': 'cancelled'})),
    });

    expect(find.text('มีรายการแจ้งชำระรอตรวจอยู่แล้ว ทีมงานจะตรวจภายใน 1 วันทำการ'), findsOneWidget);
    expect(find.byType(StickyActionBar), findsNothing);
    // รายการรอตรวจแสดงครั้งเดียว (ไม่ซ้ำในประวัติ)
    expect(find.text('รอตรวจ'), findsOneWidget);
    expect(find.text('3 เดือน · 1,500.00 บาท'), findsOneWidget);
    expect(find.text('ราคามาตรฐาน 1,770.00 บาท'), findsOneWidget);
    expect(find.text('อนุมัติแล้ว ใช้งานได้ถึง 13 ต.ค. 2026'), findsOneWidget);
    expect(find.text('ปฏิเสธ: ไม่พบยอดเงินเข้าบัญชี'), findsOneWidget);
    expect(find.text('หมายเหตุจากทีมงาน: โอนเข้าบัญชีผิด'), findsOneWidget);
    expect(find.text('ดูสลิป'), findsOneWidget);

    await tester.tap(find.text('ยกเลิกรายการ'));
    await tester.pumpAndSettle();
    expect(find.text('ยกเลิกรายการแจ้งชำระ?'), findsOneWidget);
    await tester.tap(find.widgetWithText(TextButton, 'ยกเลิกรายการ').last);
    await tester.pumpAndSettle();

    expect(
      adapter.requests.map((r) => '${r.method} ${r.path}'),
      contains('POST /selfserve/package/payments/3f2a9c1b-1111-2222-3333-444455556666/cancel'),
    );
  });

  testWidgets('role อื่นเห็นสถานะ แต่ไม่มีปุ่มยกเลิก', (tester) async {
    final noAmount = Map<String, dynamic>.from(pending())
      ..remove('amount')
      ..remove('expectedAmount')
      ..remove('slip');
    await pumpPage(tester, role: 'Technician', routes: {
      'GET /selfserve/package': (
        200,
        envelope({...overview(canSubmit: false, withPending: true), 'pending': noAmount}),
      ),
      'GET /selfserve/package/payments': (200, envelope([noAmount])),
    });

    expect(find.text('รอตรวจ'), findsOneWidget);
    expect(find.text('3 เดือน'), findsWidgets);
    expect(find.text('ยกเลิกรายการ'), findsNothing);
    expect(find.text('ดูสลิป'), findsNothing);
  });

  testWidgets('เน็ตหลุดตอนส่ง: เก็บคำขอเดิมไว้ กดลองใหม่ใช้ requestId เดิม ไม่สร้างคำขอใหม่', (tester) async {
    final adapter = await pumpPage(
      tester,
      slip: (bytes: Uint8List.fromList(List.filled(64, 1)), fileName: 'image_picker_1.jpg'),
      routes: {
        'GET /selfserve/package': (200, envelope(overview())),
        'GET /selfserve/package/payments': (200, envelope(<Object>[])),
        'POST /selfserve/package/payments': (200, envelope(pending())),
      },
    );
    adapter.networkFailures['POST /selfserve/package/payments'] = 1;

    await tester.tap(find.widgetWithText(ChoiceChip, '3 เดือน'));
    await tester.pump();
    await tester.tap(find.text('กรอกยอดตามราคา 1,770.00 บาท'));
    await tester.pump();
    await tester.tap(find.text('แตะเพื่อเลือก'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'ยืนยัน'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('แนบรูปสลิป'));
    await tester.pumpAndSettle();

    final submit = find.descendant(of: find.byType(StickyActionBar), matching: find.byType(FilledButton));
    expect(tester.widget<FilledButton>(submit).onPressed, isNotNull);
    await tester.tap(submit);
    await tester.pumpAndSettle();

    expect(find.text('มีคำแจ้งชำระที่ยังไม่ทราบผล'), findsOneWidget);
    // ระหว่างนี้สร้างคำขอใหม่ไม่ได้ ต้องลองใหม่หรือทิ้งก่อน
    expect(find.textContaining('มีคำขอที่ยังไม่ทราบผล'), findsOneWidget);

    await tester.tap(find.widgetWithText(FilledButton, 'ลองใหม่'));
    await tester.pumpAndSettle();

    final posts = adapter.requests
        .where((r) => r.method == 'POST' && r.path == '/selfserve/package/payments')
        .map((r) => {for (final f in (r.data as FormData).fields) f.key: f.value})
        .toList();
    expect(posts, hasLength(2));
    expect(posts[0]['requestId'], isNotEmpty);
    expect(posts[1]['requestId'], posts[0]['requestId']);
    expect(posts[1]['amount'], '1770.00');
    expect(posts[1]['monthCount'], '3');
    expect(find.text('มีคำแจ้งชำระที่ยังไม่ทราบผล'), findsNothing);
  });

  // จอโทรศัพท์แคบ (360pt) ตอนกรอกครบและมีรายการรอตรวจ ต้องไม่มีแถวไหนล้นขอบ (overflow = เทสต์ล้ม)
  testWidgets('จอแคบ 360pt ไม่มีส่วนไหนล้น', (tester) async {
    await pumpPage(
      tester,
      size: const Size(360, 3000),
      slip: (bytes: Uint8List.fromList(List.filled(64, 1)), fileName: 'image_picker_1.jpg'),
      routes: {
        'GET /selfserve/package': (200, envelope(overview())),
        'GET /selfserve/package/payments': (200, envelope([pending(), ...history])),
      },
    );
    await tester.tap(find.widgetWithText(ChoiceChip, '3 เดือน'));
    await tester.pump();
    await tester.enterText(find.byType(TextField).first, '1500');
    await tester.tap(find.text('แนบรูปสลิป'));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(find.text('เปลี่ยนรูป'), findsOneWidget);
  });

  testWidgets('ไม่ใช่อู่ที่สมัครเอง (404 SELFSERVE_NOT_APPLICABLE) แสดงเหตุผลพร้อมรหัสอ้างอิง', (tester) async {
    await pumpPage(tester, routes: {
      'GET /selfserve/package': (
        404,
        {
          'success': false,
          'error': {'code': 'SELFSERVE_NOT_APPLICABLE', 'messageTh': 'ใช้ได้เฉพาะอู่ที่สมัครเอง'},
          'traceId': 'trace-404',
        },
      ),
    });

    expect(find.text('หน้านี้สำหรับอู่ที่สมัครใช้ ServicePro เอง'), findsOneWidget);
    expect(find.text('ใช้ได้เฉพาะอู่ที่สมัครเอง'), findsOneWidget);
    expect(find.text('รหัสอ้างอิง trace-404'), findsOneWidget);
  });
}

class _RoutingAdapter implements HttpClientAdapter {
  _RoutingAdapter(this.routes);

  final Map<String, (int, Object)> routes;
  final requests = <RequestOptions>[];

  /// จำนวนครั้งที่ path นี้จะล้มแบบเน็ตหลุด (ไม่รู้ผล) ก่อนตอบตามปกติ
  final networkFailures = <String, int>{};

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream,
      Future<void>? cancelFuture) async {
    requests.add(options);
    if (requestStream != null) await requestStream.drain<void>();
    final key = '${options.method} ${options.path}';
    final failures = networkFailures[key] ?? 0;
    if (failures > 0) {
      networkFailures[key] = failures - 1;
      throw DioException(requestOptions: options, type: DioExceptionType.connectionError);
    }
    final route = routes[key];
    // path ที่ไม่ได้กำหนด (เช่น /auth/me) = 404 แบบไม่มี envelope เหมือน endpoint ที่ยังไม่ deploy
    if (route == null) return ResponseBody.fromString('not found', 404);
    return ResponseBody.fromString(
      jsonEncode(route.$2),
      route.$1,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

class _FakeSession extends SessionNotifier {
  _FakeSession(this.role);
  final String role;

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
          plan: const PlanInfo(kindName: 'trial', daysLeft: 23),
          workspace: const WorkspaceInfo(workspaceNo: 'SP1042', name: 'อู่ช่างเอก'),
        ),
        branchId: 1001,
        branchName: 'อู่ช่างเอก',
      );
}
