/// แพ็กเกจและการแจ้งชำระเงิน ตามสัญญา docs/17 §5.1 และ §8.7.1
///
/// ใช้กับสองแบบ ([PackageFlow]): อู่ที่สมัครเอง (shard sp1 · `selfserve`) และสาขา GaragePro กลุ่ม Service ใน db2
/// (`garagepro`) ที่ส่งต่อการแจ้งชำระเข้าระบบแจ้งชำระเดิมของ GaragePro (D9) · แบบหลังไม่มียอดเงิน/หมายเหตุ
/// ยกเลิกเองไม่ได้ และสลิปเป็นลิงก์รูปภายนอก
///
/// [BIZ] API ตัดฟิลด์ที่เป็น null ทิ้งจาก JSON (WhenWritingNull) และซ่อนยอดเงิน/หมายเหตุ/สลิปจาก role ที่ไม่ใช่ Manager
/// ทุกฟิลด์ที่ไม่บังคับจึงอ่านแบบ optional และ parse ไม่ผ่านต้องได้ null ไม่ใช่ exception
/// · เวลาเป็น UTC เสมอ (ค่าที่ไม่มีโซนถือเป็น UTC เหมือน `_instant` ใน auth.dart)
/// · ฟิลด์ `...Date` เป็นวันตามปฏิทินไทย `yyyy-MM-dd` อ่านด้วย [parseDateOnly] ห้ามแปลงโซนเวลา
library;

import 'dart:typed_data';

import '../core/mileage.dart' show parseDateOnly;

/// แบบของหน้าแพ็กเกจ (`flow` ของ overview)
abstract final class PackageFlow {
  /// อู่ที่สมัครเอง (sp1) · ข้อมูลอยู่ในตารางของเรา
  static const selfserve = 'selfserve';

  /// สาขา GaragePro ใน db2 · ทีมงานตรวจสลิปและกดยืนยันในระบบ GaragePro
  static const garagepro = 'garagepro';
}

/// สถานะรายการแจ้งชำระ ตรงกับ `sp_PackagePayment.Status` (ส่งเป็นตัวพิมพ์เล็ก)
/// · สาขา GaragePro มีแค่ pending · approved · cancelled
abstract final class PackagePaymentStatus {
  static const pending = 'pending';
  static const approved = 'approved';
  static const rejected = 'rejected';
  static const cancelled = 'cancelled';
}

/// เหตุผลที่แจ้งชำระไม่ได้ (`cannotSubmitCode`)
abstract final class PackageCannotSubmit {
  static const notManager = 'not_manager';
  static const pendingExists = 'pending_exists';
  static const notRenewable = 'not_renewable';
  static const unavailable = 'unavailable';
}

/// ไฟล์สลิปที่แนบ (ไม่มีใน JSON เมื่อผู้ดูไม่ใช่ Manager)
///
/// อู่ที่สมัครเอง: มี [contentType] และโหลดไฟล์ผ่าน API ที่ต้องมี Bearer token
/// · สาขา GaragePro: มีแค่ [url] ลิงก์รูป HTTPS ภายนอกตามที่ระบบเดิมเก็บไว้ (ไม่ต้องใช้ token ของเรา)
class PackageSlipInfo {
  const PackageSlipInfo({this.contentType, this.sizeBytes, this.url});

  final String? contentType;
  final int? sizeBytes;

  /// ลิงก์ HTTPS แบบเต็ม · null = โหลดผ่าน API ของเรา
  final String? url;

  bool get isPdf =>
      contentType?.toLowerCase() == 'application/pdf' ||
      (url != null && Uri.parse(url!).path.toLowerCase().endsWith('.pdf'));
  bool get isImage => contentType?.toLowerCase().startsWith('image/') ?? (url != null && !isPdf);

  /// รูปอยู่นอกระบบเรา โหลดตรงจากลิงก์ (ห้ามแนบ token ของเราไปกับคำขอนี้)
  bool get isExternal => url != null;

