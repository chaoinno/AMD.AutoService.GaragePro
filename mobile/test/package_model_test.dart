import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/api_client.dart';
import 'package:garage_pro_service_ops/api/package_api.dart';
import 'package:garage_pro_service_ops/models/package.dart';

/// รูปร่าง JSON ตามสัญญา docs/17 §5.1 · ฟิลด์ที่เป็น null ไม่อยู่ใน JSON เลย (WhenWritingNull)
Map<String, dynamic> paymentJson({
  String status = 'pending',
  bool manager = true,
  Map<String, dynamic> extra = const {},
}) =>
    {
      'id': '3f2a9c1b-1111-2222-3333-444455556666',
      'shortId': '3f2a9c1b',
      'status': status,
      'monthCount': 3,
      if (manager) 'amount': 1500.00,
      if (manager) 'expectedAmount': 1770.00,
      'paidAt': '2026-10-21T07:30:00Z',
      'submittedAt': '2026-10-21T07:41:12Z',
      'submittedBy': 'SP1042.M01',
      if (manager) 'note': 'โอนจากบัญชีบริษัท',
      if (manager) 'slip': {'contentType': 'image/jpeg', 'sizeBytes': 183422},
      ...extra,
    };

Map<String, dynamic> overviewJson({Map<String, dynamic> extra = const {}}) => {
      'workspaceNo': 'SP1042',
      'plan': 'trial',
      'status': 'active',
      'endsAt': '2026-11-13T17:00:00Z',
      'lastUsableDate': '2026-11-13',
      'daysLeft': 23,
      'readOnly': false,
      'vatRate': 0.07,
      'monthOptions': [
        {
          'months': 1,
          'subtotal': 3999.00,
          'vat': 279.93,
          'price': 4278.93,
          'previewEndsAt': '2026-12-13T17:00:00Z',
          'previewLastUsableDate': '2026-12-13',
        },
        // ยังไม่ตั้งราคา = ไม่มี price
        {'months': 3, 'previewLastUsableDate': '2027-02-13'},
      ],
      'paymentAccount': {
        'bankName': 'กสิกรไทย',
        'accountNo': '123-4-56789-0',
        'accountName': 'บริษัท อาร์มาดิลโล่ เทค จำกัด',
      },
      'canSubmit': true,
      'paidAtMaxPastDays': 60,
      ...extra,
    };

