/// กติกาฝั่งแอปของหน้าแพ็กเกจและการแจ้งชำระเงิน (docs/17 §2, §5.1) ฟังก์ชันล้วน ไม่มี I/O ทดสอบได้ตรงๆ
///
/// [SECURITY] ทุกข้อในไฟล์นี้เป็นแค่การบอกผู้ใช้ล่วงหน้า ไม่ให้เสียเวลาอัปโหลดแล้วโดนปฏิเสธ
/// server ตรวจซ้ำทุกข้อและเป็นผู้ตัดสินเสมอ (PACKAGE_PAYMENT_VALIDATION พร้อม `field`)
library;

import 'package:intl/intl.dart';

import '../../models/attachment.dart';
import '../../models/auth.dart';
import '../../models/package.dart';
import '../../widgets/trial_banner.dart' show paidWarnDays, trialWarnDays;

/// ช่องทางติดต่อทีมขาย ตรงกับ `PlanErrors.ContactLine` ฝั่ง backend
const packageContactLine = 'LINE @garagepro';

/// เวลาที่โอนล่วงหน้าเวลาปัจจุบันได้ไม่เกินเท่านี้ (เท่ากับ tolerance ของ server)
/// วงล้อเลือกนาทีปัดขึ้นทีละ 5 นาที เวลาที่เลือกจึงอาจเลยเวลาปัจจุบันไปเล็กน้อยได้
const paidAtFutureTolerance = Duration(minutes: 5);

/// เพดานยอดเงินที่ server รับ (≤ 999,999.99)
const packageMaxAmount = 999999.99;

/// ความยาวหมายเหตุสูงสุด (≤ 500)
const packageNoteMaxLength = 500;

/// ชนิดรูปที่แนบจากแอปได้ · PDF แนบได้เฉพาะบนเว็บ (แอปไม่มีตัวเลือกไฟล์)
const slipImageContentTypes = <String>{'image/png', 'image/jpeg', 'image/webp'};

final _money = NumberFormat('#,##0.00', 'en_US');
final _thaiDate = DateFormat('d MMM y', 'th');
final _thaiDateTime = DateFormat('d MMM y HH:mm', 'th');

String _moneyText(double v) => _money.format(v);

/// วันตามปฏิทินไทยจาก API (`...Date`) แสดงตรงตัว ไม่แปลงโซนเวลา
String packageDateText(DateTime date) => _thaiDate.format(date);

/// เวลา UTC จาก API แสดงเป็นเวลาเครื่อง (เครื่องในไทย = เวลาไทย เหมือนทุกหน้าในแอป)
String packageDateTimeText(DateTime utc) => '${_thaiDateTime.format(utc.toLocal())} น.';

// ---------------------------------------------------------------- ข้อความ

/// ชื่อแพ็กเกจที่ลูกค้าเห็น
String packagePlanLabel(String plan) => switch (plan) {
      'trial' => 'ทดลองใช้',
      'paid' => 'แพ็กเกจชำระเงิน',
      'suspended' => 'ถูกระงับ',
      'converted' => 'ย้ายไปใช้ GaragePro แล้ว',
      _ => 'ไม่ทราบแพ็กเกจ',
    };

/// บรรทัดรองของปุ่ม "แพ็กเกจและการชำระเงิน" ในหน้าโปรไฟล์ เช่น `SP1042 · ทดลองใช้ · เหลือ 23 วัน`
/// อ่านจากแพ็กเกจในเซสชัน (SessionGuard อัปเดตทุก 5 นาที) ไม่ยิง API เพิ่ม
String packageEntrySummary(PlanInfo? plan, WorkspaceInfo? workspace) {
  final parts = <String>[
    ?workspace?.workspaceNo,
    if (plan != null) packagePlanLabel(plan.kindName),
    if (plan != null && plan.readOnly) 'อ่านอย่างเดียว' else ?daysLeftText(plan?.daysLeft),
  ];
  return parts.isEmpty ? 'ดูแพ็กเกจและแจ้งชำระเงิน' : parts.join(' · ');
}

/// จำนวนเดือนสำหรับชิป
String monthOptionLabel(int months) => months == 12 ? '12 เดือน (1 ปี)' : '$months เดือน';

