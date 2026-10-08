import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/mileage.dart';
import '../../core/tokens.dart';
import '../../models/handover.dart';

/// [BIZ] เลขไมล์ตอนส่งมอบ + นัดครั้งถัดไป (ไมล์ + จำนวนเดือน) — บังคับก่อนเซ็น (`HANDOVER_SERVICE_INFO_REQUIRED`)
/// ผู้ใช้กรอกเดือน ระบบคำนวณวันที่ให้ · ก่อนเซ็นวันที่เป็นพรีวิว server คำนวณใหม่จากวันส่งมอบจริงตอนเซ็น
/// ต้องตรงกับ HandoverServiceInfoSection.tsx ฝั่งเว็บ
class HandoverServiceInfo extends ConsumerStatefulWidget {
  const HandoverServiceInfo({super.key, required this.jobId, required this.handover, required this.onSaved});

  final String jobId;
  final Handover handover;
  final VoidCallback onSaved;

  @override
  ConsumerState<HandoverServiceInfo> createState() => _HandoverServiceInfoState();
}

class _HandoverServiceInfoState extends ConsumerState<HandoverServiceInfo> {
  static const _kmSteps = [5000, 10000];
  static const _monthPresets = [3, 6, 12];

  late final _handoverCtrl = TextEditingController(text: widget.handover.mileageAtHandover?.toString() ?? '');
  late final _nextCtrl = TextEditingController(text: widget.handover.nextServiceMileage?.toString() ?? '');
  late final _monthsCtrl = TextEditingController(text: widget.handover.nextServiceMonths?.toString() ?? '');
  bool _busy = false;

  @override
  void dispose() {
    _handoverCtrl.dispose();
    _nextCtrl.dispose();
    _monthsCtrl.dispose();
    super.dispose();
  }

  int? get _intakeKm => widget.handover.mileageAtIntake;
  int? get _handoverKm => kmInputError(_handoverCtrl.text) == null ? parseKm(_handoverCtrl.text) : null;
  int? get _nextKm => kmInputError(_nextCtrl.text) == null ? parseKm(_nextCtrl.text) : null;
  int? get _months {
    final m = int.tryParse(_monthsCtrl.text.trim());
    return m != null && m >= minServiceMonths && m <= maxServiceMonths ? m : null;
  }

  // มิเรอร์ HandoverService.ValidateServiceInfo — server ตรวจซ้ำเสมอ
  String? get _handoverError {
    final format = kmInputError(_handoverCtrl.text);
    if (format != null) return format;
    final km = _handoverKm;
    if (km != null && _intakeKm != null && km < _intakeKm!) return 'ต้องไม่น้อยกว่าไมล์ขณะรับรถ (${formatKm(_intakeKm)})';
    return null;
  }

  String? get _nextError {
    final format = kmInputError(_nextCtrl.text);
    if (format != null) return format;
    final next = _nextKm;
    final handover = _handoverKm;
    if (next != null && handover != null && next <= handover) return 'ต้องมากกว่าไมล์ตอนส่งมอบ (${formatKm(handover)})';
    return null;
  }

  String? get _monthsError => _monthsCtrl.text.trim().isNotEmpty && _months == null
      ? 'กรอกจำนวนเดือน $minServiceMonths–$maxServiceMonths'
      : null;

  String? get _blockedReason {
    if (_intakeKm == null) return 'งานนี้ยังไม่มีเลขไมล์ขณะรับรถ — บันทึกที่หน้าตรวจสภาพรถขณะรับก่อน';
    if (_handoverKm == null) return _handoverError ?? 'กรอกเลขไมล์ตอนส่งมอบ';
    if (_nextKm == null) return _nextError ?? 'กรอกเลขไมล์ที่นัดครั้งถัดไป';
    if (_months == null) return _monthsError ?? 'เลือกระยะเวลานัดครั้งถัดไป';
    return _handoverError ?? _nextError;
  }

  bool get _dirty =>
      _handoverKm != widget.handover.mileageAtHandover ||
      _nextKm != widget.handover.nextServiceMileage ||
      _months != widget.handover.nextServiceMonths;

