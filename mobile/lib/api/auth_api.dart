import 'api_client.dart';
import '../models/auth.dart';

class AuthApi {
  AuthApi(this._c);
  final ApiClient _c;

  Future<LoginResult> login(String userName, String password) async {
    final data = await _c.post<Map<String, dynamic>>('/auth/login',
        body: {'userName': userName, 'password': password});
    return LoginResult.fromJson(data);
  }

  /// ใช้ "เคาะประตู" ให้ server ตรวจว่าบัญชียังใช้งานได้ — ไม่ได้ใช้ข้อมูลที่ตอบกลับ
  /// ถ้าถูกตัดสิทธิ์ ApiClient จะล้างเซสชันให้เองจาก AUTH_REQUIRED (ดู SessionGuard)
  Future<void> ping() => _c.get<Object?>('/auth/me');
}