/// ข้อความวันคงเหลือ · null = ไม่มีวันหมดอายุ (ไม่เดาตัวเลข)
String? daysLeftText(int? daysLeft) => switch (daysLeft) {
      null => null,
      < 0 => 'หมดอายุแล้ว ${-daysLeft} วัน',
      0 => 'วันนี้เป็นวันสุดท้าย',
      _ => 'เหลือ $daysLeft วัน',
    };

/// ข้อความเหตุผลปฏิเสธ ใช้ `rejectReasonTh` ของ server ก่อนเสมอ ตารางนี้เป็นค่าสำรองเมื่อ server ไม่ส่ง
String rejectReasonText(PackagePayment p) =>
    p.rejectReasonTh ??
    switch (p.rejectReason) {
      'slip_unreadable' => 'สลิปไม่ชัด อ่านไม่ออก',
      'amount_mismatch' => 'ยอดเงินไม่ตรงกับแพ็กเกจ',
      'not_received' => 'ไม่พบยอดเงินเข้าบัญชี',
      'duplicate' => 'สลิปซ้ำกับที่เคยแจ้ง',
      'other' => 'อื่นๆ',
      _ => 'ไม่ระบุเหตุผล',
    };

/// บรรทัดสรุปของรายการในประวัติ (คู่กับชิปสถานะ)
String paymentOutcomeText(PackagePayment p) => switch (p.status) {
      PackagePaymentStatus.pending => 'รอทีมงานตรวจสอบ จะแจ้งผลในแอปเมื่อตรวจเสร็จ',
      PackagePaymentStatus.approved => p.periodLastUsableDate != null
          ? 'อนุมัติแล้ว ใช้งานได้ถึง ${packageDateText(p.periodLastUsableDate!)}'
          : 'อนุมัติแล้ว',
      PackagePaymentStatus.rejected => 'ปฏิเสธ: ${rejectReasonText(p)}',
      PackagePaymentStatus.cancelled => p.cancelledAt != null
          ? 'ยกเลิกเมื่อ ${packageDateTimeText(p.cancelledAt!)}'
          : 'ยกเลิกแล้ว',
      _ => 'สถานะ ${p.status}',
    };

/// "7%" จากสัดส่วน 0.07 · ตัดศูนย์ท้ายทศนิยม (0.075 = "7.5%")
String vatPercentLabel(double rate) {
  final percent = (rate * 100 * 100).round() / 100;
  final text = percent == percent.roundToDouble()
      ? percent.toStringAsFixed(0)
      : percent.toStringAsFixed(2).replaceFirst(RegExp(r'0+$'), '');
  return '$text%';
}

/// "ก่อน VAT 3,999.00 + VAT 7% 279.93" · null = server ไม่ได้แยก VAT มาให้ (ไม่คำนวณเอง ดู [PackageMonthOption])
String? priceBreakdownText(PackageMonthOption option, double? vatRate) {
  final subtotal = option.subtotal;
  final vat = option.vat;
  if (subtotal == null || vat == null) return null;
  final vatLabel = vatRate == null ? 'VAT' : 'VAT ${vatPercentLabel(vatRate)}';
  return 'ก่อน VAT ${_moneyText(subtotal)} + $vatLabel ${_moneyText(vat)}';
}

/// ป้ายใต้ยอดเงิน: "รวม VAT" เมื่อราคาแยก VAT · ไม่แยก = ไม่ต้องบอก
String priceUnitText(PackageMonthOption option) => option.vat != null ? 'บาท รวม VAT' : 'บาท';

/// ยอดที่ต้องโอนของจำนวนเดือนที่เลือก หรือคำอธิบายเมื่อยังไม่ตั้งราคา (docs/17 §11)
String expectedAmountHint(PackageMonthOption? option) {
  if (option == null) return 'เลือกระยะเวลาก่อน';
  final price = option.price;
  if (price == null) {
    return 'ยังไม่ได้ตั้งราคามาตรฐาน กรอกยอดที่โอนจริง สอบถามยอดได้ที่ $packageContactLine';
  }
  final vat = option.vat != null ? ' (รวม VAT)' : '';
  return 'ยอดที่ต้องโอน ${monthOptionLabel(option.months)}$vat ${_moneyText(price)} บาท';
}

