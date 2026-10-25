import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:package_info_plus/package_info_plus.dart';

/// เวอร์ชันของแอปที่ติดตั้งอยู่จริง เช่น "1.3.0 (5)" — อ่านจาก bundle ของเครื่อง ไม่ใช่ค่าที่เขียนซ้ำในโค้ด
/// จึงตรงกับ `version:` ใน pubspec.yaml ของ build นั้นเสมอ (ไว้ถามผู้ใช้ว่าเครื่องไหนยังเป็นรุ่นเก่า)
final appVersionProvider = FutureProvider<String>((ref) async {
  final info = await PackageInfo.fromPlatform();
  return info.buildNumber.isEmpty ? info.version : '${info.version} (${info.buildNumber})';
});
