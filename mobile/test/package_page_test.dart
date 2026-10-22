import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
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

  // ราคาจริง (docs/17 §5.5): 3,999 บาท/เดือนก่อน VAT + VAT 7% · ไม่มีส่วนลด
  final options = [
    {'months': 1, 'subtotal': 3999.00, 'vat': 279.93, 'price': 4278.93, 'previewLastUsableDate': '2026-12-13'},
    {'months': 3, 'subtotal': 11997.00, 'vat': 839.79, 'price': 12836.79, 'previewLastUsableDate': '2027-02-13'},
    {'months': 12, 'subtotal': 47988.00, 'vat': 3359.16, 'price': 51347.16, 'previewLastUsableDate': '2027-11-13'},
  ];

  Map<String, dynamic> pending() => {
        'id': '3f2a9c1b-1111-2222-3333-444455556666',
        'shortId': '3f2a9c1b',
        'status': 'pending',
        'monthCount': 3,
        'amount': 12000.0,
        'expectedAmount': 12836.79,
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
        'vatRate': 0.07,
        'monthOptions': options,
        'paymentAccount': {
          'bankName': 'ธนาคารกสิกรไทย',
          'accountNo': '068-8-63750-3',
          'accountName': 'บจก. อาร์มาดิลโล เทค จำกัด',
        },
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

  final slipImage = (bytes: Uint8List.fromList(List.filled(64, 1)), fileName: 'image_picker_1.jpg');

  Future<_RoutingAdapter> pumpPage(
    WidgetTester tester, {
    required Map<String, (int, Object)> routes,
    String role = 'Manager',
    PickedImage? slip,
    Size size = const Size(900, 4000),
    double textScale = 1,
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
      child: MaterialApp(
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(textScale)),
          child: child!,
        ),
        home: const PackagePage(),
      ),
    ));
    await tester.pumpAndSettle();
    return adapter;
  }

  FilledButton submitButton(WidgetTester tester) => tester.widget<FilledButton>(
      find.descendant(of: find.byType(StickyActionBar), matching: find.byType(FilledButton)));

  String amountField(WidgetTester tester) => tester.widget<TextField>(find.byType(TextField).first).controller!.text;

  testWidgets('การ์ดหัว + ต่ออายุ 3 ขั้น: เลือกระยะเวลาแล้วเติมยอดรวม VAT ให้ ปุ่มบอกขั้นถัดไปที่ขาด', (tester) async {
    final semantics = tester.ensureSemantics();
    await pumpPage(tester, routes: {
      'GET /selfserve/package': (200, envelope(overview())),
      'GET /selfserve/package/payments': (200, envelope(history)),
    });

    expect(find.text('SP1042'), findsOneWidget);
    expect(find.text('อู่ช่างเอก'), findsOneWidget);
    expect(find.text('13 พ.ย. 2026'), findsOneWidget);
    expect(find.text('เหลือ 23 วัน'), findsOneWidget);
    expect(find.text('ต่ออายุใน 3 ขั้น'), findsOneWidget);
    expect(find.text('068-8-63750-3'), findsOneWidget);
    expect(find.text('ธนาคารกสิกรไทย'), findsOneWidget);
    // ทุกการ์ดระยะเวลาแสดงยอดรวม VAT ตัวใหญ่ + ราคาก่อน VAT/VAT
    expect(find.text('12,836.79'), findsOneWidget);
    expect(find.bySemanticsLabel(RegExp(r'ก่อน VAT 11,997\.00 \+ VAT 7% 839\.79')), findsOneWidget);
    // ไม่มีป้ายคุ้มสุด/ยอดนิยม (docs/17 §6.3)
    expect(find.textContaining('คุ้ม'), findsNothing);
    expect(find.text('เลือกระยะเวลาที่ต้องการต่ออายุ (ขั้นที่ 1)'), findsOneWidget);
    expect(find.text('ยังไม่ได้เลือกระยะเวลา'), findsOneWidget);

    await tester.tap(find.text('3 เดือน'));
    await tester.pumpAndSettle();

    expect(amountField(tester), '12,836.79');
    expect(find.text('ตรงกับยอดที่ต้องโอน (รวม VAT)'), findsOneWidget);
    // แถบล่าง: ระยะเวลา + วันที่ใช้ได้ถึง + ยอดรวม VAT
    expect(find.text('3 เดือน · ถึง 13 ก.พ. 2027'), findsOneWidget);
    expect(find.text('12,836.79 บาท'), findsWidgets);
    expect(find.bySemanticsLabel('ขั้นที่ 1 เลือกระยะเวลา เสร็จแล้ว'), findsOneWidget);
    expect(find.text('แนบรูปสลิปการโอน (ขั้นที่ 3)'), findsOneWidget);
    expect(submitButton(tester).onPressed, isNull);

    // เปลี่ยนระยะเวลา ยอดที่ระบบเติมเปลี่ยนตาม
    await tester.tap(find.text('12 เดือน (1 ปี)'));
    await tester.pumpAndSettle();
    expect(amountField(tester), '51,347.16');
    semantics.dispose();
  });

  testWidgets('ยอดที่พิมพ์เองไม่ถูกทับ · ไม่ตรงราคาเตือนแต่ไม่บล็อก · กดใช้ยอดตามแพ็กเกจได้', (tester) async {
    await pumpPage(tester, routes: {
      'GET /selfserve/package': (200, envelope(overview())),
      'GET /selfserve/package/payments': (200, envelope(<Object>[])),
    });

    await tester.tap(find.text('3 เดือน'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField).first, '12000');
    await tester.pump();

    expect(find.textContaining('ยอดไม่ตรงกับราคาแพ็กเกจ (12,836.79 บาท รวม VAT)'), findsOneWidget);
    expect(find.text('ยังไม่มีประวัติการแจ้งชำระ'), findsOneWidget);

    // เปลี่ยนระยะเวลาแล้วยอดที่ผู้ใช้พิมพ์ยังอยู่
    await tester.tap(find.text('1 เดือน'));
    await tester.pumpAndSettle();
    expect(amountField(tester), '12000');

    await tester.tap(find.text('ใช้ยอดตามแพ็กเกจ 4,278.93 บาท'));
    await tester.pump();
    expect(amountField(tester), '4,278.93');
    expect(find.textContaining('ยอดไม่ตรงกับราคาแพ็กเกจ'), findsNothing);
  });

  testWidgets('ปุ่มคัดลอกเลขบัญชีได้ตัวเลขล้วน ยอดโอนไม่มีจุลภาค', (tester) async {
    final copied = <String>[];
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
      if (call.method == 'Clipboard.setData') copied.add((call.arguments as Map)['text'] as String);
      return null;
    });
    addTearDown(() =>
        tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(SystemChannels.platform, null));

    await pumpPage(tester, routes: {
      'GET /selfserve/package': (200, envelope(overview())),
      'GET /selfserve/package/payments': (200, envelope(<Object>[])),
    });
    // ยังไม่เลือกระยะเวลา มีแค่ปุ่มคัดลอกเลขบัญชี
    expect(find.text('คัดลอก'), findsOneWidget);
    await tester.tap(find.text('คัดลอก'));
    await tester.pump();

    await tester.tap(find.text('3 เดือน'));
    await tester.pumpAndSettle();
    expect(find.text('คัดลอก'), findsNWidgets(2));
    await tester.tap(find.text('คัดลอก').last);
    await tester.pump();

    expect(copied, ['0688637503', '12836.79']);
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
    // แจ้งเองไม่ได้ = ไม่มีขั้นตอนให้กด แต่ยังเห็นราคาและบัญชี
    expect(find.text('ต่ออายุใน 3 ขั้น'), findsNothing);
    expect(find.text('ราคาและบัญชีรับเงิน'), findsOneWidget);
    expect(find.text('ราคารวม VAT 7% แล้ว'), findsOneWidget);
    // รายการรอตรวจแสดงครั้งเดียว (ไม่ซ้ำในประวัติ)
    expect(find.text('รอตรวจ'), findsOneWidget);
    expect(find.text('3 เดือน · 12,000.00 บาท'), findsOneWidget);
    expect(find.text('ราคามาตรฐาน 12,836.79 บาท'), findsOneWidget);
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

  testWidgets('อ่านอย่างเดียวแล้ว: การ์ดหัวบอกสถานะด้วยไอคอน + ข้อความ และยังแจ้งชำระได้', (tester) async {
    await pumpPage(tester, routes: {
      'GET /selfserve/package': (
        200,
        envelope({
          ...overview(),
          'plan': 'paid',
          'readOnly': true,
          'daysLeft': -3,
          'lastUsableDate': '2026-10-19',
        }),
      ),
      'GET /selfserve/package/payments': (200, envelope(<Object>[])),
    });

    expect(find.text('แพ็กเกจชำระเงิน · อ่านอย่างเดียว'), findsOneWidget);
    expect(find.text('หมดอายุแล้ว 3 วัน'), findsOneWidget);
    expect(find.text('ตอนนี้ดูข้อมูลได้ แต่แก้ไขไม่ได้'), findsOneWidget);
    expect(find.byType(StickyActionBar), findsOneWidget);
  });

  testWidgets('เน็ตหลุดตอนส่ง: เก็บคำขอเดิมไว้ กดลองใหม่ใช้ requestId เดิม ไม่สร้างคำขอใหม่', (tester) async {
    final semantics = tester.ensureSemantics();
    final adapter = await pumpPage(
      tester,
      slip: slipImage,
      routes: {
        'GET /selfserve/package': (200, envelope(overview())),
        'GET /selfserve/package/payments': (200, envelope(<Object>[])),
        'POST /selfserve/package/payments': (200, envelope(pending())),
      },
    );
    adapter.networkFailures['POST /selfserve/package/payments'] = 1;

    await tester.tap(find.text('3 เดือน'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('แนบรูปสลิป'));
    await tester.pumpAndSettle();
    expect(find.text('เลือกวันเวลาที่โอนตามสลิป'), findsOneWidget);
    await tester.tap(find.text('แตะเพื่อเลือก'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'ยืนยัน'));
    await tester.pumpAndSettle();

    expect(find.bySemanticsLabel('ขั้นที่ 3 แนบสลิปการโอน เสร็จแล้ว'), findsOneWidget);
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
        .map((r) => (
              fields: {for (final f in (r.data as FormData).fields) f.key: f.value},
              fileLength: (r.data as FormData).files.single.value.length,
            ))
        .toList();
    expect(posts, hasLength(2));
    expect(posts[0].fields['requestId'], isNotEmpty);
    expect(posts[1].fields['requestId'], posts[0].fields['requestId']);
    // ยอดที่ระบบเติมให้ส่งเป็นรูปแบบของ server (ไม่มีจุลภาค)
    expect(posts[1].fields['amount'], '12836.79');
    expect(posts[1].fields['monthCount'], '3');
    expect(posts[1].fileLength, posts[0].fileLength);
    expect(find.text('มีคำแจ้งชำระที่ยังไม่ทราบผล'), findsNothing);
    semantics.dispose();
  });

  testWidgets('422 พร้อม field: ข้อความ server ขึ้นใต้ช่องนั้น', (tester) async {
    final adapter = await pumpPage(
      tester,
      slip: slipImage,
      routes: {
        'GET /selfserve/package': (200, envelope(overview())),
        'GET /selfserve/package/payments': (200, envelope(<Object>[])),
        'POST /selfserve/package/payments': (
          422,
          {
            'success': false,
            'error': {'code': 'PACKAGE_PAYMENT_VALIDATION', 'messageTh': 'กรุณาใช้ปี ค.ศ.', 'field': 'paidAt'},
            'traceId': 'trace-422',
          },
        ),
      },
    );

    await tester.tap(find.text('3 เดือน'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('แนบรูปสลิป'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('แตะเพื่อเลือก'));
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'ยืนยัน'));
    await tester.pumpAndSettle();
    await tester.tap(find.descendant(of: find.byType(StickyActionBar), matching: find.byType(FilledButton)));
    await tester.pumpAndSettle();

    expect(adapter.requests.where((r) => r.method == 'POST'), hasLength(1));
    expect(find.text('กรุณาใช้ปี ค.ศ.'), findsOneWidget);
    // ไม่ใช่เน็ตหลุด = ไม่เก็บเป็นคำขอค้าง
    expect(find.text('มีคำแจ้งชำระที่ยังไม่ทราบผล'), findsNothing);
  });

  // จอโทรศัพท์แคบ (360pt) และตัวอักษร 130% ตอนกรอกครบและมีรายการรอตรวจ ต้องไม่มีแถวไหนล้นขอบ (overflow = เทสต์ล้ม)
  for (final scale in [1.0, 1.3]) {
    testWidgets('จอแคบ 360pt ตัวอักษร ${(scale * 100).round()}% ไม่มีส่วนไหนล้น', (tester) async {
      await pumpPage(
        tester,
        size: const Size(360, 4200),
        textScale: scale,
        slip: slipImage,
        routes: {
          'GET /selfserve/package': (200, envelope(overview())),
          'GET /selfserve/package/payments': (200, envelope([pending(), ...history])),
        },
      );
      await tester.tap(find.text('12 เดือน (1 ปี)'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField).first, '12000');
      await tester.tap(find.text('แนบรูปสลิป'));
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
      expect(find.text('เปลี่ยนรูป'), findsOneWidget);
    });

    testWidgets('จอแคบ 360pt ตัวอักษร ${(scale * 100).round()}% รายการรอตรวจ + อ่านอย่างเดียวไม่ล้น', (tester) async {
      await pumpPage(
        tester,
        size: const Size(360, 3600),
        textScale: scale,
        routes: {
          'GET /selfserve/package': (
            200,
            envelope({...overview(canSubmit: false, withPending: true), 'readOnly': true, 'daysLeft': -3}),
          ),
          'GET /selfserve/package/payments': (200, envelope([pending(), ...history])),
        },
      );
      expect(tester.takeException(), isNull);
      expect(find.text('ราคาและบัญชีรับเงิน'), findsOneWidget);
    });
  }

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
