import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/api/handover_api.dart';
import 'package:garage_pro_service_ops/features/handover/handover_service_info.dart';
import 'package:garage_pro_service_ops/models/handover.dart';
import 'package:garage_pro_service_ops/models/service_reminder.dart';
import 'package:intl/date_symbol_data_local.dart';

const _templates = [
  ServiceReminderTemplate(id: 't-oil', name: 'น้ำมันเครื่อง', defaultMileageInterval: 10000, defaultMonths: 6),
  ServiceReminderTemplate(id: 't-air', name: 'กรองอากาศ', defaultMileageInterval: 20000, defaultMonths: 12),
];

class _FakeHandoverApi implements HandoverApi {
  Map<String, dynamic>? saved;

  @override
  Future<Handover> saveServiceInfo(String jobId,
      {required int mileageAtHandover, required List<Map<String, dynamic>> items}) async {
    saved = {'jobId': jobId, 'mileageAtHandover': mileageAtHandover, 'items': items};
    return _handover();
  }

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

Handover _handover({List<HandoverServiceItem> items = const [], int? mileageAtHandover, bool isLocked = false}) => Handover(
      id: 'h', jobId: 'j', isLocked: isLocked, receiptIssued: true, items: const [],
      mileageAtIntake: 40000, mileageAtHandover: mileageAtHandover, serviceItems: items,
    );

void main() {
  setUpAll(() => initializeDateFormatting('th'));

  Future<_FakeHandoverApi> pump(WidgetTester tester, Handover handover) async {
    tester.view.physicalSize = const Size(900, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);
    final api = _FakeHandoverApi();
    await tester.pumpWidget(ProviderScope(
      overrides: [
        handoverApiProvider.overrideWithValue(api),
        serviceReminderTemplatesProvider.overrideWith((ref) async => _templates),
      ],
      child: MaterialApp(
        home: Scaffold(
          body: SingleChildScrollView(
            child: HandoverServiceInfo(jobId: 'j', handover: handover, onSaved: () {}),
          ),
        ),
      ),
    ));
    await tester.pump();
    return api;
  }

  testWidgets('with no items the save button is disabled and says an item is needed', (tester) async {
    await pump(tester, _handover());

    await tester.enterText(find.widgetWithText(TextField, 'เลขไมล์ตอนส่งมอบ (กม.) *'), '45000');
    await tester.pump();

    expect(find.text('เพิ่มรายการนัดบริการอย่างน้อย 1 รายการ'), findsOneWidget);
    final save = tester.widget<FilledButton>(find.ancestor(of: find.text('บันทึกเลขไมล์และนัดครั้งถัดไป'), matching: find.byWidgetPredicate((widget) => widget is FilledButton)));
    expect(save.onPressed, isNull);
  });

  testWidgets('picking a template adds a row whose mileage follows the handover mileage, then saves the new body', (tester) async {
    final api = await pump(tester, _handover());

    await tester.enterText(find.widgetWithText(TextField, 'เลขไมล์ตอนส่งมอบ (กม.) *'), '45000');
    await tester.pump();
    await tester.tap(find.text('เพิ่มจากรายการตั้งต้น'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('น้ำมันเครื่อง'));
    await tester.pumpAndSettle();

    // ไมล์นัด = ไมล์ส่งมอบ + 10,000 และเดือน 6 มาจากรายการตั้งต้น
    expect(find.widgetWithText(TextField, '55000'), findsOneWidget);
    expect(find.widgetWithText(TextField, '6'), findsOneWidget);

    // เปลี่ยนไมล์ส่งมอบ → แถวที่ยังไม่ถูกแก้เองตามไปด้วย
    await tester.enterText(find.widgetWithText(TextField, '45000'), '46000');
    await tester.pump();
    expect(find.widgetWithText(TextField, '56000'), findsOneWidget);

    // รายการที่ใช้แล้วไม่ขึ้นซ้ำใน sheet
    await tester.tap(find.text('เพิ่มจากรายการตั้งต้น'));
    await tester.pumpAndSettle();
    expect(find.text('กรองอากาศ'), findsOneWidget);
    expect(find.text('น้ำมันเครื่อง'), findsOneWidget); // เหลือเฉพาะชื่อในการ์ดแถว ไม่ซ้ำใน sheet
    await tester.tapAt(const Offset(10, 10)); // ปิด sheet
    await tester.pumpAndSettle();

    await tester.tap(find.ancestor(of: find.text('บันทึกเลขไมล์และนัดครั้งถัดไป'), matching: find.byWidgetPredicate((widget) => widget is FilledButton)));
    await tester.pumpAndSettle();

    expect(api.saved, {
      'jobId': 'j',
      'mileageAtHandover': 46000,
      'items': [
        {'templateId': 't-oil', 'name': 'น้ำมันเครื่อง', 'nextServiceMileage': 56000, 'nextServiceMonths': 6},
      ],
    });
  });

  testWidgets('a custom row needs a name and rows can be removed', (tester) async {
    await pump(tester, _handover());
    await tester.enterText(find.widgetWithText(TextField, 'เลขไมล์ตอนส่งมอบ (กม.) *'), '45000');
    await tester.pump();

    await tester.tap(find.text('เพิ่มรายการเอง'));
    await tester.pump();
    expect(find.text('กรอกชื่อรายการ'), findsWidgets);
    expect(tester.widget<FilledButton>(find.ancestor(of: find.text('บันทึกเลขไมล์และนัดครั้งถัดไป'), matching: find.byWidgetPredicate((widget) => widget is FilledButton))).onPressed, isNull);

    await tester.tap(find.byIcon(Icons.delete_outline));
    await tester.pump();
    expect(find.text('กรอกชื่อรายการ'), findsNothing);
    expect(find.text('เพิ่มรายการนัดบริการอย่างน้อย 1 รายการ'), findsOneWidget);
  });

  testWidgets('once handed over the saved items are shown read-only', (tester) async {
    await pump(
      tester,
      _handover(isLocked: true, mileageAtHandover: 45000, items: [
        HandoverServiceItem(
            id: 'a', name: 'น้ำมันเครื่อง', nextServiceMileage: 55000, nextServiceMonths: 6, nextServiceDueOn: DateTime(2027, 4, 25)),
        HandoverServiceItem(
            id: 'b', name: 'กรองอากาศ', nextServiceMileage: 65000, nextServiceMonths: 12, nextServiceDueOn: DateTime(2027, 10, 25)),
      ]),
    );

    expect(find.textContaining('น้ำมันเครื่อง'), findsOneWidget);
    expect(find.textContaining('กรองอากาศ'), findsOneWidget);
    expect(find.byType(TextField), findsNothing);
    expect(find.text('เพิ่มรายการเอง'), findsNothing);
  });
}
