import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/features/approval/queue_page.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:garage_pro_service_ops/models/quotation.dart';

/// การ์ดคิวอนุมัติเคยล้นขวา ("RenderFlex overflowed by 25 pixels on the right") เพราะรหัส
/// QT-{เลขจ๊อบ}-NN ยาวคู่กับชิปสถานะในแถวเดียว — จอแคบต้องตัดข้อความ ไม่ใช่ล้น
void main() {
  testWidgets('การ์ดรหัสยาวไม่ล้นบนจอแคบ 320pt', (tester) async {
    tester.view.physicalSize = const Size(320 * 3, 720 * 3);
    tester.view.devicePixelRatio = 3;
    addTearDown(tester.view.reset);

    final item = QuotationSummary(
      id: 'q1',
      code: 'QT-JB2610070227003-12',
      version: 12,
      status: 'partial',
      statusLabelTh: 'อนุมัติบางรายการ',
      jobNo: 'JB2610070227003',
      customerName: 'คุณสมชาย ใจดีมากเป็นพิเศษ ลูกค้าประจำสาขา',
      vehicleRegistration: '1กข-1234 กรุงเทพมหานคร',
      vehicleModel: 'Toyota Hilux Revo Rocco Double Cab',
      total: 1234567.89,
      ageLabelTh: '3 วัน 14 ชั่วโมง 25 นาที',
    );

    await tester.pumpWidget(
      ProviderScope(
        overrides: [
          sessionProvider.overrideWith(_NoSession.new),
          queueProvider.overrideWith((ref) async => [item]),
        ],
        child: const MaterialApp(home: QueuePage()),
      ),
    );
    await tester.pump();

    expect(find.textContaining('QT-JB2610070227003-12'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });
}

class _NoSession extends SessionNotifier {
  @override
  Session? build() => null;
}
