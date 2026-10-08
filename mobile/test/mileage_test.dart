import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:garage_pro_service_ops/core/mileage.dart';

void main() {
  setUpAll(() => initializeDateFormatting('th'));

  test('addMonthsClamped matches DateOnly.AddMonths on the server', () {
    expect(addMonthsClamped(DateTime(2027, 1, 31), 1), DateTime(2027, 2, 28));
    expect(addMonthsClamped(DateTime(2028, 1, 31), 1), DateTime(2028, 2, 29));
    expect(addMonthsClamped(DateTime(2026, 10, 8), 6), DateTime(2027, 4, 8));
  });

  test('parseKm accepts separators but rejects decimals and negatives', () {
    expect(parseKm('45,210'), 45210);
    expect(parseKm(' 45 210 '), 45210);
    expect(parseKm('12.5'), isNull);
    expect(parseKm('-5'), isNull);
    expect(kmInputError(''), isNull);
    expect(kmInputError('10000000'), isNotNull);
  });

  test('parseDateOnly keeps the calendar day without timezone shift', () {
    expect(parseDateOnly('2027-04-08'), DateTime(2027, 4, 8));
    expect(parseDateOnly(null), isNull);
  });
}
