import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/core/tokens.dart';
import 'package:garage_pro_service_ops/features/package/package_rules.dart';
import 'package:garage_pro_service_ops/models/auth.dart';
import 'package:garage_pro_service_ops/models/package.dart';
import 'package:intl/date_symbol_data_local.dart';

/// กติกาฝั่งแอปของหน้าแจ้งชำระ ต้องตรงกับที่ server บังคับ (docs/17 §2, §5.1)
void main() {
  setUpAll(() => initializeDateFormatting('th'));

  // ราคาจริง (docs/17 §5.5): 3,999 บาท/เดือนก่อน VAT + VAT 7% ที่ server ปัดแล้ว
  const option3 = PackageMonthOption(months: 3, subtotal: 11997, vat: 839.79, price: 12836.79);
  const option12 = PackageMonthOption(months: 12, subtotal: 47988, vat: 3359.16, price: 51347.16);
  const noVat = PackageMonthOption(months: 1, price: 3999);
  const unpriced = PackageMonthOption(months: 1);

  PackageOverview overview({
    bool canSubmit = true,
    String? reason,
    List<PackageMonthOption> options = const [option3, unpriced],
  }) =>
      PackageOverview(
        plan: 'trial',
        canSubmit: canSubmit,
        cannotSubmitReasonTh: reason,
        monthOptions: options,
      );

  group('ยอดเงิน', () {
    test('parseAmount รับจุลภาค/ช่องว่าง ทศนิยมไม่เกิน 2 ตำแหน่ง', () {
      expect(parseAmount('1500'), 1500);
      expect(parseAmount('1,500.50'), 1500.5);
      expect(parseAmount(' 590.5 '), 590.5);
      expect(parseAmount('0'), isNull);
      expect(parseAmount('-5'), isNull);
      expect(parseAmount('1.234'), isNull);
      expect(parseAmount('abc'), isNull);
      expect(parseAmount('1000000'), isNull);
      expect(parseAmount('999999.99'), 999999.99);
    });

    test('amountError: ช่องว่างไม่ใช่ error · บอกเหตุผลเฉพาะเจาะจง', () {
      expect(amountError(''), isNull);
      expect(amountError('1,770.00'), isNull);
      expect(amountError('1.234'), contains('ทศนิยม'));
      expect(amountError('1000000'), contains('999,999.99'));
      expect(amountError('0'), contains('มากกว่า 0'));
    });

    test('amountWire เป็นทศนิยม 2 ตำแหน่ง ไม่มีจุลภาค', () {
      expect(amountWire(1500), '1500.00');
      expect(amountWire(1234567.5), '1234567.50');
    });

    test('ยอดที่ต้องโอนและคำเตือนยอดไม่ตรง (แจ้งได้ ไม่บล็อก)', () {
      expect(expectedAmountHint(null), 'เลือกระยะเวลาก่อน');
      expect(expectedAmountHint(option3), 'ยอดที่ต้องโอน 3 เดือน (รวม VAT) 12,836.79 บาท');
      expect(expectedAmountHint(noVat), 'ยอดที่ต้องโอน 1 เดือน 3,999.00 บาท');
      expect(expectedAmountHint(unpriced), contains('LINE @garagepro'));

      expect(amountMismatchWarning('12836.79', option3), isNull);
      expect(amountMismatchWarning('12,836.79', option3), isNull);
      expect(amountMismatchWarning('11997', option3), contains('12,836.79 บาท รวม VAT'));
      expect(amountMismatchWarning('1500', noVat), isNot(contains('VAT')));
      expect(amountMismatchWarning('1500', unpriced), isNull);
      expect(amountMismatchWarning('', option3), isNull);
    });

    test('ข้อความใต้ช่องยอดเงิน: ยอดตรงยืนยันสั้นๆ · ไม่ตรงให้คำเตือนบอกเอง', () {
      expect(amountHelperText('', null), 'เลือกระยะเวลาก่อน');
      expect(amountHelperText('', unpriced), contains('LINE @garagepro'));
      expect(amountHelperText('', option3), 'กรอกยอดตามสลิป');
      expect(amountHelperText('12,836.79', option3), 'ตรงกับยอดที่ต้องโอน (รวม VAT)');
      expect(amountHelperText('3999', noVat), 'ตรงกับยอดที่ต้องโอน');
      expect(amountHelperText('12000', option3), isNull);
    });

    test('เติมยอดรวม VAT ให้เมื่อเลือกระยะเวลา แต่ไม่ทับยอดที่ผู้ใช้พิมพ์เอง', () {
      // ช่องว่าง = เติมยอดของระยะเวลาที่เลือก
      expect(amountAfterOptionChange(current: '', newPrice: 12836.79), '12,836.79');
      // ยังเป็นยอดที่ระบบเติมครั้งก่อน = เปลี่ยนตาม
      expect(amountAfterOptionChange(current: '12,836.79', previousPrice: 12836.79, newPrice: 51347.16),
          '51,347.16');
      // ผู้ใช้พิมพ์ยอดเอง = คงไว้
      expect(amountAfterOptionChange(current: '12000', previousPrice: 12836.79, newPrice: 51347.16), '12000');
      expect(amountAfterOptionChange(current: '12000', newPrice: 51347.16), '12000');
      // ระยะเวลาใหม่ไม่มีราคา: ล้างยอดเดิมที่ระบบเติม (ไม่ใช่ยอดของแพ็กเกจที่เลือกแล้ว)
      expect(amountAfterOptionChange(current: '12,836.79', previousPrice: 12836.79), '');
      expect(amountAfterOptionChange(current: '', previousPrice: 12836.79), '');
    });

    test('VAT: ป้ายเปอร์เซ็นต์ · บรรทัดก่อน VAT · หน่วยใต้ยอด', () {
      expect(vatPercentLabel(0.07), '7%');
      expect(vatPercentLabel(0.075), '7.5%');
      expect(priceBreakdownText(option3, 0.07), 'ก่อน VAT 11,997.00 + VAT 7% 839.79');
      expect(priceBreakdownText(option12, 0.07), 'ก่อน VAT 47,988.00 + VAT 7% 3,359.16');
      expect(priceBreakdownText(option3, null), 'ก่อน VAT 11,997.00 + VAT 839.79');
      // server ไม่แยก VAT มา = ไม่คำนวณเอง
      expect(priceBreakdownText(noVat, 0.07), isNull);
      expect(priceUnitText(option3), 'บาท รวม VAT');
      expect(priceUnitText(noVat), 'บาท');
    });

    test('คัดลอกเลขบัญชีเป็นตัวเลขล้วน', () {
      expect(digitsForCopy('068-8-63750-3'), '0688637503');
      expect(digitsForCopy(' 0812345678 '), '0812345678');
      expect(digitsForCopy('ไม่มีเลข'), 'ไม่มีเลข');
    });
  });

  group('วันเวลาที่โอน', () {
    final now = DateTime.utc(2026, 10, 21, 7, 0);

    test('อนาคตเกิน 5 นาทีไม่ได้ · ภายใน 5 นาทีได้ (วงล้อปัดนาทีขึ้น)', () {
      expect(paidAtError(now.add(const Duration(minutes: 4)), maxPastDays: 60, now: now), isNull);
      expect(paidAtError(now.add(const Duration(minutes: 5)), maxPastDays: 60, now: now), isNull);
      expect(paidAtError(now.add(const Duration(minutes: 6)), maxPastDays: 60, now: now),
          contains('อนาคต'));
    });

    test('ย้อนหลังได้ไม่เกิน paidAtMaxPastDays วัน', () {
      expect(paidAtError(now.subtract(const Duration(days: 59)), maxPastDays: 60, now: now), isNull);
      expect(paidAtError(now.subtract(const Duration(days: 61)), maxPastDays: 60, now: now),
          'แจ้งชำระย้อนหลังได้ไม่เกิน 60 วัน');
      expect(paidAtError(now.subtract(const Duration(days: 8)), maxPastDays: 7, now: now),
          contains('7 วัน'));
    });

    test('เทียบเป็นเวลาจริง ไม่ขึ้นกับว่าค่าเป็นเวลาเครื่องหรือ UTC', () {
      final local = now.toLocal().add(const Duration(minutes: 10));
      expect(paidAtError(local, maxPastDays: 60, now: now), isNotNull);
    });
  });

  group('สลิป', () {
    test('ชนิดไฟล์จากชื่อ · HEIC/PDF แนบจากแอปไม่ได้', () {
      expect(slipContentTypeFor('image_picker_1.jpg'), 'image/jpeg');
      expect(slipContentTypeFor('A.JPEG'), 'image/jpeg');
      expect(slipContentTypeFor('slip.png'), 'image/png');
      expect(slipContentTypeFor('slip.webp'), 'image/webp');
      expect(slipContentTypeFor('IMG_0001.HEIC'), isNull);
      expect(slipContentTypeFor('slip.pdf'), isNull);
      expect(slipContentTypeFor('noext'), isNull);
    });

    test('slipError: ว่าง · ชนิดผิด (แนะนำเว็บสำหรับ PDF) · เกิน 15 MB', () {
      expect(slipError(sizeBytes: 1000, contentType: 'image/jpeg'), isNull);
      expect(slipError(sizeBytes: 0, contentType: 'image/jpeg'), contains('ไม่มีข้อมูล'));
      expect(slipError(sizeBytes: 1000, contentType: null), contains('PDF ให้แจ้งผ่านเว็บ'));
      expect(slipError(sizeBytes: 1000, contentType: 'application/pdf'), contains('ผ่านเว็บ'));
      expect(slipError(sizeBytes: 15 * 1024 * 1024 + 1, contentType: 'image/png'), contains('15 MB'));
    });
  });

  group('submitDisabledReason', () {
    final now = DateTime.utc(2026, 10, 21, 7);
    final paidAt = now.subtract(const Duration(hours: 1));

    PackageFormInput complete({String amount = '12,836.79', int? months = 3, DateTime? at}) =>
        PackageFormInput(
          monthCount: months,
          amountText: amount,
          paidAt: at ?? paidAt,
          slipSizeBytes: 1000,
          slipContentType: 'image/jpeg',
        );

    test('เหตุผลจาก server มาก่อนเสมอ และใช้ข้อความนั้นตรงๆ', () {
      final reason = submitDisabledReason(
        overview: overview(canSubmit: false, reason: 'เฉพาะผู้จัดการอู่'),
        input: complete(),
        now: now,
      );
      expect(reason, 'เฉพาะผู้จัดการอู่');
      expect(submitDisabledReason(overview: overview(canSubmit: false), input: complete(), now: now),
          contains('LINE @garagepro'));
    });

    test('เรียงตามลำดับบนหน้าจอ: ระยะเวลา → สลิป → เวลาที่โอน → ยอด', () {
      String? reasonOf(PackageFormInput input) =>
          submitDisabledReason(overview: overview(), input: input, now: now);

      expect(reasonOf(const PackageFormInput()), 'เลือกระยะเวลาที่ต้องการต่ออายุ (ขั้นที่ 1)');
      expect(reasonOf(const PackageFormInput(monthCount: 6)), 'เลือกระยะเวลาที่ต้องการต่ออายุ (ขั้นที่ 1)');
      // ยอดเงินถูกเติมให้ตอนเลือกระยะเวลา ขั้นถัดไปที่ขาดจึงเป็นสลิป
      expect(reasonOf(const PackageFormInput(monthCount: 3, amountText: '12,836.79')),
          'แนบรูปสลิปการโอน (ขั้นที่ 3)');
      expect(
          reasonOf(const PackageFormInput(
              monthCount: 3, amountText: '12,836.79', slipSizeBytes: 1000, slipContentType: 'image/heic')),
          contains('PNG, JPEG หรือ WebP'));
      expect(
          reasonOf(const PackageFormInput(
              monthCount: 3, amountText: '12,836.79', slipSizeBytes: 1000, slipContentType: 'image/jpeg')),
          'เลือกวันเวลาที่โอนตามสลิป');
      expect(
          reasonOf(PackageFormInput(
              monthCount: 3, paidAt: paidAt, slipSizeBytes: 1000, slipContentType: 'image/jpeg')),
          'กรอกยอดเงินที่โอน');
      expect(reasonOf(complete(amount: '0')), contains('มากกว่า 0'));
      expect(reasonOf(complete(at: now.add(const Duration(hours: 1)))), contains('อนาคต'));
      expect(reasonOf(complete()), isNull);
    });

    test('มีคำขอที่ยังไม่ทราบผล = ห้ามสร้างคำขอใหม่จนกว่าจะลองใหม่หรือทิ้ง', () {
      expect(
        submitDisabledReason(
            overview: overview(), input: complete(), hasUnresolvedRequest: true, now: now),
        contains('ลองใหม่'),
      );
    });

    test('ไม่มีตัวเลือกเดือนเลย = บอกให้ติดต่อทีมงาน', () {
      expect(submitDisabledReason(overview: overview(options: const []), input: complete(), now: now),
          contains('LINE @garagepro'));
    });

    test('ยอดไม่ตรงราคาส่งได้ (ส่วนลด/โปรโมชัน)', () {
      expect(submitDisabledReason(overview: overview(), input: complete(amount: '12000'), now: now),
          isNull);
    });

    test('หมายเหตุเกิน 500 ตัวอักษร', () {
      final input = PackageFormInput(
        monthCount: 3,
        amountText: '12836.79',
        paidAt: paidAt,
        slipSizeBytes: 10,
        slipContentType: 'image/png',
        note: 'ก' * 501,
      );
      expect(submitDisabledReason(overview: overview(), input: input, now: now), contains('500'));
    });
  });

  group('ข้อความ', () {
    PackagePayment payment(String status, {Map<String, Object?> extra = const {}}) =>
        PackagePayment.tryParse({'id': '3f2a9c1b-0000', 'status': status, ...extra})!;

    test('ผลของรายการตามสถานะ', () {
      expect(paymentOutcomeText(payment('pending')), contains('รอทีมงานตรวจสอบ'));
      expect(paymentOutcomeText(payment('approved', extra: {'periodLastUsableDate': '2027-02-13'})),
          'อนุมัติแล้ว ใช้งานได้ถึง 13 ก.พ. 2027');
      expect(paymentOutcomeText(payment('approved')), 'อนุมัติแล้ว');
      expect(paymentOutcomeText(payment('rejected', extra: {'rejectReasonTh': 'ยอดเงินไม่ตรงกับแพ็กเกจ'})),
          'ปฏิเสธ: ยอดเงินไม่ตรงกับแพ็กเกจ');
      expect(paymentOutcomeText(payment('cancelled')), 'ยกเลิกแล้ว');
    });

    test('เหตุผลปฏิเสธใช้ข้อความ server ก่อน ตารางในแอปเป็นค่าสำรอง', () {
      expect(rejectReasonText(payment('rejected', extra: {'rejectReason': 'not_received'})),
          'ไม่พบยอดเงินเข้าบัญชี');
      expect(
          rejectReasonText(payment('rejected',
              extra: {'rejectReason': 'not_received', 'rejectReasonTh': 'ข้อความจาก server'})),
          'ข้อความจาก server');
      expect(rejectReasonText(payment('rejected')), 'ไม่ระบุเหตุผล');
    });

    test('วันคงเหลือ · ป้ายเดือน · ชื่อแพ็กเกจ', () {
      expect(daysLeftText(null), isNull);
      expect(daysLeftText(23), 'เหลือ 23 วัน');
      expect(daysLeftText(0), 'วันนี้เป็นวันสุดท้าย');
      expect(daysLeftText(-3), 'หมดอายุแล้ว 3 วัน');
      expect(monthOptionLabel(1), '1 เดือน');
      expect(monthOptionLabel(12), '12 เดือน (1 ปี)');
      expect(packagePlanLabel('paid'), 'แพ็กเกจชำระเงิน');
      expect(packagePlanLabel('enterprise'), 'ไม่ทราบแพ็กเกจ');
    });

    test('สรุปบนปุ่มโปรไฟล์', () {
      expect(
        packageEntrySummary(const PlanInfo(kindName: 'trial', daysLeft: 23),
            const WorkspaceInfo(workspaceNo: 'SP1042', name: 'อู่')),
        'SP1042 · ทดลองใช้ · เหลือ 23 วัน',
      );
      expect(packageEntrySummary(const PlanInfo(kindName: 'paid', daysLeft: -1, readOnly: true), null),
          'แพ็กเกจชำระเงิน · อ่านอย่างเดียว');
      expect(packageEntrySummary(null, null), 'ดูแพ็กเกจและแจ้งชำระเงิน');
    });
  });

  group('แถบเวลาคงเหลือ', () {
    test('สัดส่วนเทียบช่วง 30 วัน · หมดอายุ = 0 · ไม่มีวันหมด = ไม่มีแถบ', () {
      expect(daysLeftFraction(null), isNull);
      expect(daysLeftFraction(15), 0.5);
      expect(daysLeftFraction(30), 1.0);
      expect(daysLeftFraction(200), 1.0);
      expect(daysLeftFraction(0), 0);
      expect(daysLeftFraction(-3), 0);
    });

    test('ระดับความเร่งด่วน: เกณฑ์เตือนเดียวกับแถบบนสุด (ทดลองใช้ 7 วัน · ชำระเงิน 14 วัน)', () {
      PackageOverview o(String plan, int? days, {bool readOnly = false}) =>
          PackageOverview(plan: plan, daysLeft: days, readOnly: readOnly);

      expect(packageUrgency(o('trial', 23)), PackageUrgency.ok);
      expect(packageUrgency(o('trial', 8)), PackageUrgency.ok);
      expect(packageUrgency(o('trial', 7)), PackageUrgency.warn);
      expect(packageUrgency(o('paid', 14)), PackageUrgency.warn);
      expect(packageUrgency(o('paid', 15)), PackageUrgency.ok);
      expect(packageUrgency(o('paid', -1)), PackageUrgency.danger);
      expect(packageUrgency(o('trial', 20, readOnly: true)), PackageUrgency.danger);
      expect(packageUrgency(o('suspended', null)), PackageUrgency.danger);
      expect(packageUrgency(o('converted', null)), PackageUrgency.none);
    });
  });

  group('error จาก server', () {
    test('ชี้ช่องในฟอร์มจาก field · error ไฟล์เป็นของช่องสลิปเสมอ', () {
      expect(formFieldOfError('PACKAGE_PAYMENT_VALIDATION', 'amount'), 'amount');
      expect(formFieldOfError('PACKAGE_PAYMENT_VALIDATION', 'paidAt'), 'paidAt');
      // requestId/until/reason ไม่ใช่ช่องที่ผู้ใช้แก้ได้ในหน้านี้
      expect(formFieldOfError('PACKAGE_PAYMENT_VALIDATION', 'requestId'), isNull);
      expect(formFieldOfError('PACKAGE_PAYMENT_VALIDATION', null), isNull);
      expect(formFieldOfError('ATTACHMENT_TOO_LARGE', null), 'slip');
      expect(formFieldOfError('ATTACHMENT_TYPE_INVALID', null), 'slip');
      expect(formFieldOfError('RATE_LIMITED', 'amount'), isNull);
    });

    test('error ที่ต้องโหลดหน้าใหม่', () {
      expect(errorNeedsRefresh('PACKAGE_PAYMENT_PENDING_EXISTS'), isTrue);
      expect(errorNeedsRefresh('PACKAGE_NOT_RENEWABLE'), isTrue);
      expect(errorNeedsRefresh('PACKAGE_PAYMENT_UNAVAILABLE'), isTrue);
      expect(errorNeedsRefresh('PACKAGE_PAYMENT_NOT_PENDING'), isTrue);
      expect(errorNeedsRefresh('RATE_LIMITED'), isFalse);
      expect(errorNeedsRefresh('NETWORK_ERROR'), isFalse);
    });
  });

  group('PackagePaymentStatusStyle', () {
    test('ครบทุกสถานะของสัญญา · มีไอคอน + ข้อความ + สีต่างกัน', () {
      const statuses = [
        PackagePaymentStatus.pending,
        PackagePaymentStatus.approved,
        PackagePaymentStatus.rejected,
        PackagePaymentStatus.cancelled,
      ];
      expect(PackagePaymentStatusStyle.tokens.toSet(), statuses.toSet());

      final styles = statuses.map(PackagePaymentStatusStyle.of).toList();
      expect(styles.map((s) => s.labelTh), ['รอตรวจ', 'อนุมัติแล้ว', 'ปฏิเสธ', 'ยกเลิก']);
      expect(styles.map((s) => s.icon).toSet(), hasLength(4));
      expect(styles.map((s) => s.bg).toSet(), hasLength(4));
    });

    test('ไม่สนตัวพิมพ์ · ค่าที่ไม่รู้จักได้ fallback ไม่ได้ป้ายของโดเมนอื่น', () {
      expect(PackagePaymentStatusStyle.of(' Approved ').labelTh, 'อนุมัติแล้ว');
      final unknown = PackagePaymentStatusStyle.of('superseded');
      expect(unknown.labelTh, 'ไม่ทราบสถานะ');
      expect(unknown.icon, Icons.help_outline);
    });
  });
}
