import 'dart:typed_data';

import 'package:dio/dio.dart';

import '../models/package.dart';
import 'api_client.dart';

/// แพ็กเกจและการแจ้งชำระเงิน (docs/17 §5.1 · §8.7.1)
///
/// route อยู่ใต้ `/selfserve` ที่ `TrialReadOnlyFilter` ยกเว้นไว้ จึงเรียกได้แม้อู่อยู่ช่วงอ่านอย่างเดียว
/// · ตอบทั้งอู่ที่สมัครเอง (sp1) และสาขา GaragePro กลุ่ม Service ใน db2 (`flow: garagepro`)
/// · shard หรือกลุ่มสาขาอื่นได้ 404 `SELFSERVE_NOT_APPLICABLE`
/// · `slipBytes` ใช้กับสลิปของอู่ที่สมัครเองเท่านั้น สลิปของสาขา GaragePro เป็นลิงก์ภายนอก (`PackageSlipInfo.url`)
class PackageApi {
  PackageApi(this._c);
  final ApiClient _c;

  static const notApplicableCode = 'SELFSERVE_NOT_APPLICABLE';

  /// server รับ take ได้ไม่เกิน 50
  static const maxTake = 50;

  Future<PackageOverview> overview() async =>
      PackageOverview.fromJson(await _c.get<Object?>('/selfserve/package'));

  /// ใหม่สุดก่อน
  Future<List<PackagePayment>> payments({int take = 20}) async {
    final data = await _c.get<Object?>('/selfserve/package/payments',
        query: {'take': take.clamp(1, maxTake)});
    return PackagePayment.listFrom(data);
  }

  /// แจ้งชำระพร้อมสลิป (Manager เท่านั้น) · ส่งซ้ำด้วย requestId เดิมได้แถวเดิม
  /// · สาขา GaragePro ไม่ส่ง amount/note (draft ที่ amount เป็น null)
  ///
  /// ส่งไฟล์จากไบต์ในหน่วยความจำ ไม่ใช่จาก path: กดลองใหม่หลังเน็ตหลุดจะได้ไฟล์เดิมทุกไบต์
  /// แม้ไฟล์ชั่วคราวของ image_picker ถูกระบบลบไปแล้ว
  Future<PackagePayment> submit(PackagePaymentDraft draft) async {
    final form = FormData.fromMap({
      ...draft.fields(),
      'slip': MultipartFile.fromBytes(
        draft.slipBytes,
        filename: draft.slipFileName,
        contentType: DioMediaType.parse(draft.slipContentType),
      ),
    });

    final data = await _c.unwrap<Object?>(
      () => _c.dio.post('/selfserve/package/payments',
          data: form, options: Options(contentType: 'multipart/form-data')),
    );
    return _payment(data);
  }

  Future<PackagePayment> cancel(String id) async =>
      _payment(await _c.post<Object?>('/selfserve/package/payments/$id/cancel'));

  /// ไฟล์สลิป (image/jpeg หรือ application/pdf) ต้องมี Bearer จึงใช้ Image.network ตรงๆ ไม่ได้
  Future<Uint8List> slipBytes(String id) =>
      _c.unwrapBytes('/selfserve/package/payments/$id/slip');

  static PackagePayment _payment(Object? data) =>
      PackagePayment.tryParse(data) ??
      (throw ApiException('UNKNOWN', 'เซิร์ฟเวอร์ตอบกลับข้อมูลรายการแจ้งชำระไม่ครบ'));
}