  static PackageSlipInfo? tryParse(Object? raw) {
    if (raw is! Map) return null;
    final type = _string(raw['contentType']);
    final url = _httpsUrl(raw['url']);
    if (type == null && url == null) return null;
    return PackageSlipInfo(contentType: type, sizeBytes: _int(raw['sizeBytes']), url: url);
  }
}

/// รายการแจ้งชำระหนึ่งครั้ง (`PackagePayment`)
class PackagePayment {
  const PackagePayment({
    required this.id,
    required this.shortId,
    required this.status,
    this.monthCount,
    this.amount,
    this.expectedAmount,
    this.paidAt,
    this.submittedAt,
    this.submittedBy,
    this.note,
    this.slip,
    this.periodStartsAt,
    this.periodEndsAt,
    this.periodLastUsableDate,
    this.reviewedAt,
    this.rejectReason,
    this.rejectReasonTh,
    this.rejectNote,
    this.cancelledAt,
  });

  /// อู่ที่สมัครเอง = GUID · สาขา GaragePro = เลขแถวของระบบเดิม (ตัวเลขในรูปข้อความ)
  final String id;

  /// รหัสที่ทีมงานใช้อ้างอิง: 8 ตัวอักษรแรกของ GUID (`spadmin approve-payment --id`) หรือเลขแถวทั้งเลข
  final String shortId;

  /// ตัวพิมพ์เล็ก ดู [PackagePaymentStatus] · ค่าที่แอปไม่รู้จักยังแสดงได้ (fallback ของ style)
  final String status;

  /// สาขา GaragePro ไม่มีค่าที่เชื่อถือได้ = null
  final int? monthCount;

  /// ไม่ใช่ Manager หรือสาขา GaragePro = null (server ไม่ส่ง)
  final double? amount;

  /// ราคามาตรฐาน ณ ตอนแจ้ง · null = ยังไม่ตั้งราคา หรือผู้ดูไม่ใช่ Manager
  final double? expectedAmount;

  /// UTC
  final DateTime? paidAt;
  final DateTime? submittedAt;
  final String? submittedBy;
  final String? note;
  final PackageSlipInfo? slip;

  /// ช่วงที่ต่อให้จริง (เฉพาะ approved) · UTC · [periodEndsAt] = เที่ยงคืนไทยของวันแรกที่หมดสิทธิ์
  final DateTime? periodStartsAt;
  final DateTime? periodEndsAt;

  /// วันสุดท้ายที่ใช้ได้หลังอนุมัติ (วันไทย) · สาขา GaragePro อาจส่งมาแค่ [periodEndsAt] ใช้ [lastUsableDate] แทน
  final DateTime? periodLastUsableDate;

  final DateTime? reviewedAt;
  final String? rejectReason;
  final String? rejectReasonTh;
  final String? rejectNote;
  final DateTime? cancelledAt;

  bool get isPending => status == PackagePaymentStatus.pending;

  /// วันสุดท้ายที่ใช้ได้ของรายการที่อนุมัติ: ค่าจาก server ก่อน ไม่มีก็คำนวณจาก [periodEndsAt]
  DateTime? get lastUsableDate => periodLastUsableDate ?? thaiLastUsableDate(periodEndsAt);

