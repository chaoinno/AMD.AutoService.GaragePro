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

  /// ใช้ "เคาะประตู" ให้ server ตรวจว่าบัญชียังใช้งานได้ + อ่านแพ็กเกจล่าสุดของอู่กลับมา
  /// ถ้าถูกตัดสิทธิ์ ApiClient จะล้างเซสชันให้เองจาก AUTH_REQUIRED (ดู SessionGuard)
  /// [MeResult.fromJson] ไม่โยน exception — คำตอบรูปร่างแปลกได้แค่ "ไม่มีแพ็กเกจ"
  Future<MeResult> ping() async =>
      MeResult.fromJson(await _c.get<Object?>('/auth/me'));
}
