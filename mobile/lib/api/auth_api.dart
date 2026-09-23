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
}
