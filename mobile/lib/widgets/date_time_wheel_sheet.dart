import 'package:flutter/cupertino.dart';
import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../core/format.dart';
import '../core/tokens.dart';

/// ตัวเลือกวันเวลาแบบวงล้อ ป้ายภาษาไทยทั้งหมด
///
/// ไม่ใช้ `showDatePicker`/`CupertinoDatePicker` เพราะแอปไม่มี `flutter_localizations` — ตัวเลือกพวกนั้นจะขึ้น
/// ชื่อเดือนภาษาอังกฤษ และเพิ่ม package นี้ไม่ได้: มัน pin `intl 0.20.2` ซึ่งชนกับ `intl: ^0.20.3` ใน pubspec
/// วงล้อเขียนเองจึงใช้แค่ `CupertinoPicker` (ตัวเลข/ข้อความที่เราใส่เอง) ไม่พึ่ง localizations เลย
///
/// [validate] คืนเหตุผลที่ยังยืนยันไม่ได้ (null = ใช้ได้) — แสดงเหนือปุ่มแทนการ disable เฉยๆ
Future<DateTime?> showDateTimeWheel(
  BuildContext context, {
  required String title,
  required DateTime initial,
  required DateTime firstDay,
  required DateTime lastDay,
  String? Function(DateTime value)? validate,
}) =>
    showModalBottomSheet<DateTime>(
      context: context,
      isScrollControlled: true,
      useSafeArea: true,
      backgroundColor: T.cardBg,
      shape: const RoundedRectangleBorder(
        borderRadius: BorderRadius.vertical(top: Radius.circular(T.rCard)),
      ),
      builder: (_) => _DateTimeWheel(
        title: title,
        initial: initial,
        firstDay: firstDay,
        lastDay: lastDay,
        validate: validate,
      ),
    );

/// นาทีในวงล้อเลือกทีละ [minuteStep] — นัดหมายไม่ต้องละเอียดถึงนาที และวงล้อ 60 ช่องหมุนหายาก
const minuteStep = 5;

/// วันทั้งหมดในวงล้อ (เที่ยงคืนของแต่ละวัน) ตั้งแต่ [first] ถึง [last] รวมทั้งสองฝั่ง
List<DateTime> wheelDays(DateTime first, DateTime last) {
  final start = DateTime(first.year, first.month, first.day);
  final end = DateTime(last.year, last.month, last.day);
  // นับด้วย DateTime(y, m, d + i) ไม่ใช่ add(Duration(days: i)) — วันที่เปลี่ยนเวลาออมแสงยาวไม่เท่า 24 ชม.
  return [
    for (var d = start; !d.isAfter(end); d = DateTime(d.year, d.month, d.day + 1)) d,
  ];
}

/// ประกอบวัน + ชั่วโมง + นาทีเป็นเวลาท้องถิ่น
DateTime composeDateTime(DateTime day, int hour, int minute) =>
    DateTime(day.year, day.month, day.day, hour, minute);

/// ช่องนาทีที่ใกล้ [minute] ที่สุดโดยไม่ย้อนกลับ (ปัดขึ้น) — 59 ปัดเป็นช่องสุดท้าย 55
int minuteIndexFor(int minute) => ((minute + minuteStep - 1) ~/ minuteStep).clamp(0, 60 ~/ minuteStep - 1);

class _DateTimeWheel extends StatefulWidget {
  const _DateTimeWheel({
    required this.title,
    required this.initial,
    required this.firstDay,
    required this.lastDay,
    required this.validate,
  });

  final String title;
  final DateTime initial;
  final DateTime firstDay;
  final DateTime lastDay;
  final String? Function(DateTime value)? validate;

  @override
  State<_DateTimeWheel> createState() => _DateTimeWheelState();
}

class _DateTimeWheelState extends State<_DateTimeWheel> {
  static final _dayLabel = DateFormat('EEE d MMM y', 'th');

  late final List<DateTime> _days = wheelDays(widget.firstDay, widget.lastDay);
  late int _dayIndex;
  late int _hour;
  late int _minuteIndex;

  late final FixedExtentScrollController _dayCtrl;
  late final FixedExtentScrollController _hourCtrl;
  late final FixedExtentScrollController _minuteCtrl;

  @override
  void initState() {
    super.initState();
    final initialDay = DateTime(widget.initial.year, widget.initial.month, widget.initial.day);
    _dayIndex = _days.indexWhere((d) => d == initialDay);
    if (_dayIndex < 0) _dayIndex = initialDay.isBefore(_days.first) ? 0 : _days.length - 1;
    _hour = widget.initial.hour;
    _minuteIndex = minuteIndexFor(widget.initial.minute);

    _dayCtrl = FixedExtentScrollController(initialItem: _dayIndex);
    _hourCtrl = FixedExtentScrollController(initialItem: _hour);
    _minuteCtrl = FixedExtentScrollController(initialItem: _minuteIndex);
  }

