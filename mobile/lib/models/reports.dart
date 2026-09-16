/// รายงาน — ตรงกับ DTO ใน Application/Dtos/ReportDtos.cs
/// ฟิลด์ต้นทุน/กำไรเป็น null เมื่อ role ปัจจุบันไม่มีสิทธิ์เห็น (server strip ให้แล้ว) — ต้องซ่อนทั้งบล็อก
library;

class JobStatusCount {
  const JobStatusCount(this.status, this.statusLabelTh, this.count);
  final String status;
  final String statusLabelTh;
  final int count;

  static JobStatusCount fromJson(Map<String, dynamic> j) => JobStatusCount(
        j['status'] as String,
        j['statusLabelTh'] as String? ?? '',
        (j['count'] as num?)?.toInt() ?? 0,
      );
}

class OverdueJob {
  const OverdueJob({
    required this.jobId,
    required this.jobNo,
    required this.customerName,
    required this.vehicleRegistration,
    required this.status,
    required this.statusLabelTh,
    required this.promiseAt,
  });

  final String jobId;
  final String jobNo;
  final String customerName;
  final String vehicleRegistration;
  final String status;
  final String statusLabelTh;
  final DateTime promiseAt;

  static OverdueJob fromJson(Map<String, dynamic> j) => OverdueJob(
        jobId: j['jobId'] as String,
        jobNo: j['jobNo'] as String? ?? '',
        customerName: j['customerName'] as String? ?? '',
        vehicleRegistration: j['vehicleRegistration'] as String? ?? '',
        status: j['status'] as String? ?? '',
        statusLabelTh: j['statusLabelTh'] as String? ?? '',
        promiseAt: DateTime.parse(j['promiseAt'] as String).toLocal(),
      );
}

class DashboardReport {
  const DashboardReport({
    required this.jobsByStatus,
    required this.overdueCount,
    required this.overdueJobs,
    required this.waitingQcCount,
    required this.waitingPaymentCount,
    required this.collectedToday,
    required this.receiptsIssuedToday,
  });

  final List<JobStatusCount> jobsByStatus;
  final int overdueCount;
  final List<OverdueJob> overdueJobs;
  final int waitingQcCount;
  final int waitingPaymentCount;
  final double collectedToday;
  final int receiptsIssuedToday;

  int get totalJobs => jobsByStatus.fold(0, (sum, s) => sum + s.count);

  static DashboardReport fromJson(Map<String, dynamic> j) => DashboardReport(
        jobsByStatus: ((j['jobsByStatus'] as List<dynamic>?) ?? const [])
            .map((e) => JobStatusCount.fromJson(e as Map<String, dynamic>))
            .toList(),
        overdueCount: (j['overdueCount'] as num?)?.toInt() ?? 0,
        overdueJobs: ((j['overdueJobs'] as List<dynamic>?) ?? const [])
            .map((e) => OverdueJob.fromJson(e as Map<String, dynamic>))
            .toList(),
        waitingQcCount: (j['waitingQcCount'] as num?)?.toInt() ?? 0,
        waitingPaymentCount: (j['waitingPaymentCount'] as num?)?.toInt() ?? 0,
        collectedToday: (j['collectedToday'] as num?)?.toDouble() ?? 0,
        receiptsIssuedToday: (j['receiptsIssuedToday'] as num?)?.toInt() ?? 0,
      );
}

class StatusDuration {
  const StatusDuration(this.status, this.statusLabelTh, this.segmentCount, this.averageHours);
  final String status;
  final String statusLabelTh;
  final int segmentCount;
  final double averageHours;

  static StatusDuration fromJson(Map<String, dynamic> j) => StatusDuration(
        j['status'] as String? ?? '',
        j['statusLabelTh'] as String? ?? '',
        (j['segmentCount'] as num?)?.toInt() ?? 0,
        (j['averageHours'] as num?)?.toDouble() ?? 0,
      );
}

class StuckJob {
  const StuckJob({
    required this.jobId,
    required this.jobNo,
    required this.customerName,
    required this.statusLabelTh,
    required this.hoursInStatus,
    required this.isOverdue,
  });

  final String jobId;
  final String jobNo;
  final String customerName;
  final String statusLabelTh;
  final double hoursInStatus;
  final bool isOverdue;

  static StuckJob fromJson(Map<String, dynamic> j) => StuckJob(
        jobId: j['jobId'] as String,
        jobNo: j['jobNo'] as String? ?? '',
        customerName: j['customerName'] as String? ?? '',
        statusLabelTh: j['statusLabelTh'] as String? ?? '',
        hoursInStatus: (j['hoursInStatus'] as num?)?.toDouble() ?? 0,
        isOverdue: j['isOverdue'] as bool? ?? false,
      );
}

class CycleTimeReport {
  const CycleTimeReport({
    required this.averageDurationByStatus,
    required this.completedJobCount,
    this.averageTotalHours,
    this.p90TotalHours,
    required this.topStuckJobs,
  });

  final List<StatusDuration> averageDurationByStatus;
  final int completedJobCount;
  final double? averageTotalHours;
  final double? p90TotalHours;
  final List<StuckJob> topStuckJobs;

