import 'package:flutter_test/flutter_test.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:garage_pro_service_ops/api/client.dart';
import 'package:garage_pro_service_ops/app/app.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  testWidgets('เปิดแอปโดยไม่มีเซสชันแล้วต้องเห็นหน้าเข้าสู่ระบบ', (tester) async {
    SharedPreferences.setMockInitialValues({});
    final prefs = await SharedPreferences.getInstance();

    await tester.pumpWidget(ProviderScope(
      overrides: [sharedPrefsProvider.overrideWithValue(prefs)],
      child: const GarageProApp(),
    ));
    await tester.pumpAndSettle();

    expect(find.text('เข้าสู่ระบบ'), findsWidgets);
  });
}
