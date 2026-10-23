import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/api_client.dart';
import 'package:garage_pro_service_ops/api/notifications_api.dart';
import 'package:garage_pro_service_ops/app/routes.dart';
import 'package:garage_pro_service_ops/features/notifications/data/notification_providers.dart';
import 'package:garage_pro_service_ops/models/app_notification.dart';

/// อะแดปเตอร์ปลอม — จดคำขอที่ได้รับ แล้วตอบ envelope ตามที่กำหนด
class _RecordingAdapter implements HttpClientAdapter {
  _RecordingAdapter(this.data);
  final Object data;
  final requests = <RequestOptions>[];

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream,
      Future<void>? cancelFuture) async {
    requests.add(options);
    return ResponseBody.fromBytes(
      utf8.encode(jsonEncode({'success': true, 'data': data, 'traceId': 't'})),
      200,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

NotificationsApi _api(_RecordingAdapter adapter) {
  final dio = Dio(BaseOptions(baseUrl: 'http://localhost:5080/api/v1'));
  dio.httpClientAdapter = adapter;
  return NotificationsApi(ApiClient(readToken: () => 'token', dio: dio));
}

/// รูปแบบเดียวกับที่ server ส่งจริง — ฟิลด์ที่เป็น null ไม่อยู่ใน JSON เลย (WhenWritingNull)
Map<String, dynamic> _json({
  String kind = NotificationKind.chatMention,
  String entityType = 'JobChatMessage',
  String? jobId = 'job-1',
  String? linkHint = 'chat',
  String? readAt,
  String? resolvedAt,
}) =>
    {
      'id': 'n-1',
      'kind': kind,
      'titleTh': 'สมชาย กล่าวถึงคุณในแชท',
      'jobId': ?jobId,
      'entityType': entityType,
      'linkHint': ?linkHint,
      'actorName': 'สมชาย',
      'createdAt': '2026-10-13T03:15:42.1234567Z',
      'readAt': ?readAt,
      'resolvedAt': ?resolvedAt,
    };

void main() {
  group('AppNotification.fromJson', () {
    test('อ่านได้แม้ฟิลด์ที่ไม่บังคับถูกตัดทิ้ง และเก็บ createdAt ดิบไว้ทำ cursor', () {
      final n = AppNotification.fromJson(_json(jobId: null, linkHint: null));

      expect(n.jobId, isNull);
      expect(n.bodyTh, isNull);
      expect(n.isUnread, isTrue);
      // 7 หลักทศนิยมต้องอยู่ครบ — DateTime ของ Dart เก็บได้แค่ 6 หลัก ส่งค่าที่ parse แล้วกลับไปจะซ้ำรอยต่อหน้า
      expect(n.createdAtRaw, '2026-10-13T03:15:42.1234567Z');
      expect(n.createdAt.isUtc, isFalse);
    });

    test('อ่านแล้ว / มีคนดำเนินการแล้ว ไม่นับเป็นยังไม่อ่าน', () {
      expect(AppNotification.fromJson(_json(readAt: '2026-10-13T04:00:00Z')).isUnread, isFalse);

      final resolved = AppNotification.fromJson(_json(resolvedAt: '2026-10-13T04:00:00Z'));
      expect(resolved.isResolved, isTrue);
      expect(resolved.isRead, isFalse);
      expect(resolved.isUnread, isFalse);
    });

    test('withReadAt สลับสถานะโดยไม่แตะฟิลด์อื่น', () {
      final n = AppNotification.fromJson(_json());
      final read = n.withReadAt(DateTime(2026, 10, 13));

      expect(read.isRead, isTrue);
      expect(read.withReadAt(null).isUnread, isTrue);
      expect(read.createdAtRaw, n.createdAtRaw);
    });
  });

  group('notificationTarget', () {
    test('แชทเปิดหน้าแชทของจ๊อบ', () {
      expect(notificationTarget(AppNotification.fromJson(_json())), Routes.jobChat('job-1'));
    });

    test('ใบเสนอราคาเปิดหน้าจ๊อบ ไม่ใช่หน้าอนุมัติ (ใบเซ็นไปแล้ว)', () {
      for (final kind in [
        NotificationKind.quotationApproved,
        NotificationKind.quotationPartial,
        NotificationKind.quotationAllRejected,
      ]) {
        final n = AppNotification.fromJson(
            _json(kind: kind, entityType: 'Quotation', linkHint: 'quote'));
        expect(notificationTarget(n), Routes.job('job-1'), reason: kind);
      }
    });

    test('เอกสารจัดซื้อไม่มีหน้าในแอป', () {
      for (final type in ['PR', 'PO']) {
        final n = AppNotification.fromJson(
            _json(kind: NotificationKind.purchasePending, entityType: type, jobId: null, linkHint: null));
        expect(notificationTarget(n), isNull, reason: type);
      }
    });

    test('ผลตรวจการแจ้งชำระแพ็กเกจเปิดหน้าแพ็กเกจ (ไม่มีจ๊อบ)', () {
      for (final kind in [
        NotificationKind.packagePaymentApproved,
        NotificationKind.packagePaymentRejected,
      ]) {
        final n = AppNotification.fromJson(
            _json(kind: kind, entityType: 'PackagePayment', jobId: null, linkHint: 'package'));
        expect(notificationTarget(n), Routes.package, reason: kind);
      }
      // backend ไม่ส่ง linkHint ก็ยังไปถูกจาก entityType
      final noHint = AppNotification.fromJson(_json(
          kind: NotificationKind.packagePaymentApproved,
          entityType: 'PackagePayment',
          jobId: null,
          linkHint: null));
      expect(notificationTarget(noHint), Routes.package);
    });

    test('ไม่มีจ๊อบ = ไม่มีที่หมาย', () {
      expect(notificationTarget(AppNotification.fromJson(_json(jobId: null))), isNull);
    });
  });

  test('ป้ายบนกระดิ่งเกิน 99 แสดง 99+', () {
    expect(unreadBadgeLabel(1), '1');
    expect(unreadBadgeLabel(99), '99');
    expect(unreadBadgeLabel(100), '99+');
  });

  group('NotificationsApi', () {
    test('หน้าถัดไปส่ง beforeAt เป็นค่าดิบตรงตัว', () async {
      final adapter = _RecordingAdapter({'items': [_json()], 'hasMore': true});

      final page = await _api(adapter).list(
        unreadOnly: true,
        beforeAt: '2026-10-13T03:15:42.1234567Z',
        beforeId: 'n-0',
        take: 30,
      );

      final q = adapter.requests.single.queryParameters;
      expect(adapter.requests.single.path, '/notifications');
      expect(q['unreadOnly'], true);
      expect(q['beforeAt'], '2026-10-13T03:15:42.1234567Z');
      expect(q['beforeId'], 'n-0');
      expect(page.items.single.id, 'n-1');
      expect(page.hasMore, isTrue);
    });

    test('ไม่ส่ง unreadOnly/cursor เมื่อไม่ได้ขอ', () async {
      final adapter = _RecordingAdapter({'items': [], 'hasMore': false});

      await _api(adapter).list();

      final q = adapter.requests.single.queryParameters;
      expect(q.containsKey('unreadOnly'), isFalse);
      expect(q.containsKey('beforeAt'), isFalse);
    });

    test('ทำเป็นอ่านแล้ว/ยังไม่อ่าน ใช้ POST/DELETE และคืนจำนวนล่าสุด', () async {
      final adapter = _RecordingAdapter({'unread': 4});
      final api = _api(adapter);

      expect(await api.markRead('n-1'), 4);
      expect(await api.markUnread('n-1'), 4);
      expect(await api.markAllRead(), 4);
      expect(await api.unreadCount(), 4);

      expect(adapter.requests.map((r) => '${r.method} ${r.path}').toList(), [
        'POST /notifications/n-1/read',
        'DELETE /notifications/n-1/read',
        'POST /notifications/read-all',
        'GET /notifications/unread-count',
      ]);
    });
  });
}