  static CycleTimeReport fromJson(Map<String, dynamic> j) => CycleTimeReport(
        averageDurationByStatus: ((j['averageDurationByStatus'] as List<dynamic>?) ?? const [])
            .map((e) => StatusDuration.fromJson(e as Map<String, dynamic>))
            .toList(),
        completedJobCount: (j['completedJobCount'] as num?)?.toInt() ?? 0,
        averageTotalHours: (j['averageTotalHours'] as num?)?.toDouble(),
        p90TotalHours: (j['p90TotalHours'] as num?)?.toDouble(),
        topStuckJobs: ((j['topStuckJobs'] as List<dynamic>?) ?? const [])
            .map((e) => StuckJob.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class LineTypeTotals {
  const LineTypeTotals(this.type, this.netAmount, this.costAmount, this.marginAmount);
  final String type;
  final double netAmount;
  final double? costAmount;
  final double? marginAmount;

  static LineTypeTotals fromJson(Map<String, dynamic> j) => LineTypeTotals(
        j['type'] as String? ?? '',
        (j['netAmount'] as num?)?.toDouble() ?? 0,
        (j['costAmount'] as num?)?.toDouble(),
        (j['marginAmount'] as num?)?.toDouble(),
      );
}

class TechnicianRevenue {
  const TechnicianRevenue(this.technicianName, this.lineCount, this.netAmount);
  final String technicianName;
  final int lineCount;
  final double netAmount;

  static TechnicianRevenue fromJson(Map<String, dynamic> j) => TechnicianRevenue(
        j['technicianName'] as String? ?? '',
        (j['lineCount'] as num?)?.toInt() ?? 0,
        (j['netAmount'] as num?)?.toDouble() ?? 0,
      );
}

class SalesMarginReport {
  const SalesMarginReport({
    required this.quotationCount,
    required this.netAmount,
    this.costAmount,
    this.marginAmount,
    this.marginPercent,
    required this.byType,
    required this.byTechnician,
  });

  final int quotationCount;
  final double netAmount;

  /// null = role นี้ไม่มีสิทธิ์เห็นต้นทุน (server strip ให้แล้ว) — ต้องซ่อนทั้งบล็อก ไม่ใช่แสดง 0
  final double? costAmount;
  final double? marginAmount;
  final double? marginPercent;

  final List<LineTypeTotals> byType;
  final List<TechnicianRevenue> byTechnician;

  bool get canSeeCost => costAmount != null;

  static SalesMarginReport fromJson(Map<String, dynamic> j) => SalesMarginReport(
        quotationCount: (j['quotationCount'] as num?)?.toInt() ?? 0,
        netAmount: (j['netAmount'] as num?)?.toDouble() ?? 0,
        costAmount: (j['costAmount'] as num?)?.toDouble(),
        marginAmount: (j['marginAmount'] as num?)?.toDouble(),
        marginPercent: (j['marginPercent'] as num?)?.toDouble(),
        byType: ((j['byType'] as List<dynamic>?) ?? const [])
            .map((e) => LineTypeTotals.fromJson(e as Map<String, dynamic>))
            .toList(),
        byTechnician: ((j['byTechnician'] as List<dynamic>?) ?? const [])
            .map((e) => TechnicianRevenue.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}

class StockAgingBucket {
  const StockAgingBucket(this.bucketLabelTh, this.value);
  final String bucketLabelTh;
  final double value;

  static StockAgingBucket fromJson(Map<String, dynamic> j) => StockAgingBucket(
        j['bucketLabelTh'] as String? ?? '',
        (j['value'] as num?)?.toDouble() ?? 0,
      );
}

class AgingStockLot {
  const AgingStockLot({
    required this.catalogCode,
    required this.catalogName,
    required this.warehouseName,
    required this.remainingQuantity,
    required this.unitCost,
    required this.ageDays,
  });

  final String catalogCode;
  final String catalogName;
  final String warehouseName;
  final int remainingQuantity;
  final double unitCost;
  final int ageDays;

  static AgingStockLot fromJson(Map<String, dynamic> j) => AgingStockLot(
        catalogCode: j['catalogCode'] as String? ?? '',
        catalogName: j['catalogName'] as String? ?? '',
        warehouseName: j['warehouseName'] as String? ?? '',
        remainingQuantity: (j['remainingQuantity'] as num?)?.toInt() ?? 0,
        unitCost: (j['unitCost'] as num?)?.toDouble() ?? 0,
        ageDays: (j['ageDays'] as num?)?.toInt() ?? 0,
      );
}

class StockReport {
  const StockReport({
    required this.totalValuation,
    required this.damagedValuation,
    required this.agingBuckets,
    required this.oldestLots,
  });

  final double totalValuation;
  final double damagedValuation;
  final List<StockAgingBucket> agingBuckets;
  final List<AgingStockLot> oldestLots;

  static StockReport fromJson(Map<String, dynamic> j) => StockReport(
        totalValuation: (j['totalValuation'] as num?)?.toDouble() ?? 0,
        damagedValuation: (j['damagedValuation'] as num?)?.toDouble() ?? 0,
        agingBuckets: ((j['agingBuckets'] as List<dynamic>?) ?? const [])
            .map((e) => StockAgingBucket.fromJson(e as Map<String, dynamic>))
            .toList(),
        oldestLots: ((j['oldestLots'] as List<dynamic>?) ?? const [])
            .map((e) => AgingStockLot.fromJson(e as Map<String, dynamic>))
            .toList(),
      );
}
