import 'dart:io';
import 'dart:typed_data';

import 'package:dio/dio.dart';

import 'api_client.dart';
import '../models/attachment.dart';

class AttachmentsApi {
  AttachmentsApi(this._c);
  final ApiClient _c;

  Future<List<Attachment>> list(String jobId, {String? kind}) async {
    final data = await _c.get<List<dynamic>>('/attachments',
        query: {'jobId': jobId, 'kind': ?kind});
    return data.map((e) => Attachment.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<Attachment> upload({
    required File file,
    required String jobId,
    required String kind,
    String? entityId,
  }) async {
    final form = FormData.fromMap({
      'file': await MultipartFile.fromFile(file.path, filename: file.uri.pathSegments.last),
      'jobId': jobId,
      'kind': kind,
      'entityId': ?entityId,
    });

    final data = await _c.unwrap<Map<String, dynamic>>(
      () => _c.dio.post('/attachments', data: form,
          options: Options(contentType: 'multipart/form-data')),
    );
    return Attachment.fromJson(data);
  }

  /// ดาวน์โหลดไฟล์แนบ — endpoint นี้ต้องมี Bearer เสมอ จึงใส่ URL ลง Image.network ตรงๆ ไม่ได้
  /// (เว็บเคยพลาดจุดนี้แล้วรูปพังเงียบๆ ทุกหน้า — ดู AttachmentImage.tsx)
  Future<Uint8List> download(String relativePath) =>
      _c.unwrapBytes('/attachments/file', query: {'path': relativePath});

  Future<Uint8List> vehicleImage(int vehicleId) =>
      _c.unwrapBytes('/vehicles/$vehicleId/image');

  Future<Uint8List> staffImage(int staffId) => _c.unwrapBytes('/staffs/$staffId/image');
}
