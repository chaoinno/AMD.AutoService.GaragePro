import 'package:flutter/material.dart';

/// key ระดับแอป — ให้โค้ดนอก widget tree (เช่นตัวจัดการเซสชันหมดอายุใน api layer)
/// สั่ง pop/แสดงข้อความได้โดยไม่ต้องมี BuildContext
final rootNavigatorKey = GlobalKey<NavigatorState>();
final scaffoldMessengerKey = GlobalKey<ScaffoldMessengerState>();

/// ข้อความแจ้งเตือนระดับแอป — [UI] ต้องมีสาเหตุ และมีรหัสอ้างอิงเมื่อมาจาก error ของ server
void showAppMessage(String message, {String? traceId, bool isError = false}) {
  final messenger = scaffoldMessengerKey.currentState;
  if (messenger == null) return;

  messenger
    ..clearSnackBars()
    ..showSnackBar(SnackBar(
      content: Text(
        traceId == null ? message : '$message\nรหัสอ้างอิง $traceId',
        style: const TextStyle(fontSize: 15, height: 1.6),
      ),
      backgroundColor: isError ? const Color(0xFFA31D1D) : const Color(0xFF1B3557),
      duration: const Duration(seconds: 6),
      behavior: SnackBarBehavior.floating,
    ));
}
