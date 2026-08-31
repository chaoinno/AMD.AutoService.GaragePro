import 'package:flutter/material.dart';
import 'package:intl/intl.dart';

import '../api/client.dart';
import '../core/tokens.dart';

final _money = NumberFormat('#,##0.00', 'en_US');
final _int = NumberFormat('#,##0', 'en_US');

/// [UI] ตัวเลขเงินต้อง format 1,234.56 เสมอ และแสดงด้วย T.money (tabular figures)
String money(double v) => _money.format(v);

/// จำนวนที่ไม่ใช่เงิน เช่น จำนวนชิ้นคงเหลือ
String qty(num v) => v % 1 == 0 ? _int.format(v) : _money.format(v);

String dateTimeTh(DateTime dt) => DateFormat('d MMM yyyy · HH:mm น.', 'th').format(dt);

String dateTh(DateTime dt) => DateFormat('d MMM yyyy', 'th').format(dt);

/// เวลาสั้นสำหรับรายการ — วันนี้แสดงเวลา ปีนี้แสดงวัน/เดือน อื่นๆ แสดงปีด้วย
String dateShortTh(DateTime dt) {
  final now = DateTime.now();
  if (dt.year == now.year && dt.month == now.month && dt.day == now.day) {
    return DateFormat('HH:mm น.', 'th').format(dt);
  }
  return dt.year == now.year
      ? DateFormat('d MMM', 'th').format(dt)
      : DateFormat('d MMM yy', 'th').format(dt);
}

/// ป้ายสถานะ — [UI] สี + ไอคอน + ข้อความเสมอ ห้ามใช้สีเดียวสื่อความหมาย
class StatusChip extends StatelessWidget {
  const StatusChip(this.token, {super.key, this.compact = false});

  final String token;
  final bool compact;

  @override
  Widget build(BuildContext context) {
    final s = StatusStyle.of(token);
    return Container(
      padding: EdgeInsets.symmetric(
        horizontal: compact ? T.s8 : T.s12,
        vertical: compact ? 4 : 6,
      ),
      decoration: BoxDecoration(
        color: s.bg,
        borderRadius: BorderRadius.circular(T.rChip),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(s.icon, size: compact ? 13 : 15, color: s.fg),
          const SizedBox(width: 5),
          Text(
            s.labelTh,
            style: TextStyle(
              fontSize: compact ? 12 : 13,
              fontWeight: FontWeight.w700,
              color: s.fg,
              height: 1.6,
            ),
          ),
        ],
      ),
    );
  }
}

/// [UI] ทุก state ต้องมี สาเหตุ + ปุ่มถัดไป + รหัสอ้างอิง
class StateBlock extends StatelessWidget {
  const StateBlock({
    super.key,
    required this.icon,
    required this.title,
    required this.body,
    this.traceId,
    this.actionLabel,
    this.onAction,
    this.tone = StateTone.neutral,
  });

  factory StateBlock.fromError(Object error, {VoidCallback? onRetry}) {
    if (error is ApiException) {
      return StateBlock(
        icon: Icons.error_outline,
        title: 'ทำรายการไม่สำเร็จ',
        body: error.messageTh,
        traceId: error.traceId,
        tone: StateTone.error,
        actionLabel: onRetry == null ? null : 'ลองใหม่',
        onAction: onRetry,
      );
    }
    return StateBlock(
      icon: Icons.error_outline,
      title: 'เกิดข้อผิดพลาด',
      body: '$error',
      tone: StateTone.error,
      actionLabel: onRetry == null ? null : 'ลองใหม่',
      onAction: onRetry,
    );
  }

  final IconData icon;
  final String title;
  final String body;
  final String? traceId;
  final String? actionLabel;
  final VoidCallback? onAction;
  final StateTone tone;

