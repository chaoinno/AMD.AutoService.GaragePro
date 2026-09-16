import 'api_client.dart';
import '../models/reports.dart';

/// รายงานเปิดเฉพาะ Manager/Office (ReportsService.Allowed) — role อื่นจะได้ REPORTS_FORBIDDEN
class ReportsApi {
  ReportsApi(this._c);
  final ApiClient _c;

  Future<DashboardReport> dashboard() async {
    final data = await _c.get<Map<String, dynamic>>('/reports/dashboard');
    return DashboardReport.fromJson(data);
  }

  Future<CycleTimeReport> cycleTime({DateTime? fromDate, DateTime? toDate}) async {
    final data = await _c.get<Map<String, dynamic>>('/reports/cycle-time', query: {
      'fromDate': ?fromDate?.toIso8601String(),
      'toDate': ?toDate?.toIso8601String(),
    });
    return CycleTimeReport.fromJson(data);
  }

  Future<SalesMarginReport> salesMargin({DateTime? fromDate, DateTime? toDate}) async {
    final data = await _c.get<Map<String, dynamic>>('/reports/sales-margin', query: {
      'fromDate': ?fromDate?.toIso8601String(),
      'toDate': ?toDate?.toIso8601String(),
    });
    return SalesMarginReport.fromJson(data);
  }

  Future<StockReport> stock() async {
    final data = await _c.get<Map<String, dynamic>>('/reports/stock');
    return StockReport.fromJson(data);
  }
}
