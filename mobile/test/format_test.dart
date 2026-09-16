import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/widgets/common.dart';

/// [UI] ตัวเลขเงินต้องเป็นรูปแบบ 1,234.56 เสมอ
void main() {
  test('จัดรูปแบบตัวเลขเงินตามกฎ UI', () {
    expect(money(0), '0.00');
    expect(money(1234.5), '1,234.50');
    expect(money(1234.56), '1,234.56');
    expect(money(8838.2), '8,838.20');
    expect(money(-250), '-250.00');
    expect(money(1234567.89), '1,234,567.89');
  });
}
