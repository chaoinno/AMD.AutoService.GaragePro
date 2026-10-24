import 'package:flutter_test/flutter_test.dart';
import 'package:garage_pro_service_ops/core/service_items.dart';
import 'package:garage_pro_service_ops/models/handover.dart';
import 'package:garage_pro_service_ops/models/service_reminder.dart';

const _oil = ServiceReminderTemplate(
    id: 't-oil', name: 'น้ำมันเครื่อง', defaultMileageInterval: 10000, defaultMonths: 6);
const _gear = ServiceReminderTemplate(
    id: 't-gear', name: 'น้ำมันเกียร์', defaultMileageInterval: 40000, defaultMonths: 24);

void main() {
  test('a template row follows the handover mileage until the user edits the mileage', () {
    final d = ServiceItemDraft.fromTemplate(_oil);
    expect(serviceItemMileageText(d, 45230), '55230');
    expect(serviceItemMileageText(d, 46000), '56000');
    expect(serviceItemMileageText(d, null), '');
    d
      ..mileageText = '60000'
      ..autoInterval = null;
    expect(serviceItemMileageText(d, 46000), '60000');
  });

  test('saved rows keep their stored mileage (not an auto sum)', () {
    final d = ServiceItemDraft.fromSaved(const HandoverServiceItem(
        id: 'a', templateId: 't-oil', name: 'น้ำมันเครื่อง', nextServiceMileage: 52000, nextServiceMonths: 6));
    expect(serviceItemMileageText(d, 45230), '52000');
    expect(d.customName, isFalse);
    expect(ServiceItemDraft.fromSaved(const HandoverServiceItem(
            id: 'b', name: 'ลูกปืน', nextServiceMileage: 1, nextServiceMonths: 1)).customName,
        isTrue);
  });

  test('validate: name required and unique (trimmed, case-insensitive), mileage above handover, months 1-24', () {
    final a = ServiceItemDraft.fromTemplate(_oil);
    final b = ServiceItemDraft.custom()..name = ' น้ำมันเครื่อง ';
    final all = [a, b];
    expect(validateServiceItem(a, all, 45000).name, 'ชื่อรายการซ้ำกัน');
    expect(validateServiceItem(b, all, 45000).name, 'ชื่อรายการซ้ำกัน');
    final blank = ServiceItemDraft.custom();
    expect(validateServiceItem(blank, [blank], 45000).name, 'กรอกชื่อรายการ');

    final low = ServiceItemDraft.custom()
      ..name = 'x'
      ..mileageText = '45000'
      ..autoInterval = null;
    expect(validateServiceItem(low, [low], 45000).mileage, contains('มากกว่าไมล์ตอนส่งมอบ'));
    low.mileageText = '45001';
    expect(validateServiceItem(low, [low], 45000).mileage, isNull);

    a.monthsText = '0';
    expect(validateServiceItem(a, [a], 45000).months, contains('1–24'));
    a.monthsText = '25';
    expect(validateServiceItem(a, [a], 45000).months, contains('1–24'));
    a.monthsText = '24';
    expect(validateServiceItem(a, [a], 45000).months, isNull);
  });

  test('blocked reason points at what the user can fix, in order', () {
    final ok = ServiceItemDraft.fromTemplate(_oil);
    String? reason({int? intake = 40000, int? handover = 45000, String? hErr, List<ServiceItemDraft>? items}) =>
        serviceInfoBlockedReason(intakeKm: intake, handoverKm: handover, handoverError: hErr, items: items ?? [ok]);

    expect(reason(intake: null), contains('ยังไม่มีเลขไมล์ขณะรับรถ'));
    expect(reason(handover: null), 'กรอกเลขไมล์ตอนส่งมอบ');
    expect(reason(hErr: 'ต้องไม่น้อยกว่า'), 'ต้องไม่น้อยกว่า');
    expect(reason(items: []), 'เพิ่มรายการนัดบริการอย่างน้อย 1 รายการ');
    expect(reason(items: [ServiceItemDraft.custom()]), contains('กรอกชื่อรายการ'));
    final noMonths = ServiceItemDraft.fromTemplate(_oil)..monthsText = '';
    expect(reason(items: [noMonths]), contains('เลือกระยะเวลานัด'));
    expect(reason(), isNull);
  });

  test('request body trims names, keeps template id and resolves auto mileage', () {
    final a = ServiceItemDraft.fromTemplate(_gear);
    final b = ServiceItemDraft.custom()
      ..name = ' ลูกปืนล้อ '
      ..mileageText = '70000'
      ..autoInterval = null
      ..monthsText = '12';
    expect(serviceItemsBody([a, b], 45000), [
      {'templateId': 't-gear', 'name': 'น้ำมันเกียร์', 'nextServiceMileage': 85000, 'nextServiceMonths': 24},
      {'templateId': null, 'name': 'ลูกปืนล้อ', 'nextServiceMileage': 70000, 'nextServiceMonths': 12},
    ]);
  });

  test('signature ignores name padding so an untouched form is not dirty', () {
    final saved = serviceInfoSignature(45000, [('น้ำมันเครื่อง', 55000, 6)]);
    expect(serviceInfoSignature(45000, [(' น้ำมันเครื่อง ', 55000, 6)]), saved);
    expect(serviceInfoSignature(45000, [('น้ำมันเครื่อง', 55000, 12)]), isNot(saved));
  });

  test('Handover.fromJson reads serviceItems; old server without them still parses and is incomplete', () {
    final withItems = Handover.fromJson({
      'id': 'h', 'jobId': 'j', 'isLocked': false, 'receiptIssued': true,
      'items': [], 'mileageAtHandover': 45000,
      'nextServiceMileage': 55000, 'nextServiceMonths': 6, 'nextServiceDueOn': '2027-04-25',
      'serviceItems': [
        {'id': 'a', 'templateId': 't-oil', 'name': 'น้ำมันเครื่อง', 'nextServiceMileage': 55000, 'nextServiceMonths': 6, 'nextServiceDueOn': '2027-04-25'},
        {'id': 'b', 'name': 'กรองอากาศ', 'nextServiceMileage': 65000, 'nextServiceMonths': 12, 'nextServiceDueOn': '2027-10-25'},
      ],
    });
    expect(withItems.serviceItems.map((i) => i.name), ['น้ำมันเครื่อง', 'กรองอากาศ']);
    expect(withItems.serviceItems[1].templateId, isNull);
    expect(withItems.serviceItems[0].nextServiceDueOn, DateTime(2027, 4, 25));
    expect(withItems.serviceInfoComplete, isTrue);

    final old = Handover.fromJson({
      'id': 'h', 'jobId': 'j', 'isLocked': false, 'receiptIssued': true,
      'items': [], 'mileageAtHandover': 45000, 'nextServiceMileage': 55000, 'nextServiceMonths': 6,
    });
    expect(old.serviceItems, isEmpty);
    expect(old.serviceInfoComplete, isFalse); // ต้องมีอย่างน้อย 1 รายการ
  });
}
