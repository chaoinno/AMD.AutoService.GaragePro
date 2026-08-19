import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'api/client.dart';
import 'core/tokens.dart';
import 'features/approval/queue_page.dart';
import 'features/auth/login_page.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting('th');

  // โหลด session ที่เก็บไว้ก่อนสร้าง provider — จะได้ไม่กระพริบหน้า login ตอนเปิดแอป
  final prefs = await SharedPreferences.getInstance();

  runApp(ProviderScope(
    overrides: [sharedPrefsProvider.overrideWithValue(prefs)],
    child: const GarageProApp(),
  ));
}

class GarageProApp extends StatelessWidget {
  const GarageProApp({super.key});

  @override
  Widget build(BuildContext context) => MaterialApp(
        title: 'GaragePro Service Ops',
        debugShowCheckedModeBanner: false,
        theme: ThemeData(
          useMaterial3: true,
          scaffoldBackgroundColor: T.pageBg,
          colorScheme: ColorScheme.fromSeed(
            seedColor: T.blue600,
            primary: T.blue600,
            surface: T.cardBg,
          ),
          // [UI] ข้อความไทย line-height 1.6–1.7 · น้ำหนักไม่ต่ำกว่า 400
          textTheme: const TextTheme().apply(bodyColor: T.text, displayColor: T.text),
        ),
        home: const AuthGate(authenticated: QueuePage()),
      );
}
