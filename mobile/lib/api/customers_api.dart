import 'package:dio/dio.dart';

import 'api_client.dart';
import '../models/customer.dart';

class CustomersApi {
  CustomersApi(this._c);
  final ApiClient _c;

  Future<List<CustomerSummary>> search({String? keyword, int pageSize = 20}) async {
    final data = await _c.get<Map<String, dynamic>>('/customers', query: {
      'keyword': ?keyword,
      'page': 1,
      'pageSize': pageSize,
    });
    return ((data['items'] as List<dynamic>?) ?? const [])
        .map((e) => CustomerSummary.fromJson(e as Map<String, dynamic>))
        .toList();
  }

  Future<CustomerDetail> get(int customerId) async {
    final data = await _c.get<Map<String, dynamic>>('/customers/$customerId');
    return CustomerDetail.fromJson(data);
  }

  /// server บังคับแค่ ชื่อ-สกุล-เบอร์ · ถ้าซ้ำจะคืน CUSTOMER_DUPLICATE พร้อมรายการที่ซ้ำใน details
  Future<CustomerDetail> create({
    required String firstName,
    required String lastName,
    required String phoneNumber1,
  }) async {
    final data = await _c.post<Map<String, dynamic>>('/customers', body: {
      'firstName': firstName,
      'lastName': lastName,
      'phoneNumber1': phoneNumber1,
    });
    return CustomerDetail.fromJson(data);
  }

  Future<VehicleReferenceData> vehicleReferenceData() async {
    final data = await _c.get<Map<String, dynamic>>('/cars/reference-data');
    return VehicleReferenceData.fromJson(data);
  }

  Future<List<LookupItem>> provinces() async {
    final data = await _c.get<List<dynamic>>('/locations/provinces');
    return data.map((e) => LookupItem.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<List<LookupItem>> models(int brandId) async {
    final data = await _c.get<List<dynamic>>('/cars/models', query: {'brandId': brandId});
    return data.map((e) => LookupItem.fromJson(e as Map<String, dynamic>)).toList();
  }

  Future<List<LookupItem>> nicknames(int modelId) async {
    final data = await _c.get<List<dynamic>>('/cars/nicknames', query: {'modelId': modelId});
    return data.map((e) => LookupItem.fromJson(e as Map<String, dynamic>)).toList();
  }

  /// endpoint รับเป็น multipart เพราะรองรับรูปรถด้วย — ที่นี่ส่งเฉพาะฟิลด์ข้อความ
  Future<int> createVehicle({
    required int customerId,
    required String registration,
    required int provinceId,
    required int brandId,
    required int modelId,
    required int nicknameId,
    required int yearId,
  }) async {
    final form = FormData.fromMap({
      'customerId': customerId,
      'registration': registration,
      'provinceId': provinceId,
      'brandId': brandId,
      'modelId': modelId,
      'nicknameId': nicknameId,
      'yearId': yearId,
    });

    final data = await _c.unwrap<Map<String, dynamic>>(
      () => _c.dio.post('/vehicles', data: form,
          options: Options(contentType: 'multipart/form-data')),
    );
    return (data['id'] as num).toInt();
  }
}
