import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../../api/client.dart';
import '../../../core/format.dart';
import '../../../core/job_schedule.dart';
import '../../../core/mileage.dart';
import '../../../core/tokens.dart';
import '../../../models/job.dart';
import '../../../widgets/date_time_wheel_sheet.dart';

/// รถนัดหมายมาถึงอู่แล้ว → เปลี่ยนเป็นรถในอู่ (กฎข้อ 17) — คืน [Job] ที่อัปเดตแล้ว หรือ null ถ้ายกเลิก
///
/// [BIZ] ไม่ผูกกับวันนัด (มาก่อน/หลังนัดก็แปลงได้) · เวลาที่รถเข้าห้ามเป็นอนาคต · ต้องมีเลขไมล์
/// เพราะรถอยู่ที่อู่จริงแล้ว (กฎข้อ 22) — เติมค่าที่บันทึกไว้ให้ถ้ามี
Future<Job?> showConvertToInShopSheet(BuildContext context, Job job) =>
    showModalBottomSheet<Job>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      backgroundColor: T.cardBg,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
      ),
      builder: (_) => _ConvertToInShopSheet(job: job),
    );

class _ConvertToInShopSheet extends ConsumerStatefulWidget {
  const _ConvertToInShopSheet({required this.job});

  final Job job;

  @override
  ConsumerState<_ConvertToInShopSheet> createState() => _ConvertToInShopSheetState();
}

class _ConvertToInShopSheetState extends ConsumerState<_ConvertToInShopSheet> {
  late DateTime _arrivedAt = DateTime.now();
  late final _mileageCtrl = TextEditingController(
    text: widget.job.mileageAtIntake?.toString() ?? '',
  );
  bool _busy = false;
  ApiException? _error;

  @override
  void dispose() {
    _mileageCtrl.dispose();
    super.dispose();
  }

  String? get _blockedReason {
    final arrival = actualArrivalError(_arrivedAt, now: DateTime.now());
    if (arrival != null) return arrival;
    if (_mileageCtrl.text.trim().isEmpty) return 'กรอกเลขไมล์ขณะรับรถ';
    return kmInputError(_mileageCtrl.text);
  }

  Future<void> _pickArrival() async {
    final now = DateTime.now();
    final picked = await showDateTimeWheel(
      context,
      title: 'วันเวลาที่รถเข้าอู่จริง',
      initial: _arrivedAt,
      // รถอาจมาถึงตั้งแต่เมื่อวานแล้วเพิ่งมีคนบันทึก — เปิดย้อนให้ 30 วัน ไม่ใช่ย้อนได้ไม่จำกัด
      firstDay: DateTime(now.year, now.month, now.day - 30),
      lastDay: now,
      validate: (v) => actualArrivalError(v, now: DateTime.now()),
    );
    if (picked != null && mounted) setState(() => _arrivedAt = picked);
  }

  Future<void> _submit() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      final updated = await ref.read(jobsApiProvider).convertToInShop(
            widget.job.jobId,
            actualArrivalAt: _arrivedAt,
            mileageAtIntake: parseKm(_mileageCtrl.text),
          );
      if (mounted) Navigator.of(context).pop(updated);
    } on ApiException catch (e) {
      if (mounted) setState(() => _error = e);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final appointmentAt = widget.job.appointmentAt;
    final blocked = _blockedReason;

    return Padding(
      padding: EdgeInsets.fromLTRB(
          T.s16, T.s16, T.s16, MediaQuery.of(context).viewInsets.bottom + T.s16),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            const Text('รถมาถึงอู่แล้ว',
                style: TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
            const SizedBox(height: T.s4),
            Text(
              appointmentAt == null
                  ? 'เปลี่ยนงานนี้เป็น "รถในอู่" และบันทึกเวลาที่รถเข้าจริง'
                  : 'นัดไว้ ${fullDateTime(appointmentAt)} น. — มาก่อนหรือหลังนัดก็บันทึกได้ '
                      'วันนัดเดิมยังเก็บไว้เป็นประวัติ',
              style: const TextStyle(fontSize: 14, color: T.muted, height: 1.7),
            ),
            const SizedBox(height: T.s16),
            InkWell(
              onTap: _busy ? null : _pickArrival,
              borderRadius: BorderRadius.circular(T.rInput),
              child: InputDecorator(
                decoration: InputDecoration(
                  labelText: 'วันเวลาที่รถเข้าอู่จริง *',
                  floatingLabelBehavior: FloatingLabelBehavior.always,
                  prefixIcon: const Icon(Icons.login),
                  suffixIcon: const Icon(Icons.expand_more),
                  errorText: actualArrivalError(_arrivedAt, now: DateTime.now()),
                  border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
                ),
                child: Text('${fullDateTime(_arrivedAt)} น.',
                    style: const TextStyle(fontSize: 16, height: 1.6)),
              ),
            ),
            const SizedBox(height: T.s16),
            TextField(
              controller: _mileageCtrl,
              enabled: !_busy,
              keyboardType: TextInputType.number,
              onChanged: (_) => setState(() {}),
              style: const TextStyle(fontSize: 16, height: 1.6),
              decoration: InputDecoration(
                labelText: 'เลขไมล์ขณะรับรถ (กม.) *',
                hintText: 'เช่น 45210',
                errorText: kmInputError(_mileageCtrl.text),
                prefixIcon: const Icon(Icons.speed),
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(T.rInput)),
              ),
            ),
            if (_error != null) ...[
              const SizedBox(height: T.s12),
              Text(
                _error!.traceId == null
                    ? _error!.messageTh
                    : '${_error!.messageTh}\nรหัสอ้างอิง ${_error!.traceId}',
                style: const TextStyle(fontSize: 14, height: 1.6, color: T.red600),
              ),
            ],
            if (blocked != null) ...[
              const SizedBox(height: T.s12),
              Text(blocked, style: const TextStyle(fontSize: 14, height: 1.6, color: T.muted)),
            ],
            const SizedBox(height: T.s12),
            SizedBox(
              height: T.ctaHeight,
              child: FilledButton(
                onPressed: _busy || blocked != null ? null : _submit,
                style: FilledButton.styleFrom(
                  backgroundColor: T.blue600,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(T.rCard)),
                ),
                child: Text(_busy ? 'กำลังบันทึก…' : 'ยืนยัน เปลี่ยนเป็นรถในอู่',
                    style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