/// ข้อความใต้ช่องยอดเงิน · ยอดตรงแพ็กเกจ = ยืนยันสั้นๆ (ตัวเลขแสดงในช่องแล้ว) · ไม่ตรง = null ([amountMismatchWarning] บอกเอง)
String? amountHelperText(String amountText, PackageMonthOption? option) {
  final price = option?.price;
  if (option == null || price == null) return expectedAmountHint(option);
  final amount = parseAmount(amountText);
  if (amount != null && (amount - price).abs() < 0.005) {
    return option.vat != null ? 'ตรงกับยอดที่ต้องโอน (รวม VAT)' : 'ตรงกับยอดที่ต้องโอน';
  }
  return amountText.trim().isEmpty ? 'กรอกยอดตามสลิป' : null;
}

/// ยอดที่กรอกไม่ตรงราคาแพ็กเกจ: แจ้งได้ (โอนขาด/เกิน ทีมงานคุยต่อ) แต่บอกให้รู้ก่อนส่ง · null = ไม่ต้องเตือน
String? amountMismatchWarning(String amountText, PackageMonthOption? option) {
  final price = option?.price;
  final amount = parseAmount(amountText);
  if (price == null || amount == null) return null;
  if ((amount - price).abs() < 0.005) return null;
  final vat = option!.vat != null ? ' รวม VAT' : '';
  return 'ยอดไม่ตรงกับราคาแพ็กเกจ (${_moneyText(price)} บาท$vat) แจ้งได้ ทีมงานจะตรวจสอบก่อนอนุมัติ';
}

/// ยอดในช่อง "ยอดเงินที่โอน" หลังเปลี่ยนระยะเวลา
///
/// [UI] กรอกยอดรวม VAT ให้ล่วงหน้า (docs/17 §6.3) แต่ห้ามทับยอดที่ผู้ใช้พิมพ์เอง: แทนที่เฉพาะเมื่อช่องว่าง
/// หรือยังเป็นยอดที่ระบบเติมให้ครั้งก่อน ([previousPrice]) · ระยะเวลาใหม่ไม่มีราคา = ล้างยอดเดิมที่ระบบเติม
/// เพราะไม่ใช่ยอดของแพ็กเกจที่เลือกแล้ว
String amountAfterOptionChange({
  required String current,
  double? previousPrice,
  double? newPrice,
}) {
  final typed = parseAmount(current);
  final untouched = current.trim().isEmpty ||
      (previousPrice != null && typed != null && (typed - previousPrice).abs() < 0.005);
  if (!untouched) return current;
  return newPrice == null ? '' : _moneyText(newPrice);
}

/// ข้อความที่คัดลอกเลขบัญชี/พร้อมเพย์: ตัวเลขล้วน เพราะช่องเลขบัญชีของแอปธนาคารบางแอปไม่รับขีด
/// · ไม่มีตัวเลขเลย = คืนค่าเดิม
String digitsForCopy(String value) {
  final digits = value.replaceAll(RegExp(r'\D'), '');
  return digits.isEmpty ? value.trim() : digits;
}

// ---------------------------------------------------------------- แถบเวลาคงเหลือ

/// ความยาวของแถบเวลาคงเหลือ (วัน) เท่ากับช่วงทดลองใช้ 30 วัน: อู่ทดลองใช้เห็นสัดส่วนจริง
/// อู่ที่เหลือมากกว่านี้เห็นแถบเต็ม (แถบบอกแค่ "ใกล้หมดแค่ไหน" ไม่ได้อ้างความยาวแพ็กเกจ)
const packageGaugeWindowDays = 30;

/// ระดับความเร่งด่วนของแพ็กเกจ ใช้เลือกสี/ไอคอนของแถบเวลาคงเหลือ (คู่กับข้อความเสมอ)
enum PackageUrgency { none, ok, warn, danger }

/// สัดส่วนของแถบเวลาคงเหลือ 0..1 · null = ไม่มีวันหมดอายุ (ไม่แสดงแถบ)
double? daysLeftFraction(int? daysLeft, {int windowDays = packageGaugeWindowDays}) {
  if (daysLeft == null) return null;
  if (daysLeft <= 0) return 0;
  return (daysLeft / windowDays).clamp(0.0, 1.0);
}