  @override
  Widget build(BuildContext context) {
    final (bg, fg) = switch (tone) {
      StateTone.error => (const Color(0xFFFBE9E9), T.red600),
      StateTone.warn => (const Color(0xFFFDF3E2), const Color(0xFF8A5A00)),
      StateTone.neutral => (const Color(0xFFEEF1F5), T.muted),
    };

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(T.s24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 56,
              height: 56,
              decoration: BoxDecoration(color: bg, shape: BoxShape.circle),
              child: Icon(icon, color: fg, size: 28),
            ),
            const SizedBox(height: T.s16),
            Text(title,
                textAlign: TextAlign.center,
                style: const TextStyle(
                    fontSize: 18, fontWeight: FontWeight.w700, color: T.text, height: 1.6)),
            const SizedBox(height: T.s8),
            Text(body,
                textAlign: TextAlign.center,
                style: const TextStyle(fontSize: 15, color: T.muted, height: 1.7)),
            if (traceId != null) ...[
              const SizedBox(height: T.s12),
              Text('รหัสอ้างอิง $traceId',
                  style: const TextStyle(
                      fontFamily: T.fontMono, fontSize: 12, color: T.faint)),
            ],
            if (actionLabel != null && onAction != null) ...[
              const SizedBox(height: T.s24),
              SizedBox(
                height: T.touchMin,
                child: FilledButton(
                  onPressed: onAction,
                  style: FilledButton.styleFrom(
                    backgroundColor: T.blue600,
                    shape: RoundedRectangleBorder(
                        borderRadius: BorderRadius.circular(T.rInput)),
                    padding: const EdgeInsets.symmetric(horizontal: T.s24),
                  ),
                  child: Text(actionLabel!,
                      style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w700)),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

enum StateTone { neutral, warn, error }

/// แถบล่างตรึง — [UI] ฟอร์มยาวบนมือถือใช้ปุ่มหลักเต็มความกว้าง 54–56px
class StickyActionBar extends StatelessWidget {
  const StickyActionBar({
    super.key,
    required this.label,
    required this.onPressed,
    this.secondaryLabel,
    this.onSecondary,
    this.disabledReason,
    this.hint,
  });

  final String label;
  final VoidCallback? onPressed;
  final String? secondaryLabel;
  final VoidCallback? onSecondary;

  /// [UI] ปุ่มที่ปิดใช้งานต้องบอกเหตุผลเสมอ ห้าม disable เฉยๆ
  final String? disabledReason;
  final String? hint;

  @override
  Widget build(BuildContext context) {
    final enabled = onPressed != null && disabledReason == null;

    return Container(
      padding: EdgeInsets.fromLTRB(
          T.s16, T.s12, T.s16, T.s12 + MediaQuery.of(context).padding.bottom),
      decoration: const BoxDecoration(
        color: Colors.white,
        border: Border(top: BorderSide(color: T.border)),
        boxShadow: [
          BoxShadow(color: Color(0x14000000), blurRadius: 16, offset: Offset(0, -4))
        ],
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          if (disabledReason != null)
            Padding(
              padding: const EdgeInsets.only(bottom: T.s8),
              child: Row(
                children: [
                  const Icon(Icons.info_outline, size: 15, color: Color(0xFF8A5A00)),
                  const SizedBox(width: 6),
                  Expanded(
                    child: Text(disabledReason!,
                        style: const TextStyle(
                            fontSize: 13, color: Color(0xFF8A5A00), height: 1.6)),
                  ),
                ],
              ),
            )
          else if (hint != null)
            Padding(
              padding: const EdgeInsets.only(bottom: T.s8),
              child: Text(hint!,
                  style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
            ),
          Row(
            children: [
              if (secondaryLabel != null) ...[
                Expanded(
                  child: SizedBox(
                    height: T.ctaHeight,
                    child: OutlinedButton(
                      onPressed: onSecondary,
                      style: OutlinedButton.styleFrom(
                        side: const BorderSide(color: T.borderStrong),
                        foregroundColor: T.navy700,
                        shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(T.rCard)),
                      ),
                      child: Text(secondaryLabel!,
                          style: const TextStyle(
                              fontSize: 16, fontWeight: FontWeight.w700)),
                    ),
                  ),
                ),
                const SizedBox(width: T.s12),
              ],
              Expanded(
                flex: secondaryLabel != null ? 2 : 1,
                child: SizedBox(
                  height: T.ctaHeight,
                  child: FilledButton(
                    onPressed: enabled ? onPressed : null,
                    style: FilledButton.styleFrom(
                      backgroundColor: T.blue600,
                      disabledBackgroundColor: const Color(0xFFCBD5E1),
                      shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(T.rCard)),
                    ),
                    child: Text(label,
                        style: const TextStyle(
                            fontSize: 17, fontWeight: FontWeight.w700)),
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

class InfoBanner extends StatelessWidget {
  const InfoBanner({
    super.key,
    required this.icon,
    required this.title,
    this.body,
    required this.tone,
  });

  final IconData icon;
  final String title;
  final String? body;
  final StateTone tone;

  @override
  Widget build(BuildContext context) {
    final (bg, border, fg) = switch (tone) {
      StateTone.error => (const Color(0xFFFEF6F6), const Color(0xFFF0C2C2), const Color(0xFFA31D1D)),
      StateTone.warn => (const Color(0xFFFFFBF3), const Color(0xFFF0D9AC), const Color(0xFF8A5A00)),
      StateTone.neutral => (const Color(0xFFF8FAFC), T.border, T.muted),
    };

    return Container(
      padding: const EdgeInsets.all(T.s12),
      decoration: BoxDecoration(
        color: bg,
        border: Border.all(color: border),
        borderRadius: BorderRadius.circular(T.rInput),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: fg),
          const SizedBox(width: T.s8),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title,
                    style: TextStyle(
                        fontSize: 14, fontWeight: FontWeight.w700, color: fg, height: 1.6)),
                if (body != null) ...[
                  const SizedBox(height: 2),
                  Text(body!,
                      style: TextStyle(fontSize: 13, color: fg, height: 1.65)),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}
