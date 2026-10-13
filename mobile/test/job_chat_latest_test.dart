import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/api_client.dart';
import 'package:garage_pro_service_ops/api/job_chat_api.dart';

/// ตอบข้อความล่าสุดให้ทุก jobId ที่ขอมา — และปฏิเสธถ้าเกินเพดานเหมือน server จริง
class _LatestAdapter implements HttpClientAdapter {
  final batchSizes = <int>[];

  @override
  Future<ResponseBody> fetch(RequestOptions options, Stream<Uint8List>? requestStream,
      Future<void>? cancelFuture) async {
    final ids = (options.queryParameters['jobIds'] as List).cast<String>();
    batchSizes.add(ids.length);

    final envelope = ids.length > JobChatApi.maxLatestJobIds
        ? {
            'success': false,
            'error': {'code': 'CHAT_VALIDATION', 'messageTh': 'เกินเพดาน'},
          }
        : {
            'success': true,
            'data': [
              for (final id in ids) {'jobId': id, 'messageId': 'm-$id'},
            ],
          };
    return ResponseBody.fromBytes(utf8.encode(jsonEncode(envelope)), 200, headers: {
      Headers.contentTypeHeader: ['application/json'],
    });
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  test('jobId เกิน 100 ถูกแบ่งยิงเป็นชุด แล้วรวมผลครบทุกจ๊อบ', () async {
    final adapter = _LatestAdapter();
    final dio = Dio(BaseOptions(baseUrl: 'http://localhost:5080/api/v1'))..httpClientAdapter = adapter;
    final api = JobChatApi(ApiClient(readToken: () => 'token', dio: dio));

    final ids = [for (var i = 0; i < 230; i++) 'job-$i'];
    final latest = await api.latestPerJob(ids);

    expect(adapter.batchSizes, [100, 100, 30]);
    expect(latest.length, 230);
    expect(latest['job-229'], 'm-job-229');
  });

  test('ไม่มีจ๊อบ ไม่ยิงคำขอ', () async {
    final adapter = _LatestAdapter();
    final dio = Dio(BaseOptions(baseUrl: 'http://localhost:5080/api/v1'))..httpClientAdapter = adapter;

    expect(await JobChatApi(ApiClient(readToken: () => 'token', dio: dio)).latestPerJob([]), isEmpty);
    expect(adapter.batchSizes, isEmpty);
  });
}