  static PackagePayment? tryParse(Object? raw) {
    if (raw is! Map) return null;
    final id = _idString(raw['id']);
    if (id == null) return null;

    // เลขแถวของสาขา GaragePro ใช้ทั้งเลข ตัด 8 ตัวแรกเฉพาะ GUID (ตัดเลขแถวจะได้เลขของรายการอื่น)
    final shortId = _idString(raw['shortId']) ??
        (RegExp(r'^\d+$').hasMatch(id) || id.length <= 8 ? id : id.substring(0, 8));
    return PackagePayment(
      id: id,
      shortId: shortId,
      status: (_string(raw['status']) ?? '').toLowerCase(),
      monthCount: _int(raw['monthCount']),
      amount: _double(raw['amount']),
      expectedAmount: _double(raw['expectedAmount']),
      paidAt: _instant(raw['paidAt']),
      submittedAt: _instant(raw['submittedAt']),
      submittedBy: _string(raw['submittedBy']),
      note: _string(raw['note']),
      slip: PackageSlipInfo.tryParse(raw['slip']),
      periodStartsAt: _instant(raw['periodStartsAt']),
      periodEndsAt: _instant(raw['periodEndsAt']),
      periodLastUsableDate: parseDateOnly(_string(raw['periodLastUsableDate'])),
      reviewedAt: _instant(raw['reviewedAt']),
      rejectReason: _string(raw['rejectReason'])?.toLowerCase(),
      rejectReasonTh: _string(raw['rejectReasonTh']),
      rejectNote: _string(raw['rejectNote']),
      cancelledAt: _instant(raw['cancelledAt']),
    );
  }

  /// รายการจาก `GET .../payments` ข้ามแถวที่รูปร่างผิดแทนที่จะทำให้ทั้งหน้าพัง
  static List<PackagePayment> listFrom(Object? raw) => raw is List
      ? raw.map(PackagePayment.tryParse).whereType<PackagePayment>().toList()
      : const [];
}

/// ตัวเลือกจำนวนเดือน พร้อมราคาและพรีวิว "ถ้าอนุมัติวันนี้ จะใช้งานได้ถึง …" ที่ server คำนวณ
///
/// [BIZ] ราคาทั้งสามช่องมาจาก server (ปัด VAT ที่นั่นที่เดียว) แอปไม่คำนวณ VAT เอง ไม่งั้นทศนิยมอาจไม่ตรงกับ
/// `expectedAmount` ที่ทีมงานใช้เทียบยอดโอน
class PackageMonthOption {
  const PackageMonthOption({
    required this.months,
    this.subtotal,
    this.vat,
    this.price,
    this.previewEndsAt,
    this.previewLastUsableDate,
  });

  final int months;

  /// ราคาก่อน VAT · null = ยังไม่ตั้งราคา หรือราคาไม่แยก VAT
  final double? subtotal;

  /// VAT ที่ server ปัดทศนิยมแล้ว · null = ไม่แยก VAT
  final double? vat;

  /// ยอดที่ต้องโอน (รวม VAT) = `expectedAmount` ของรายการที่แจ้ง · null = ยังไม่ตั้งราคา
  final double? price;
  final DateTime? previewEndsAt;
  final DateTime? previewLastUsableDate;

  /// มีราคาก่อน VAT และ VAT แยกให้แสดงได้
  bool get hasVatBreakdown => subtotal != null && vat != null;

  /// พรีวิววันสุดท้ายที่ใช้ได้: ค่าจาก server ก่อน ไม่มีก็คำนวณจาก [previewEndsAt]
  DateTime? get lastUsableDate => previewLastUsableDate ?? thaiLastUsableDate(previewEndsAt);

  static PackageMonthOption? tryParse(Object? raw) {
    if (raw is! Map) return null;
    final months = _int(raw['months']);
    if (months == null || months <= 0) return null;
    return PackageMonthOption(
      months: months,
      subtotal: _double(raw['subtotal']),
      vat: _double(raw['vat']),
      price: _double(raw['price']),
      previewEndsAt: _instant(raw['previewEndsAt']),
      previewLastUsableDate: parseDateOnly(_string(raw['previewLastUsableDate'])),
    );
  }
}

/// บัญชีรับเงิน · ไม่มีใน JSON = ยังไม่ตั้ง (แจ้งชำระไม่ได้)
class PackagePaymentAccount {
  const PackagePaymentAccount({this.bankName, this.accountNo, this.accountName, this.promptPayId});

  final String? bankName;
  final String? accountNo;
  final String? accountName;
  final String? promptPayId;

