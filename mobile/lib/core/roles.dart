/// บทบาทผู้ใช้ — ตรงกับ UserRole ใน Domain/Enums/CommonEnums.cs
/// server ส่งมาเป็นชื่อ enum ตรงๆ (`role.ToString()`) จึงเทียบแบบไม่สนตัวพิมพ์
///
/// [SECURITY] ทุกกฎในไฟล์นี้เป็นแค่ "จะแสดงปุ่มไหม" — server เป็นผู้ตัดสินเสมอ
/// ค่าที่นี่ต้องสะท้อนกฎฝั่ง backend ให้ตรง ไม่ใช่กฎคนละชุด
library;

enum AppRole {
  frontDesk('FrontDesk', 'พนักงานหน้าร้าน'),
  technician('Technician', 'ช่างเทคนิค'),
  office('Office', 'ธุรการ'),
  cashier('Cashier', 'แคชเชียร์'),
  manager('Manager', 'ผู้จัดการสาขา'),
  lead('Lead', 'หัวหน้าช่าง'),
  unknown('', 'ไม่ทราบบทบาท');

  const AppRole(this.token, this.labelTh);
  final String token;
  final String labelTh;

  static AppRole parse(String? raw) {
    final key = raw?.trim().toLowerCase();
    if (key == null || key.isEmpty) return AppRole.unknown;
    for (final role in AppRole.values) {
      if (role != AppRole.unknown && role.token.toLowerCase() == key) return role;
    }
    return AppRole.unknown;
  }

  /// รับชำระ/ออกใบเสร็จ — PosService.EnsureAccess
  bool get canTakePayment =>
      this == AppRole.cashier || this == AppRole.office || this == AppRole.manager;

  /// ส่งมอบรถ + เช็คลิสต์ของในรถ — HandoverService (รวม FrontDesk ตั้งแต่รอบเปิดสิทธิ์มือถือ)
  bool get canHandOverVehicle => canTakePayment || this == AppRole.frontDesk;

  /// รายงาน — ReportsService.Allowed
  bool get canSeeReports => this == AppRole.manager || this == AppRole.office;

  /// เปิดจ๊อบ/รับรถ — ไม่มี role gate ที่ API (มีแต่ RequireShiftSession)
  /// จำกัดที่ client เพื่อไม่ให้ช่างเห็นปุ่มที่ไม่ใช่งานตัวเอง ไม่ใช่เพื่อความปลอดภัย
  bool get isFrontOfHouse =>
      this == AppRole.frontDesk || this == AppRole.office || this == AppRole.manager;

  bool get isWorkshop => this == AppRole.technician || this == AppRole.lead;
}
