import 'package:flutter/material.dart';

import '../core/tokens.dart';

/// ธีมเดียวของแอป — ตั้ง fontFamily ที่นี่ที่เดียว ไม่ต้องให้ทุก TextStyle ระบุเอง
ThemeData buildAppTheme() {
  const base = TextTheme();

  return ThemeData(
    useMaterial3: true,
    scaffoldBackgroundColor: T.pageBg,
    fontFamily: T.fontTh,
    // ถ้า glyph ไหนไม่มีในฟอนต์ที่ bundle ไว้ ให้ระบบเลือกฟอนต์ไทยของเครื่องแทนการขึ้นกล่องสี่เหลี่ยม
    fontFamilyFallback: const ['Noto Sans Thai', 'Thonburi', 'sans-serif'],
    colorScheme: ColorScheme.fromSeed(
      seedColor: T.blue600,
      primary: T.blue600,
      surface: T.cardBg,
    ),
    // [UI] ข้อความไทย line-height 1.6–1.7 · น้ำหนักไม่ต่ำกว่า 400
    textTheme: base.apply(bodyColor: T.text, displayColor: T.text),
    appBarTheme: const AppBarTheme(
      backgroundColor: T.navy900,
      foregroundColor: Colors.white,
      elevation: 0,
    ),
    navigationBarTheme: NavigationBarThemeData(
      backgroundColor: T.cardBg,
      indicatorColor: T.blue50,
      // [UI] เป้าแตะ ≥48px — เผื่อความสูงให้ป้ายภาษาไทยสองบรรทัดไม่ถูกตัด
      height: T.touchMin + 24,
      labelBehavior: NavigationDestinationLabelBehavior.alwaysShow,
      labelTextStyle: WidgetStateProperty.all(
        const TextStyle(fontSize: 12, fontWeight: FontWeight.w600, height: 1.4),
      ),
    ),
  );
}
