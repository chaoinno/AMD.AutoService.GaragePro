import 'package:flutter/material.dart';

/// key ระดับแอป — ให้โค้ดนอก widget tree (เช่นตัวจัดการเซสชันหมดอายุใน api layer)
/// สั่ง pop/แสดงข้อความได้โดยไม่ต้องมี BuildContext
final rootNavigatorKey = GlobalKey<NavigatorState>();
final scaffoldMessengerKey = GlobalKey<ScaffoldMessengerState>();

/// ข้อความแจ้งเตือนระดับแอป — [UI] ต้องมีสาเหตุ และมีรหัสอ้างอิงเมื่อมาจาก error ของ server
/// [actionLabel]/[onAction] ใส่คู่กันเมื่อข้อความมีทางไปต่อ (เช่น "ดู" แจ้งเตือนใหม่)
void showAppMessage(
  String message, {
  String? traceId,
  bool isError = false,
  String? actionLabel,
  VoidCallback? onAction,
}) {
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
      // Flutter ให้ snackbar ที่มีปุ่มค้างจนกว่าจะกดเองเป็นค่าเริ่มต้น — ข้อความระดับแอปต้องหายเอง
      // ไม่งั้นค้างทับช่องพิมพ์/ปุ่มล่างจอทุกหน้าที่เปิดต่อ (เจอตอนทดสอบแจ้งเตือนบน simulator)
      persist: false,
      behavior: SnackBarBehavior.floating,
      action: actionLabel == null || onAction == null
          ? null
          : SnackBarAction(label: actionLabel, textColor: Colors.white, onPressed: onAction),
    ));
}