void main() {
  group('PackageOverview.fromJson', () {
    test('อ่านครบทุกฟิลด์', () {
      final o = PackageOverview.fromJson(overviewJson());

      expect(o.workspaceNo, 'SP1042');
      expect(o.plan, 'trial');
      expect(o.status, 'active');
      expect(o.endsAt, DateTime.utc(2026, 11, 13, 17));
      expect(o.endsAt!.isUtc, isTrue);
      expect(o.daysLeft, 23);
      expect(o.canSubmit, isTrue);
      expect(o.monthOptions.map((m) => m.months), [1, 3]);
      expect(o.vatRate, 0.07);
      expect(o.monthOptions.first.subtotal, 3999.0);
      expect(o.monthOptions.first.vat, 279.93);
      expect(o.monthOptions.first.price, 4278.93);
      expect(o.monthOptions.first.hasVatBreakdown, isTrue);
      expect(o.monthOptions.last.hasVatBreakdown, isFalse);
      expect(o.monthOptions.last.price, isNull);
      expect(o.optionFor(3)!.previewLastUsableDate, DateTime(2027, 2, 13));
      expect(o.optionFor(6), isNull);
      expect(o.paymentAccount!.accountNo, '123-4-56789-0');
      expect(o.paymentAccount!.promptPayId, isNull);
      expect(o.pending, isNull);
    });

    test('วันตามปฏิทินไทย (`...Date`) ไม่ถูกเลื่อนโซนเวลา', () {
      final o = PackageOverview.fromJson(overviewJson());
      // ถ้าแปลงผ่าน UTC จะกลายเป็นวันที่ 12 หรือ 14 ขึ้นกับโซนของเครื่อง
      expect(o.lastUsableDate, DateTime(2026, 11, 13));
      expect(o.lastUsableDate!.isUtc, isFalse);
    });

    test('ฟิลด์ที่ server ตัดทิ้งได้ค่าเริ่มต้นที่ปลอดภัย ไม่โยน exception', () {
      final o = PackageOverview.fromJson({'plan': 'paid', 'canSubmit': false});
      expect(o.plan, 'paid');
      expect(o.endsAt, isNull);
      expect(o.lastUsableDate, isNull);
      expect(o.daysLeft, isNull);
      expect(o.monthOptions, isEmpty);
      expect(o.paymentAccount, isNull);
      expect(o.canSubmit, isFalse);
      expect(o.paidAtMaxPastDays, PackageOverview.defaultPaidAtMaxPastDays);
      // ไม่มี vatRate = ราคาไม่คิด VAT แยก
      expect(o.vatRate, isNull);

      expect(PackageOverview.fromJson(null).plan, '');
      expect(PackageOverview.fromJson('oops').canSubmit, isFalse);
    });

    test('ตัวเลือกเดือนที่รูปร่างผิดถูกข้าม · บัญชีที่ว่างทุกช่อง = ยังไม่ตั้ง', () {
      final o = PackageOverview.fromJson(overviewJson(extra: {
        'monthOptions': [
          {'months': 0},
          {'price': 100},
          'x',
          {'months': 6, 'price': '3,300.00'},
        ],
        'paymentAccount': {'bankName': '  '},
      }));
      expect(o.monthOptions.single.months, 6);
      expect(o.monthOptions.single.price, 3300.0);
      expect(o.paymentAccount, isNull);
    });

    test('vatRate ที่ไม่ใช่สัดส่วน (เช่นส่งมาเป็น 7) ถือว่าไม่รู้ ไม่แสดง "VAT 700%"', () {
      expect(PackageOverview.fromJson(overviewJson(extra: {'vatRate': 7})).vatRate, isNull);
      expect(PackageOverview.fromJson(overviewJson(extra: {'vatRate': 0})).vatRate, isNull);
      expect(PackageOverview.fromJson(overviewJson(extra: {'vatRate': '0.07'})).vatRate, 0.07);
    });

    test('ราคาไม่แยก VAT: มีแต่ price · subtotal/vat เป็น null', () {
      final o = PackageOverview.fromJson(overviewJson(extra: {
        'monthOptions': [
          {'months': 1, 'price': 3999.00},
        ],
      })
        ..remove('vatRate'));
      expect(o.vatRate, isNull);
      expect(o.monthOptions.single.price, 3999.0);
      expect(o.monthOptions.single.subtotal, isNull);
      expect(o.monthOptions.single.vat, isNull);
    });

    test('มีรายการรอตรวจและเหตุผลที่แจ้งไม่ได้', () {
      final o = PackageOverview.fromJson(overviewJson(extra: {
        'pending': paymentJson(),
        'canSubmit': false,
        'cannotSubmitCode': 'pending_exists',
        'cannotSubmitReasonTh': 'มีรายการแจ้งชำระรอตรวจอยู่แล้ว',
      }));
      expect(o.pending!.isPending, isTrue);
      expect(o.cannotSubmitCode, PackageCannotSubmit.pendingExists);
      expect(o.cannotSubmitReasonTh, 'มีรายการแจ้งชำระรอตรวจอยู่แล้ว');
    });
  });

  group('PackagePayment.tryParse', () {
    test('Manager เห็นยอดเงิน หมายเหตุ และสลิป', () {
      final p = PackagePayment.tryParse(paymentJson())!;
      expect(p.shortId, '3f2a9c1b');
      expect(p.monthCount, 3);
      expect(p.amount, 1500.0);
      expect(p.expectedAmount, 1770.0);
      expect(p.paidAt, DateTime.utc(2026, 10, 21, 7, 30));
      expect(p.note, 'โอนจากบัญชีบริษัท');
      expect(p.slip!.isImage, isTrue);
      expect(p.slip!.sizeBytes, 183422);
    });

    test('role อื่นไม่มียอดเงิน/หมายเหตุ/สลิปใน JSON = null', () {
      final p = PackagePayment.tryParse(paymentJson(manager: false))!;
      expect(p.amount, isNull);
      expect(p.expectedAmount, isNull);
      expect(p.note, isNull);
      expect(p.slip, isNull);
    });

    test('ปฏิเสธพร้อมเหตุผล และอนุมัติพร้อมวันสุดท้าย', () {
      final rejected = PackagePayment.tryParse(paymentJson(status: 'Rejected', extra: {
        'rejectReason': 'slip_unreadable',
        'rejectReasonTh': 'สลิปไม่ชัด อ่านไม่ออก',
        'rejectNote': 'ส่งรูปใหม่',
      }))!;
      expect(rejected.status, PackagePaymentStatus.rejected);
      expect(rejected.rejectReason, 'slip_unreadable');
      expect(rejected.rejectNote, 'ส่งรูปใหม่');

      final approved = PackagePayment.tryParse(paymentJson(status: 'approved', extra: {
        'periodStartsAt': '2026-11-13T17:00:00Z',
        'periodEndsAt': '2027-02-13T17:00:00Z',
        'periodLastUsableDate': '2027-02-13',
      }))!;
      expect(approved.periodLastUsableDate, DateTime(2027, 2, 13));
      expect(approved.periodEndsAt, DateTime.utc(2027, 2, 13, 17));
    });

    test('เวลาที่ไม่มีโซนถือเป็น UTC · ไม่มี shortId ใช้ 8 ตัวแรกของ id', () {
      final p = PackagePayment.tryParse({
        'id': 'abcdef1234567890',
        'status': 'pending',
        'paidAt': '2026-10-21T07:30:00',
      })!;
      expect(p.paidAt, DateTime.utc(2026, 10, 21, 7, 30));
      expect(p.shortId, 'abcdef12');
    });

    test('ไม่มี id = null · รายการที่รูปร่างผิดถูกข้าม', () {
      expect(PackagePayment.tryParse({'status': 'pending'}), isNull);
      expect(PackagePayment.tryParse('x'), isNull);
      final list = PackagePayment.listFrom([paymentJson(), {'status': 'x'}, 42]);
      expect(list, hasLength(1));
      expect(PackagePayment.listFrom({'items': []}), isEmpty);
    });
  });

  group('PackagePaymentDraft.fields', () {
    final draft = PackagePaymentDraft(
      requestId: 'req-1',
      monthCount: 3,
      amount: 1500,
      paidAt: DateTime(2026, 10, 21, 14, 30),
      note: '  โอนจากบัญชีบริษัท ',
      slipBytes: Uint8List.fromList([1, 2, 3]),
      slipFileName: 'slip.jpg',
      slipContentType: 'image/jpeg',
    );

    test('ยอดเงินเป็น "1500.00" · จำนวนเดือนเป็นข้อความ · หมายเหตุตัดช่องว่าง', () {
      final f = draft.fields();
      expect(f['requestId'], 'req-1');
      expect(f['amount'], '1500.00');
      expect(f['monthCount'], '3');
      expect(f['note'], 'โอนจากบัญชีบริษัท');
      // มี offset เสมอ (server ตีความค่าที่ไม่มี offset เป็นเวลาไทย)
      expect(f['paidAt'], matches(RegExp(r'^2026-10-21T14:30:00[+-]\d{2}:\d{2}$')));
    });

    test('ไม่มีหมายเหตุ = ไม่ส่งฟิลด์ note', () {
      final empty = PackagePaymentDraft(
        requestId: 'req-2',
        monthCount: 1,
        amount: 590.5,
        paidAt: DateTime(2026, 10, 21, 9),
        note: '   ',
        slipBytes: Uint8List(1),
        slipFileName: 's.png',
        slipContentType: 'image/png',
      );
      expect(empty.fields().containsKey('note'), isFalse);
      expect(empty.fields()['amount'], '590.50');
    });

    test('isoWithOffset ใส่ offset ตามโซนที่ให้', () {
      final wall = DateTime(2026, 10, 21, 14, 30, 5);
      expect(isoWithOffset(wall, const Duration(hours: 7)), '2026-10-21T14:30:05+07:00');
      expect(isoWithOffset(wall, Duration.zero), '2026-10-21T14:30:05+00:00');
      expect(isoWithOffset(wall, const Duration(hours: -3, minutes: -30)), '2026-10-21T14:30:05-03:30');
    });
  });

  group('PackageApi', () {
    test('แจ้งชำระส่ง multipart ครบทุกฟิลด์ + ไฟล์ชื่อ slip', () async {
      final adapter = _RecordingAdapter(200, {'success': true, 'data': paymentJson(), 'traceId': 't'});
      final api = _api(adapter);

      final result = await api.submit(PackagePaymentDraft(
        requestId: 'req-1',
        monthCount: 3,
        amount: 1500,
        paidAt: DateTime(2026, 10, 21, 14, 30),
        note: '',
        slipBytes: Uint8List.fromList(List.filled(10, 7)),
        slipFileName: 'slip.jpg',
        slipContentType: 'image/jpeg',
      ));

      final request = adapter.requests.single;
      expect('${request.method} ${request.path}', 'POST /selfserve/package/payments');
      final form = request.data as FormData;
      final fields = {for (final e in form.fields) e.key: e.value};
      expect(fields['requestId'], 'req-1');
      expect(fields['amount'], '1500.00');
      expect(fields['monthCount'], '3');
      expect(fields.containsKey('note'), isFalse);
      final file = form.files.single;
      expect(file.key, 'slip');
      expect(file.value.filename, 'slip.jpg');
      expect(file.value.contentType.toString(), 'image/jpeg');
      expect(file.value.length, 10);
      expect(result.isPending, isTrue);
    });

    test('422 พร้อม field ส่งต่อถึง ApiException.field', () async {
      final adapter = _RecordingAdapter(422, {
        'success': false,
        'error': {
          'code': 'PACKAGE_PAYMENT_VALIDATION',
          'messageTh': 'กรุณาใช้ปี ค.ศ.',
          'field': 'paidAt',
        },
        'traceId': 'trace-9',
      });

      await expectLater(
        _api(adapter).cancel('abc'),
        throwsA(isA<ApiException>()
            .having((e) => e.code, 'code', 'PACKAGE_PAYMENT_VALIDATION')
            .having((e) => e.field, 'field', 'paidAt')
            .having((e) => e.traceId, 'traceId', 'trace-9')),
      );
    });

    test('ภาพรวม/ประวัติ/ยกเลิก ใช้ path ตามสัญญา · take ไม่เกิน 50', () async {
      final adapter = _RecordingAdapter(200, {'success': true, 'data': paymentJson(), 'traceId': 't'});
      final api = _api(adapter);

      await api.cancel('3f2a9c1b-1111');
      adapter.next = {'success': true, 'data': [paymentJson()], 'traceId': 't'};
      final history = await api.payments(take: 500);
      adapter.next = {'success': true, 'data': overviewJson(), 'traceId': 't'};
      final overview = await api.overview();

      expect(adapter.requests.map((r) => '${r.method} ${r.path}').toList(), [
        'POST /selfserve/package/payments/3f2a9c1b-1111/cancel',
        'GET /selfserve/package/payments',
        'GET /selfserve/package',
      ]);
      expect(adapter.requests[1].queryParameters['take'], 50);
      expect(history, hasLength(1));
      expect(overview.workspaceNo, 'SP1042');
    });
  });
}

PackageApi _api(HttpClientAdapter adapter) {
  final dio = Dio(BaseOptions(baseUrl: 'http://localhost:5080/api/v1'));
  dio.httpClientAdapter = adapter;
  return PackageApi(ApiClient(readToken: () => 'token', dio: dio));
}

class _RecordingAdapter implements HttpClientAdapter {
  _RecordingAdapter(this.status, this.next);

  final int status;
  Map<String, dynamic> next;
  final requests = <RequestOptions>[];

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream,
      Future<void>? cancelFuture) async {
    requests.add(options);
    // อ่าน stream ให้หมด ไม่งั้น multipart ค้าง
    if (requestStream != null) await requestStream.drain<void>();
    return ResponseBody.fromString(
      jsonEncode(next),
      status,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}