  @override
  void dispose() {
    _dayCtrl.dispose();
    _hourCtrl.dispose();
    _minuteCtrl.dispose();
    super.dispose();
  }

  DateTime get _value => composeDateTime(_days[_dayIndex], _hour, _minuteIndex * minuteStep);

  String _dayText(int index) {
    final today = DateTime.now();
    final day = _days[index];
    final diff = day.difference(DateTime(today.year, today.month, today.day)).inDays;
    final label = _dayLabel.format(day);
    return switch (diff) {
      0 => 'วันนี้ · $label',
      1 => 'พรุ่งนี้ · $label',
      _ => label,
    };
  }

  @override
  Widget build(BuildContext context) {
    final error = widget.validate?.call(_value);
    final itemExtent = MediaQuery.textScalerOf(context).clamp(maxScaleFactor: 1.6).scale(40);

    return Padding(
      padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s16),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(widget.title,
              style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w700, height: 1.5)),
          const SizedBox(height: T.s4),
          Text('${fullDateTime(_value)} น.',
              style: const TextStyle(fontSize: 16, color: T.blue600, fontWeight: FontWeight.w700)),
          const SizedBox(height: T.s12),
          const Row(
            children: [
              Expanded(flex: 5, child: _ColumnLabel('วัน')),
              Expanded(flex: 2, child: _ColumnLabel('ชั่วโมง')),
              Expanded(flex: 2, child: _ColumnLabel('นาที')),
            ],
          ),
          SizedBox(
            height: itemExtent * 5,
            child: Row(
              children: [
                Expanded(
                  flex: 5,
                  child: CupertinoPicker.builder(
                    scrollController: _dayCtrl,
                    itemExtent: itemExtent,
                    childCount: _days.length,
                    onSelectedItemChanged: (i) => setState(() => _dayIndex = i),
                    itemBuilder: (_, i) => _WheelText(_dayText(i)),
                  ),
                ),
                Expanded(
                  flex: 2,
                  child: CupertinoPicker(
                    scrollController: _hourCtrl,
                    itemExtent: itemExtent,
                    looping: true,
                    onSelectedItemChanged: (i) => setState(() => _hour = i),
                    children: [for (var h = 0; h < 24; h++) _WheelText(h.toString().padLeft(2, '0'))],
                  ),
                ),
                Expanded(
                  flex: 2,
                  child: CupertinoPicker(
                    scrollController: _minuteCtrl,
                    itemExtent: itemExtent,
                    looping: true,
                    onSelectedItemChanged: (i) => setState(() => _minuteIndex = i),
                    children: [
                      for (var m = 0; m < 60; m += minuteStep)
                        _WheelText(m.toString().padLeft(2, '0')),
                    ],
                  ),
                ),
              ],
            ),
          ),
          if (error != null) ...[
            const SizedBox(height: T.s8),
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                const Icon(Icons.info_outline, size: 18, color: T.red600),
                const SizedBox(width: T.s8),
                Expanded(
                  child: Text(error,
                      style: const TextStyle(fontSize: 14, height: 1.6, color: T.red600)),
                ),
              ],
            ),
          ],
          const SizedBox(height: T.s12),
          Row(
            children: [
              Expanded(
                child: SizedBox(
                  height: T.touchMin,
                  child: OutlinedButton(
                    onPressed: () => Navigator.of(context).pop(),
                    child: const Text('ยกเลิก', style: TextStyle(fontSize: 16)),
                  ),
                ),
              ),
              const SizedBox(width: T.s12),
              Expanded(
                child: SizedBox(
                  height: T.touchMin,
                  child: FilledButton(
                    onPressed: error == null ? () => Navigator.of(context).pop(_value) : null,
                    style: FilledButton.styleFrom(backgroundColor: T.blue600),
                    child: const Text('ยืนยัน',
                        style: TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
                  ),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

class _ColumnLabel extends StatelessWidget {
  const _ColumnLabel(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Text(text,
      textAlign: TextAlign.center, style: const TextStyle(fontSize: 13, color: T.muted));
}

class _WheelText extends StatelessWidget {
  const _WheelText(this.text);
  final String text;

  @override
  Widget build(BuildContext context) => Center(
        child: Text(
          text,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: const TextStyle(fontSize: 16, color: T.text, fontFeatures: [FontFeature.tabularFigures()]),
        ),
      );
}
