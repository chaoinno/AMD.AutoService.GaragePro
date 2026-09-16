import 'dart:typed_data';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/features/attachments/data/attachment_cache.dart';

/// เทสต์ชุดนี้เกิดจากบั๊กจริง: whenComplete(() => map.remove(key)) ทำให้ future รอตัวเอง
/// ผลคือรูปทุกใบในแอปหมุนค้างตลอดกาลโดยไม่มี error ให้เห็น — ห้ามให้หลุดอีก
void main() {
  test('load คืนไบต์ได้จริง ไม่ค้าง', () async {
    final bytes = Uint8List.fromList([1, 2, 3]);
    final cache = AttachmentCache((_) async => bytes);

    final result = await cache.load('a').timeout(const Duration(seconds: 2));
    expect(result, bytes);
  });

  test('load ส่งต่อ error ได้จริง ไม่ค้าง', () async {
    final cache = AttachmentCache((_) async => throw StateError('boom'));

    await expectLater(
      cache.load('a').timeout(const Duration(seconds: 2)),
      throwsA(isA<StateError>()),
    );
  });
}