/// อ่านอย่างเดียว/ถูกระงับ/หมดอายุ = danger · ใกล้หมดตามเกณฑ์เดียวกับแถบบนสุด (ทดลองใช้ 7 วัน · ชำระเงิน 14 วัน) = warn
PackageUrgency packageUrgency(PackageOverview o) {
  if (o.readOnly || o.plan == 'suspended') return PackageUrgency.danger;
  final days = o.daysLeft;
  if (days == null) return PackageUrgency.none;
  if (days < 0) return PackageUrgency.danger;
  final warnAt = o.plan == 'paid' ? paidWarnDays : trialWarnDays;
  return days <= warnAt ? PackageUrgency.warn : PackageUrgency.ok;
}

// ---------------------------------------------------------------- ตรวจก่อนส่ง

/// อ่านยอดเงินจากช่องกรอก ยอมรับจุลภาคและช่องว่าง · null = ไม่ใช่จำนวนเงินที่ถูกต้อง
double? parseAmount(String text) {
  final cleaned = text.replaceAll(RegExp(r'[\s,]'), '');
  if (!RegExp(r'^\d+(\.\d{1,2})?$').hasMatch(cleaned)) return null;
  final value = double.tryParse(cleaned);
  if (value == null || value <= 0 || value > packageMaxAmount) return null;
  return value;
}

/// ข้อความใต้ช่องยอดเงิน · ช่องว่างไม่ถือเป็น error (ให้ปุ่มบอกเหตุผลเอง)
String? amountError(String text) {
  if (text.trim().isEmpty) return null;
  final cleaned = text.replaceAll(RegExp(r'[\s,]'), '');
  if (RegExp(r'^\d+\.\d{3,}$').hasMatch(cleaned)) return 'ทศนิยมได้ไม่เกิน 2 ตำแหน่ง';
  final value = double.tryParse(cleaned);
  if (value != null && value > packageMaxAmount) {
    return 'ยอดเงินต้องไม่เกิน ${_moneyText(packageMaxAmount)} บาท';
  }
  if (parseAmount(text) == null) return 'กรอกยอดเงินที่โอนเป็นตัวเลขมากกว่า 0';
  return null;
}

/// ตรวจวันเวลาที่โอน: ห้ามเป็นอนาคต (เผื่อ 5 นาที) และย้อนหลังได้ไม่เกิน [maxPastDays] วัน
String? paidAtError(DateTime paidAt, {required int maxPastDays, DateTime? now}) {
  final current = (now ?? DateTime.now()).toUtc();
  final value = paidAt.toUtc();
  if (value.isAfter(current.add(paidAtFutureTolerance))) {
    return 'เวลาที่โอนต้องไม่เป็นเวลาในอนาคต';
  }
  if (value.isBefore(current.subtract(Duration(days: maxPastDays)))) {
    return 'แจ้งชำระย้อนหลังได้ไม่เกิน $maxPastDays วัน';
  }
  return null;
}

/// ชนิดไฟล์รูปจากชื่อไฟล์ · null = แนบจากแอปไม่ได้ (เช่น HEIC หรือ PDF)
String? slipContentTypeFor(String fileName) {
  final dot = fileName.lastIndexOf('.');
  final ext = dot < 0 ? '' : fileName.substring(dot + 1).toLowerCase();
  return switch (ext) {
    'jpg' || 'jpeg' => 'image/jpeg',
    'png' => 'image/png',
    'webp' => 'image/webp',
    _ => null,
  };
}

/// ตรวจไฟล์สลิปก่อนอัปโหลด · null = ใช้ได้
String? slipError({required int sizeBytes, required String? contentType}) {
  if (sizeBytes <= 0) return 'ไฟล์สลิปไม่มีข้อมูล เลือกรูปใหม่';
  if (contentType == null || !slipImageContentTypes.contains(contentType)) {
    return 'แนบได้เฉพาะรูป PNG, JPEG หรือ WebP สลิปที่เป็น PDF ให้แจ้งผ่านเว็บ';
  }
  if (sizeBytes > AttachmentLimits.maxSizeBytes) {
    return 'ไฟล์สลิปใหญ่เกิน ${AttachmentLimits.maxSizeBytes ~/ (1024 * 1024)} MB เลือกรูปอื่น';
  }
  return null;
}

