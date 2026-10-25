import '../models/handover.dart';
import '../models/service_reminder.dart';
import 'mileage.dart';

/// นัดเข้ารับบริการครั้งถัดไปได้หลายรายการ (น้ำมันเครื่อง/กรอง/เบรก…) — ตรรกะฟอร์มฝั่งมือถือ
/// ต้องตรงกับ `handoverServiceRows.ts` ฝั่งเว็บ และ `HandoverService.ValidateServiceInfo` ฝั่ง backend
/// (server ตรวจซ้ำเสมอ — ที่นี่แค่ให้ผู้ใช้รู้ก่อนกดบันทึก)

/// [ASSUME] ต้องตรงกับ `ServiceSchedule.MaxItemsPerHandover/ItemNameMaxLength`
const maxServiceItems = 10;
const serviceItemNameMax = 100;

/// รายการที่พิมพ์เอง เริ่มที่ห่างจากไมล์ส่งมอบ 10,000 กม. / 6 เดือน — แก้ได้ทุกช่อง
const customItemIntervalKm = 10000;
const customItemMonths = 6;

int _seq = 0;

/// หนึ่งแถวในฟอร์มนัดบริการ · [autoInterval] != null = ไมล์นัดตามไมล์ส่งมอบ + ระยะนี้อัตโนมัติ
/// (แถวที่เพิ่งเลือกจากรายการตั้งต้น) จนกว่าผู้ใช้จะแก้ช่องไมล์เอง
class ServiceItemDraft {
  ServiceItemDraft({
    required this.templateId,
    required this.name,
    required this.customName,
    required this.mileageText,
    required this.monthsText,
    required this.autoInterval,
  }) : key = 'item-${++_seq}';

  factory ServiceItemDraft.fromTemplate(ServiceReminderTemplate t) => ServiceItemDraft(
        templateId: t.id,
        name: t.name,
        customName: false,
        mileageText: '',
        monthsText: '${t.defaultMonths}',
        autoInterval: t.defaultMileageInterval,
      );

  factory ServiceItemDraft.custom() => ServiceItemDraft(
        templateId: null,
        name: '',
        customName: true,
        mileageText: '',
        monthsText: '$customItemMonths',
        autoInterval: customItemIntervalKm,
      );

  /// แถวที่บันทึกไว้แล้ว: ไมล์ที่เก็บคือค่าจริง ไม่ใช่ผลรวมอัตโนมัติ
  factory ServiceItemDraft.fromSaved(HandoverServiceItem i) => ServiceItemDraft(
        templateId: i.templateId,
        name: i.name,
        customName: i.templateId == null,
        mileageText: '${i.nextServiceMileage}',
        monthsText: '${i.nextServiceMonths}',
        autoInterval: null,
      );

  final String key;
  String? templateId;
  String name;
  bool customName;
  String mileageText;
  String monthsText;
  int? autoInterval;
}

String serviceItemMileageText(ServiceItemDraft d, int? handoverKm) =>
    d.autoInterval != null && handoverKm != null ? '${handoverKm + d.autoInterval!}' : d.mileageText;

int? serviceItemMileageKm(ServiceItemDraft d, int? handoverKm) {
  final text = serviceItemMileageText(d, handoverKm);
  return kmInputError(text) == null ? parseKm(text) : null;
}

int? parseServiceMonths(String text) {
  final t = text.trim();
  return RegExp(r'^\d+$').hasMatch(t) ? int.tryParse(t) : null;
}

String _nameKey(String name) => name.trim().toLowerCase();

class ServiceItemErrors {
  const ServiceItemErrors({this.name, this.mileage, this.months});
  final String? name;
  final String? mileage;
  final String? months;
}

/// มิเรอร์ ValidateServiceInfo ต่อแถว: ชื่อไม่ว่าง/ไม่ซ้ำ · ไมล์นัด > ไมล์ส่งมอบ · เดือน 1–24
ServiceItemErrors validateServiceItem(ServiceItemDraft d, List<ServiceItemDraft> all, int? handoverKm) {
  String? nameError;
  final name = d.name.trim();
  if (name.isEmpty) {
    nameError = 'กรอกชื่อรายการ';
  } else if (name.length > serviceItemNameMax) {
    nameError = 'ชื่อยาวไม่เกิน $serviceItemNameMax ตัวอักษร';
  } else if (all.any((o) => o.key != d.key && _nameKey(o.name) == _nameKey(name))) {
    nameError = 'ชื่อรายการซ้ำกัน';
  }

  String? mileageError = kmInputError(serviceItemMileageText(d, handoverKm));
  final km = serviceItemMileageKm(d, handoverKm);
  if (mileageError == null && km != null && handoverKm != null && km <= handoverKm) {
    mileageError = 'ต้องมากกว่าไมล์ตอนส่งมอบ (${formatKm(handoverKm)})';
  }

  final months = parseServiceMonths(d.monthsText);
  final monthsError = d.monthsText.trim().isNotEmpty && (months == null || months < minServiceMonths || months > maxServiceMonths)
      ? 'กรอกจำนวนเดือน $minServiceMonths–$maxServiceMonths'
      : null;

  return ServiceItemErrors(name: nameError, mileage: mileageError, months: monthsError);
}

/// เหตุผลที่ปุ่มบันทึกกดไม่ได้ (null = บันทึกได้) — เรียงจากสิ่งที่ผู้ใช้แก้เองตรงนี้ไม่ได้ไปหาสิ่งที่แก้ได้
String? serviceInfoBlockedReason({
  required int? intakeKm,
  required int? handoverKm,
  required String? handoverError,
  required List<ServiceItemDraft> items,
}) {
  if (intakeKm == null) return 'งานนี้ยังไม่มีเลขไมล์ขณะรับรถ บันทึกที่หน้าตรวจสภาพรถขณะรับก่อน';
  if (handoverKm == null) return handoverError ?? 'กรอกเลขไมล์ตอนส่งมอบ';
  if (handoverError != null) return handoverError;
  if (items.isEmpty) return 'เพิ่มรายการนัดบริการอย่างน้อย 1 รายการ';
  for (final d in items) {
    final label = d.name.trim().isEmpty ? 'รายการใหม่' : d.name.trim();
    final e = validateServiceItem(d, items, handoverKm);
    if (e.name != null) return '$label: ${e.name}';
    if (e.mileage != null) return '$label: ${e.mileage}';
    if (serviceItemMileageKm(d, handoverKm) == null) return '$label: กรอกเลขไมล์ที่นัด';
    if (e.months != null) return '$label: ${e.months}';
    if (parseServiceMonths(d.monthsText) == null) return '$label: เลือกระยะเวลานัด (เดือน)';
  }
  return null;
}

/// ข้อมูลที่จะส่ง `PUT .../handover/service-info`
List<Map<String, dynamic>> serviceItemsBody(List<ServiceItemDraft> items, int handoverKm) => [
      for (final d in items)
        {
          'templateId': d.templateId,
          'name': d.name.trim(),
          'nextServiceMileage': serviceItemMileageKm(d, handoverKm),
          'nextServiceMonths': parseServiceMonths(d.monthsText),
        },
    ];

/// ลายเซ็นของข้อมูลที่จะบันทึก — เทียบกับที่ server เก็บไว้ว่ามีการแก้ไขหรือไม่
String serviceInfoSignature(int? handoverKm, Iterable<(String, int?, int?)> items) =>
    '$handoverKm|${items.map((i) => '${i.$1.trim()}:${i.$2}:${i.$3}').join(';')}';
