import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'api/client.dart';
import 'app/app.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializeDateFormatting('th');
  _registerFontLicenses();

  // โหลด session ที่เก็บไว้ก่อนสร้าง provider — จะได้ไม่กระพริบหน้า login ตอนเปิดแอป
  final prefs = await SharedPreferences.getInstance();

  runApp(ProviderScope(
    overrides: [sharedPrefsProvider.overrideWithValue(prefs)],
    child: const GarageProApp(),
  ));
}

/// ฟอนต์ที่ bundle มาเป็น SIL OFL — ต้องแสดง license ในหน้า "เกี่ยวกับ" ของแอป
void _registerFontLicenses() {
  LicenseRegistry.addLicense(() async* {
    for (final asset in ['assets/fonts/OFL-NotoSansThai.txt', 'assets/fonts/OFL-IBMPlexMono.txt']) {
      final text = await rootBundle.loadString(asset);
      yield LicenseEntryWithLineBreaks(const ['google_fonts_bundled'], text);
    }
  });
}