  static PackagePaymentAccount? tryParse(Object? raw) {
    if (raw is! Map) return null;
    final account = PackagePaymentAccount(
      bankName: _string(raw['bankName']),
      accountNo: _string(raw['accountNo']),
      accountName: _string(raw['accountName']),
      promptPayId: _string(raw['promptPayId']),
    );
    final empty = account.bankName == null &&
        account.accountNo == null &&
        account.accountName == null &&
        account.promptPayId == null;
    return empty ? null : account;
  }
}

/// ภาพรวมแพ็กเกจของอู่ (`GET /selfserve/package`)
class PackageOverview {
  const PackageOverview({
    this.flow = PackageFlow.selfserve,
    this.workspaceNo,
    required this.plan,
    this.status,
    this.endsAt,
    this.lastUsableDate,
    this.daysLeft,
    this.readOnly = false,
    this.vatRate,
    this.monthOptions = const [],
    this.paymentAccount,
    this.pending,
    this.canSubmit = false,
    this.cannotSubmitCode,
    this.cannotSubmitReasonTh,
    this.paidAtMaxPastDays = defaultPaidAtMaxPastDays,
    this.canCancel = true,
    this.amountRequired = true,
  });

  /// ค่าเริ่มต้นของ `SelfServe:Package:PaidAtMaxPastDays` ใช้เมื่อ server ไม่ส่ง
  static const defaultPaidAtMaxPastDays = 60;

  /// [PackageFlow] (ตัวพิมพ์เล็ก) · ไม่มีใน JSON = selfserve (server รุ่นก่อน D9)
  final String flow;

  final String? workspaceNo;

  /// trial | paid | suspended | converted · สาขา GaragePro = legacy (ตัวพิมพ์เล็ก)
  final String plan;

  /// provisioning | active | expired | purged
  final String? status;

  /// UTC · null = ไม่มีกำหนด
  final DateTime? endsAt;

  /// วันสุดท้ายที่ใช้ได้ (วันไทย)
  final DateTime? lastUsableDate;

  /// ค่าเดียวกับ /auth/me ติดลบได้ (หมดอายุไปแล้วกี่วัน)
  final int? daysLeft;
  final bool readOnly;

  /// อัตรา VAT เป็นสัดส่วน (0.07 = 7%) · null = ราคาไม่คิด VAT แยก
  final double? vatRate;
  final List<PackageMonthOption> monthOptions;
  final PackagePaymentAccount? paymentAccount;

  /// รายการรอตรวจ (มีได้ครั้งละ 1 รายการต่ออู่)
  final PackagePayment? pending;

  final bool canSubmit;
  final String? cannotSubmitCode;

  /// ข้อความจาก server ที่ใช้เป็นเหตุผลของปุ่มที่กดไม่ได้ ห้ามแต่งใหม่
  final String? cannotSubmitReasonTh;
  final int paidAtMaxPastDays;

  /// ยกเลิกรายการรอตรวจเองได้ (ร่วมกับเงื่อนไข Manager) · สาขา GaragePro = false
  final bool canCancel;

  /// ฟอร์มต้องมียอดเงิน (และรับหมายเหตุ) · สาขา GaragePro = false เพราะระบบเดิมไม่มีช่องเหล่านี้
  final bool amountRequired;

  bool get isGaragePro => flow == PackageFlow.garagepro;

  /// วันสุดท้ายที่ใช้ได้: ค่าจาก server ก่อน ไม่มีก็คำนวณจาก [endsAt]
  DateTime? get effectiveLastUsableDate => lastUsableDate ?? thaiLastUsableDate(endsAt);

  PackageMonthOption? optionFor(int? months) {
    if (months == null) return null;
    for (final option in monthOptions) {
      if (option.months == months) return option;
    }
    return null;
  }

