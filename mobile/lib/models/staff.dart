/// พนักงาน — ใช้เฉพาะส่วนที่มือถือต้องใช้ (ตัวเลือก mention และผู้เบิก)
library;

class StaffSummary {
  const StaffSummary({
    required this.id,
    required this.code,
    required this.fullName,
    this.positionName,
  });

  final int id;
  final String code;
  final String fullName;
  final String? positionName;

  static StaffSummary fromJson(Map<String, dynamic> j) => StaffSummary(
        id: (j['id'] as num).toInt(),
        code: j['code'] as String? ?? '',
        fullName: j['fullName'] as String? ?? '',
        positionName: j['positionName'] as String?,
      );
}
