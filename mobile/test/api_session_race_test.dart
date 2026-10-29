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

void main() {
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