  factory PackageOverview.fromJson(Object? raw) {
    if (raw is! Map) return const PackageOverview(plan: '');
    final options = raw['monthOptions'];
    final maxPast = _int(raw['paidAtMaxPastDays']);
    final flow = _string(raw['flow'])?.toLowerCase() ?? PackageFlow.selfserve;
    // ไม่มีสองช่องนี้ = server รุ่นก่อน D9 ซึ่งมีแต่อู่ที่สมัครเอง (ยกเลิกได้ · ต้องกรอกยอด) · garagepro ที่ไม่ส่งมาถือว่าไม่ได้
    final selfserveDefault = flow != PackageFlow.garagepro;
    return PackageOverview(
      flow: flow,
      workspaceNo: _string(raw['workspaceNo']),
      plan: (_string(raw['plan']) ?? '').toLowerCase(),
      status: _string(raw['status'])?.toLowerCase(),
      endsAt: _instant(raw['endsAt']),
      lastUsableDate: parseDateOnly(_string(raw['lastUsableDate'])),
      daysLeft: _int(raw['daysLeft']),
      readOnly: raw['readOnly'] == true,
      vatRate: _vatRate(raw['vatRate']),
      monthOptions: options is List
          ? options.map(PackageMonthOption.tryParse).whereType<PackageMonthOption>().toList()
          : const [],
      paymentAccount: PackagePaymentAccount.tryParse(raw['paymentAccount']),
      pending: PackagePayment.tryParse(raw['pending']),
      canSubmit: raw['canSubmit'] == true,
      cannotSubmitCode: _string(raw['cannotSubmitCode'])?.toLowerCase(),
      cannotSubmitReasonTh: _string(raw['cannotSubmitReasonTh']),
      paidAtMaxPastDays: maxPast != null && maxPast > 0 ? maxPast : defaultPaidAtMaxPastDays,
      canCancel: _bool(raw['canCancel']) ?? selfserveDefault,
      amountRequired: _bool(raw['amountRequired']) ?? selfserveDefault,
    );
  }
}

// ---------------------------------------------------------------- คำขอแจ้งชำระ

/// ยอดเงินในรูปที่ส่งให้ server: `"1500.00"` (ไม่มีจุลภาค ทศนิยม 2 ตำแหน่ง)
String amountWire(double amount) => amount.toStringAsFixed(2);

/// เวลาในรูป ISO 8601 พร้อม offset ของเครื่อง เช่น `2026-10-21T14:30:00+07:00`
///
/// ส่ง offset จริงของเครื่องแทนการตรึง +07:00 เพราะวงล้อเลือกเวลาตามนาฬิกาเครื่อง
/// (เครื่องในไทยจึงได้ +07:00 ตามสัญญา และเครื่องที่ตั้งโซนอื่นก็ยังได้เวลาจริงที่ถูกต้อง)
String paidAtWire(DateTime value) {
  final local = value.isUtc ? value.toLocal() : value;
  return isoWithOffset(local, local.timeZoneOffset);
}

/// [wallClock] คือเลขวันเวลาตามนาฬิกาที่โซน [offset] (ไม่สน isUtc) แยกออกมาให้ทดสอบได้โดยไม่ขึ้นกับโซนของเครื่องที่รันเทสต์
String isoWithOffset(DateTime wallClock, Duration offset) {
  String two(int v) => v.toString().padLeft(2, '0');
  final sign = offset.isNegative ? '-' : '+';
  final abs = offset.abs();
  final y = wallClock.year.toString().padLeft(4, '0');
  return '$y-${two(wallClock.month)}-${two(wallClock.day)}'
      'T${two(wallClock.hour)}:${two(wallClock.minute)}:${two(wallClock.second)}'
      '$sign${two(abs.inHours)}:${two(abs.inMinutes.remainder(60))}';
}

/// คำขอแจ้งชำระที่พร้อมส่ง
///
/// [BIZ] [requestId] สร้างครั้งเดียวตอนกดส่ง และ**ใช้ค่าเดิมเมื่อกดลองใหม่หลัง NETWORK_ERROR**
/// (server คืนแถวเดิม ไม่อัปโหลดซ้ำ ไม่ส่ง LINE ซ้ำ) ถ้าสร้างใหม่ทุกครั้งจะได้รายการซ้อน
class PackagePaymentDraft {
  const PackagePaymentDraft({
    required this.requestId,
    required this.monthCount,
    required this.amount,
    required this.paidAt,
    required this.note,
    required this.slipBytes,
    required this.slipFileName,
    required this.slipContentType,
  });

