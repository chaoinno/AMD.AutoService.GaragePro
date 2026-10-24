/// รายการนัดเข้ารับบริการตั้งต้นของสาขา (น้ำมันเครื่อง/กรอง/เบรก…) — ผู้จัดการตั้งบนเว็บ
/// แอปอ่านอย่างเดียว ไว้ให้พนักงานเลือกตอนส่งมอบรถ
class ServiceReminderTemplate {
  const ServiceReminderTemplate({
    required this.id,
    required this.name,
    required this.defaultMileageInterval,
    required this.defaultMonths,
    this.sortOrder = 0,
  });

  final String id;
  final String name;

  /// ไมล์ที่นัดห่างจากไมล์ตอนส่งมอบ (กม.) — ค่าตั้งต้น แก้ต่อคันได้
  final int defaultMileageInterval;
  final int defaultMonths;
  final int sortOrder;

  static ServiceReminderTemplate fromJson(Map<String, dynamic> j) => ServiceReminderTemplate(
        id: j['id'] as String,
        name: j['name'] as String? ?? '',
        defaultMileageInterval: (j['defaultMileageInterval'] as num?)?.toInt() ?? 0,
        defaultMonths: (j['defaultMonths'] as num?)?.toInt() ?? 0,
        sortOrder: (j['sortOrder'] as num?)?.toInt() ?? 0,
      );
}
