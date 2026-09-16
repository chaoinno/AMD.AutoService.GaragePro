import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/api_client.dart';

/// อะแดปเตอร์ปลอม — คืนผลตามที่กำหนดโดยไม่ต้องมี server จริง
class _FakeAdapter implements HttpClientAdapter {
  _FakeAdapter(this.statusCode, this.body, {this.contentType = 'text/html'});
  final int statusCode;
  final String body;
  final String contentType;

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream,
      Future<void>? cancelFuture) async {
    return ResponseBody.fromBytes(
      utf8.encode(body),
      statusCode,
      headers: {
        Headers.contentTypeHeader: [contentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

ApiClient _client(HttpClientAdapter adapter) {
  final dio = Dio(BaseOptions(baseUrl: 'http://localhost:5080/api/v1'));
  dio.httpClientAdapter = adapter;
  return ApiClient(readToken: () => 'token', dio: dio);
}

void main() {
  test('unwrapBytes ต้องโยน ApiException เมื่อ server ตอบ 500 เป็นหน้า HTML ไม่ใช่ envelope', () async {
    final client = _client(_FakeAdapter(500, '<html>FtpAuthenticationException</html>'));

    await expectLater(
      client.unwrapBytes('/attachments/file', query: {'path': 'x.png'}),
      throwsA(isA<ApiException>()
          .having((e) => e.code, 'code', 'NETWORK_ERROR')
          .having((e) => e.messageTh, 'messageTh', contains('500'))),
    );
  });

  test('unwrapBytes ต้องคืนไบต์เมื่อสำเร็จ', () async {
    final client = _client(_FakeAdapter(200, 'PNGDATA', contentType: 'image/png'));

    final bytes = await client.unwrapBytes('/attachments/file', query: {'path': 'x.png'});
    expect(utf8.decode(bytes), 'PNGDATA');
  });

  test('unwrapBytes ต้องโยน ApiException เมื่อ 404 พร้อม envelope ภาษาไทย', () async {
    final client = _client(_FakeAdapter(
      404,
      '{"success":false,"data":null,"error":{"code":"VEHICLE_IMAGE_NOT_FOUND",'
          '"messageTh":"ไม่พบรูปของรถคันนี้"},"traceId":"abc"}',
      contentType: 'application/json',
    ));

    await expectLater(
      client.unwrapBytes('/vehicles/1/image'),
      throwsA(isA<ApiException>()
          .having((e) => e.code, 'code', 'VEHICLE_IMAGE_NOT_FOUND')
          .having((e) => e.traceId, 'traceId', 'abc')),
    );
  });
}