  final String requestId;
  final int monthCount;

  /// null = ไม่ส่งยอดเงิน (สาขา GaragePro ที่ `amountRequired` เป็น false)
  final double? amount;

  /// เวลาเครื่อง
  final DateTime paidAt;
  final String note;
  final Uint8List slipBytes;
  final String slipFileName;
  final String slipContentType;

  /// ฟิลด์ข้อความของ multipart (ไม่รวมไฟล์) แยกไว้ให้ทดสอบรูปแบบที่ส่งได้
  Map<String, String> fields() => {
        'requestId': requestId,
        'paidAt': paidAtWire(paidAt),
        if (amount != null) 'amount': amountWire(amount!),
        'monthCount': '$monthCount',
        if (note.trim().isNotEmpty) 'note': note.trim(),
      };
}

/// อัตรา VAT ที่ใช้ได้ต้องอยู่ระหว่าง 0 (ไม่รวม) ถึง 1 · ค่าแปลก (เช่นส่งมาเป็น 7 แทน 0.07) ถือว่าไม่รู้ ดีกว่าแสดง "VAT 700%"
double? _vatRate(Object? v) {
  final rate = _double(v);
  return rate != null && rate > 0 && rate < 1 ? rate : null;
}

/// วันสุดท้ายที่ใช้ได้ (วันตามปฏิทินไทย) จากเวลาหมดสิทธิ์ที่เป็นเที่ยงคืนไทย: `End − 1 tick` ตาม docs/17 §4
/// · คิดที่ UTC+7 ตรงๆ ไม่ใช้โซนของเครื่อง ผลจึงเหมือนค่า `...Date` ที่ server ส่ง · null = ไม่มีวันหมด
DateTime? thaiLastUsableDate(DateTime? end) {
  if (end == null) return null;
  final thai = end.toUtc().subtract(const Duration(microseconds: 1)).add(const Duration(hours: 7));
  return DateTime(thai.year, thai.month, thai.day);
}

String? _string(Object? v) {
  if (v is! String) return null;
  final text = v.trim();
  return text.isEmpty ? null : text;
}

/// id ที่เป็นตัวเลข (เลขแถวของสาขา GaragePro) อาจมาเป็น number ได้ แปลงเป็นข้อความให้เหมือนกัน
String? _idString(Object? v) => switch (v) {
      int n => '$n',
      num n when n == n.truncateToDouble() => '${n.toInt()}',
      _ => _string(v),
    };

bool? _bool(Object? v) => v is bool ? v : null;

/// รับเฉพาะลิงก์ HTTPS แบบเต็ม (ลิงก์ http ถูก iOS ATS บล็อกอยู่แล้ว และไม่ควรเปิดรูปสลิปผ่านช่องทางไม่เข้ารหัส)
String? _httpsUrl(Object? v) {
  final text = _string(v);
  if (text == null) return null;
  final uri = Uri.tryParse(text);
  if (uri == null || uri.scheme.toLowerCase() != 'https' || uri.host.isEmpty) return null;
  return uri.toString();
}

int? _int(Object? v) => switch (v) {
      num n => n.toInt(),
      String s => int.tryParse(s.trim()),
      _ => null,
    };

double? _double(Object? v) => switch (v) {
      num n => n.toDouble(),
      String s => double.tryParse(s.replaceAll(',', '').trim()),
      _ => null,
    };

/// ค่าเวลาที่ไม่มีโซนคือ UTC เสมอ (บทเรียน DateTimeKind ใน CLAUDE.md)
DateTime? _instant(Object? v) {
  final text = _string(v);
  if (text == null) return null;
  final hasZone = RegExp(r'(Z|[+-]\d{2}:?\d{2})$').hasMatch(text);
  return DateTime.tryParse(hasZone ? text : '${text}Z')?.toUtc();
}