  @override
  Widget build(BuildContext context) {
    final h = widget.handover;
    return Container(
      padding: const EdgeInsets.all(T.s16),
      decoration: BoxDecoration(
        color: T.cardBg,
        border: Border.all(color: T.border),
        borderRadius: BorderRadius.circular(T.rCard),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Row(children: [
            Icon(Icons.speed, color: T.navy900),
            SizedBox(width: T.s8),
            Expanded(
              child: Text('เลขไมล์และนัดครั้งถัดไป',
                  style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700, height: 1.5)),
            ),
          ]),
          const SizedBox(height: T.s8),
          _fact('เลขไมล์ขณะรับรถ', _intakeKm == null ? 'ยังไม่ได้บันทึก' : formatKm(_intakeKm)),
          if (h.isLocked) ...[
            _fact('เลขไมล์ตอนส่งมอบ', formatKm(h.mileageAtHandover)),
            _fact('นัดครั้งถัดไป', nextServiceText(h.nextServiceMileage, h.nextServiceDueOn)),
          ] else
            ..._form(),
        ],
      ),
    );
  }

  List<Widget> _form() {
    final months = _months;
    final blocked = _blockedReason;
    return [
      const SizedBox(height: T.s12),
      _field(_handoverCtrl, 'เลขไมล์ตอนส่งมอบ (กม.) *', _handoverError),
      const SizedBox(height: T.s12),
      _field(_nextCtrl, 'นัดครั้งถัดไปที่เลขไมล์ (กม.) *', _nextError),
      const SizedBox(height: T.s8),
      Wrap(spacing: T.s8, children: [
        for (final step in _kmSteps)
          ActionChip(
            label: Text('+${formatKm(step)}', style: const TextStyle(fontSize: 15)),
            onPressed: _handoverKm == null
                ? null
                : () => setState(() => _nextCtrl.text = '${_handoverKm! + step}'),
          ),
      ]),
      const SizedBox(height: T.s12),
      _field(_monthsCtrl, 'นัดครั้งถัดไปอีก (เดือน) *', _monthsError),
      const SizedBox(height: T.s8),
      Wrap(spacing: T.s8, children: [
        for (final m in _monthPresets)
          ChoiceChip(
            label: Text('$m เดือน', style: const TextStyle(fontSize: 15)),
            selected: months == m,
            onSelected: (_) => setState(() => _monthsCtrl.text = '$m'),
          ),
      ]),
      if (months != null) ...[
        const SizedBox(height: T.s12),
        Row(children: [
          const Icon(Icons.event_outlined, size: 20, color: T.navy900),
          const SizedBox(width: T.s8),
          Expanded(
            child: Text(
              'นัดครั้งถัดไป: ${nextServiceText(_nextKm, addMonthsClamped(DateTime.now(), months))}',
              style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600, height: 1.6),
            ),
          ),
        ]),
        const Text('วันที่นับจากวันที่ยืนยันส่งมอบจริง',
            style: TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
      ],
      const SizedBox(height: T.s12),
      if (blocked != null || !_dirty)
        Padding(
          padding: const EdgeInsets.only(bottom: T.s8),
          child: Text(blocked ?? 'บันทึกแล้ว',
              style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
        ),
      SizedBox(
        width: double.infinity,
        height: T.touchMin,
        child: FilledButton.icon(
          onPressed: _busy || blocked != null || !_dirty ? null : _save,
          icon: const Icon(Icons.save_outlined),
          label: Text(_busy ? 'กำลังบันทึก…' : 'บันทึกเลขไมล์และนัดครั้งถัดไป',
              style: const TextStyle(fontSize: 16)),
        ),
      ),
    ];
  }

  Widget _field(TextEditingController ctrl, String label, String? error) => TextField(
        controller: ctrl,
        keyboardType: TextInputType.number,
        onChanged: (_) => setState(() {}),
        style: const TextStyle(fontSize: 16, height: 1.6),
        decoration: InputDecoration(
          labelText: label,
          errorText: error,
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
        ),
      );

  Widget _fact(String label, String value) => Padding(
        padding: const EdgeInsets.only(top: T.s4),
        child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
          SizedBox(width: 140, child: Text(label, style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6))),
          Expanded(child: Text(value, style: const TextStyle(fontSize: 15, fontWeight: FontWeight.w600, height: 1.6))),
        ]),
      );

  Future<void> _save() async {
    setState(() => _busy = true);
    try {
      await ref.read(handoverApiProvider).saveServiceInfo(
            widget.jobId,
            mileageAtHandover: _handoverKm!,
            nextServiceMileage: _nextKm!,
            nextServiceMonths: _months!,
          );
      widget.onSaved();
    } on ApiException catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(SnackBar(
          content: Text(e.traceId == null ? e.messageTh : '${e.messageTh}\nรหัสอ้างอิง ${e.traceId}',
              style: const TextStyle(fontSize: 15, height: 1.6)),
          backgroundColor: T.navy900,
          behavior: SnackBarBehavior.floating,
        ));
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }
}
