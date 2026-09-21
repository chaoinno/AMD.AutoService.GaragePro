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
  /// [BIZ] docs/01-workflow.md §4: ช่างแตะ "ราคาและยอดเงินทุกชนิด" ไม่ได้ — ข้อนี้ไม่เปลี่ยน
  bool get canTakePayment =>
      this == AppRole.cashier || this == AppRole.office || this == AppRole.manager;

  /// ส่งมอบรถ + ปิดงาน — HandoverService.ValidateAsync และ JobStateMachine (Ready→Completed)
  ///
  /// [BIZ] 2026-09-17 เปิดให้ทุกบทบาทปฏิบัติการ รวมช่าง/หัวหน้าช่าง: คนที่ยืนอยู่กับลูกค้าตอนเซ็นรับรถ
  /// คือคนที่เข็นรถออกมา ไม่ใช่คนหลังเคาน์เตอร์ · สิ่งที่กันการส่งมอบก่อนเวลาคือ **ต้องออกใบเสร็จก่อน**
  /// (`HANDOVER_RECEIPT_REQUIRED`) ไม่ใช่บทบาทของคนกด — เช็คที่ `Handover.receiptIssued` ก่อนเปิดปุ่ม
  bool get canHandOverVehicle => this != AppRole.unknown;

  /// ปิดงาน (Ready→Completed) — เงื่อนไขเดียวกับส่งมอบ เพราะเป็นการกดต่อเนื่องกันของคนเดียวกัน
  /// guard ฝั่ง server (ชำระครบ + ใบเสร็จ + เซ็นรับรถ) ยังคำนวณจากข้อมูลจริงเสมอ กดข้ามไม่ได้
  bool get canCloseJob => canHandOverVehicle;

  /// จับเวลาทำงาน — WorkTimeService.ResolveAsync
  /// [BIZ] ยืนยันกับผู้ใช้ 2026-09-21: หัวหน้าช่างคุม/ตรวจ ไม่ลงมือ จึงจับเวลาไม่ได้เลย
  /// (ต่างจาก isWorkshop ที่รวม lead ด้วย — อย่าเอาไปใช้แทนกัน)
  bool get canTrackWorkTime => this == AppRole.technician;

  /// แก้เวลาย้อนหลังของคนอื่น — WorkTimeService.EditAsync
  bool get canEditWorkTime => this == AppRole.lead || this == AppRole.manager;

  /// รายงาน — ReportsService.Allowed
  bool get canSeeReports => this == AppRole.manager || this == AppRole.office;

  /// เปิดจ๊อบ/รับรถ — ไม่มี role gate ที่ API (มีแต่ RequireShiftSession)
  /// จำกัดที่ client เพื่อไม่ให้ช่างเห็นปุ่มที่ไม่ใช่งานตัวเอง ไม่ใช่เพื่อความปลอดภัย
  bool get isFrontOfHouse =>
      this == AppRole.frontDesk || this == AppRole.office || this == AppRole.manager;

  bool get isWorkshop => this == AppRole.technician || this == AppRole.lead;
}