/// ข้อมูลที่ผู้ใช้กรอกในฟอร์ม (ยังไม่ผ่านการตรวจ)
class PackageFormInput {
  const PackageFormInput({
    this.monthCount,
    this.amountText = '',
    this.paidAt,
    this.slipSizeBytes,
    this.slipContentType,
    this.note = '',
  });

  final int? monthCount;
  final String amountText;

  /// เวลาเครื่อง (จากวงล้อ)
  final DateTime? paidAt;
  final int? slipSizeBytes;
  final String? slipContentType;
  final String note;
}

/// เหตุผลที่ปุ่ม "ส่งแจ้งชำระเงิน" ยังกดไม่ได้ = ขั้นถัดไปที่ยังขาด เรียงตามลำดับบนหน้าจอ
/// (เลือกระยะเวลา → แนบสลิป → วันเวลาที่โอน → ยอดเงิน → หมายเหตุ) · null = ส่งได้
///
/// [UI] เหตุผลจาก server (`cannotSubmitReasonTh`) มาก่อนเสมอ และใช้ข้อความนั้นตรงๆ ไม่แต่งใหม่
String? submitDisabledReason({
  required PackageOverview overview,
  required PackageFormInput input,
  bool hasUnresolvedRequest = false,
  DateTime? now,
}) {
  if (!overview.canSubmit) {
    return overview.cannotSubmitReasonTh ?? 'ยังแจ้งชำระเงินไม่ได้ในตอนนี้ ติดต่อ $packageContactLine';
  }
  if (hasUnresolvedRequest) {
    return 'มีคำขอที่ยังไม่ทราบผล กด "ลองใหม่" หรือ "ทิ้งคำขอนี้" ด้านบนก่อน';
  }
  if (overview.monthOptions.isEmpty) {
    return 'ยังไม่มีตัวเลือกจำนวนเดือน ติดต่อ $packageContactLine';
  }
  if (overview.optionFor(input.monthCount) == null) return 'เลือกระยะเวลาที่ต้องการต่ออายุ (ขั้นที่ 1)';
  final size = input.slipSizeBytes;
  if (size == null) return 'แนบรูปสลิปการโอน (ขั้นที่ 3)';
  final slip = slipError(sizeBytes: size, contentType: input.slipContentType);
  if (slip != null) return slip;
  final paidAt = input.paidAt;
  if (paidAt == null) return 'เลือกวันเวลาที่โอนตามสลิป';
  final paidAtProblem = paidAtError(paidAt, maxPastDays: overview.paidAtMaxPastDays, now: now);
  if (paidAtProblem != null) return paidAtProblem;
  if (input.amountText.trim().isEmpty) return 'กรอกยอดเงินที่โอน';
  final amount = amountError(input.amountText);
  if (amount != null) return amount;
  if (input.note.trim().length > packageNoteMaxLength) {
    return 'หมายเหตุยาวได้ไม่เกิน $packageNoteMaxLength ตัวอักษร';
  }
  return null;
}

/// ช่องในฟอร์มที่ error ของ server ชี้ (`error.field`) · null = ไม่ใช่ error ของช่องใดช่องหนึ่ง
///
/// error ไฟล์ (`ATTACHMENT_*`) ไม่มี `field` แต่เป็นเรื่องของช่องสลิปเสมอ
String? formFieldOfError(String code, String? field) {
  if (code.startsWith('ATTACHMENT_')) return 'slip';
  if (code != 'PACKAGE_PAYMENT_VALIDATION' || field == null) return null;
  final key = field.trim();
  return const {'paidAt', 'amount', 'monthCount', 'note', 'slip'}.contains(key) ? key : null;
}

/// error ที่แปลว่าข้อมูลของหน้า (แพ็กเกจ/รายการรอตรวจ) เปลี่ยนไปแล้ว ต้องโหลดใหม่ก่อนทำต่อ
bool errorNeedsRefresh(String code) => const {
      'PACKAGE_PAYMENT_PENDING_EXISTS',
      'PACKAGE_NOT_RENEWABLE',
      'PACKAGE_PAYMENT_UNAVAILABLE',
      'PACKAGE_PAYMENT_FORBIDDEN',
      'PACKAGE_PAYMENT_NOT_FOUND',
      'PACKAGE_PAYMENT_NOT_PENDING',
    }.contains(code);
