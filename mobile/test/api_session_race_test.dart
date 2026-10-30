import 'dart:async';
import 'dart:convert';
import 'dart:typed_data';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/api_client.dart';

class DelayedAdapter implements HttpClientAdapter {
  final started = Completer<void>();
  final response = Completer<ResponseBody>();
  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) {
    started.complete();
    return response.future;
  }

  @override
  void close({bool force = false}) {}
}

/// ตอบ 401 AUTH_REQUIRED ทุกคำขอ
class UnauthorizedAdapter implements HttpClientAdapter {
  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    return ResponseBody.fromString(
      jsonEncode({
        'success': false,
        'data': null,
        'error': {'code': 'AUTH_REQUIRED', 'messageTh': 'ข้อความจาก server'},
      }),
      401,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

ApiClient _unauthorizedClient(String? token, List<ApiException> failures) {
  final dio = Dio()..httpClientAdapter = UnauthorizedAdapter();
  return ApiClient(readToken: () => token, dio: dio, onAuthFailure: failures.add);
}

void main() {
  test('401 of a request that carried a token ends the session with the server message', () async {
    final failures = <ApiException>[];
    final client = _unauthorizedClient('current', failures);

    await expectLater(
      client.get<Object?>('/jobs'),
      throwsA(isA<ApiException>().having((e) => e.code, 'code', 'AUTH_REQUIRED')),
    );

    expect(failures.map((e) => e.messageTh), ['ข้อความจาก server']);
  });

  // เซสชันถูกล้างแล้ว client ใหม่ (ไม่มี token) ของหน้าที่ยังเปิดอยู่ยิงคำขอซ้ำ — 401 นี้ไม่ได้บอกเหตุผลอะไร
  // ห้ามสั่งออกจากระบบซ้ำ ไม่งั้นข้อความทั่วไปทับเหตุผลจริงที่เพิ่งแสดง (พบตอนทดสอบปิดพนักงาน 2026-10-30)
  test('401 of a request sent without a token does not end the session again', () async {
    final failures = <ApiException>[];
    final client = _unauthorizedClient(null, failures);

    await expectLater(
      client.get<Object?>('/jobs'),
      throwsA(isA<ApiException>().having((e) => e.code, 'code', 'AUTH_REQUIRED')),
    );

    expect(failures, isEmpty);
  });

  for (final status in [200, 401]) {
    test(
      'late HTTP $status from previous account cannot affect current account',
      () async {
        String? token = 'old';
        var logouts = 0;
        final adapter = DelayedAdapter();
        final dio = Dio();
        dio.httpClientAdapter = adapter;
        final client = ApiClient(
          readToken: () => token,
          dio: dio,
          onAuthFailure: (_) => logouts++,
        );
        final pending = client.get<Object?>('/jobs');
        final assertion = expectLater(
          pending,
          throwsA(
            isA<ApiException>().having(
              (e) => e.code,
              'code',
              'SESSION_CHANGED',
            ),
          ),
        );
        await adapter.started.future;
        token = 'new';
        adapter.response.complete(
          ResponseBody.fromString(
            jsonEncode({
              'success': status == 200,
              'data': [],
              'error': {'code': 'AUTH_REQUIRED', 'messageTh': 'ปิดใช้งานแล้ว'},
            }),
            status,
            headers: {
              Headers.contentTypeHeader: ['application/json'],
            },
          ),
        );
        await assertion;
        expect(logouts, 0);
      },
    );
  }
}
