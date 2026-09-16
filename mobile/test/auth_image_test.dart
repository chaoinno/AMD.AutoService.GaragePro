import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/api_client.dart';
import 'package:garage_pro_service_ops/features/attachments/data/attachment_cache.dart';
import 'package:garage_pro_service_ops/features/attachments/widgets/auth_image.dart';

void main() {
  testWidgets('รูปที่โหลดไม่สำเร็จต้องขึ้นสถานะผิดพลาด ไม่ใช่หมุนค้าง', (tester) async {
    final failing = Provider<AttachmentCache>((ref) => AttachmentCache(
          (_) async => throw ApiException('NETWORK_ERROR', 'เซิร์ฟเวอร์ตอบกลับผิดพลาด (HTTP 500)'),
        ));

    await tester.pumpWidget(ProviderScope(
      child: MaterialApp(
        home: Scaffold(
          body: AuthImage(cacheKey: 'x.png', cache: failing, width: 200, height: 200),
        ),
      ),
    ));

    // เฟรมแรกยังโหลดอยู่ ถือว่าปกติ
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    await tester.pump(const Duration(milliseconds: 50));
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.byType(CircularProgressIndicator), findsNothing,
        reason: 'โหลดล้มเหลวแล้วต้องไม่เหลือ spinner');
    expect(find.textContaining('HTTP 500'), findsOneWidget);
  });

  testWidgets('รูปที่โหลดสำเร็จต้องแสดงรูป', (tester) async {
    // PNG 1x1 โปร่งใส
    final png = Uint8List.fromList([
      0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
      0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
      0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
      0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
      0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
      0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ]);

    final ok = Provider<AttachmentCache>((ref) => AttachmentCache((_) async => png));

    await tester.pumpWidget(ProviderScope(
      child: MaterialApp(
        home: Scaffold(
          body: AuthImage(cacheKey: 'ok.png', cache: ok, width: 200, height: 200),
        ),
      ),
    ));
    await tester.pump(const Duration(milliseconds: 50));
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.byType(Image), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsNothing);
  });
}
