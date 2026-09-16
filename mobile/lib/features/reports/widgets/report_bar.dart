import 'package:flutter/material.dart';

import '../../../core/tokens.dart';
import '../../../widgets/common.dart';

/// แท่งกราฟแนวนอนอย่างง่าย — [UI] ทุกแท่งต้องมีตัวเลขกำกับ ห้ามให้อ่านค่าจากความยาวอย่างเดียว
class ReportBar extends StatelessWidget {
  const ReportBar({
    super.key,
    required this.label,
    required this.value,
    required this.max,
    this.color = T.blue600,
    this.valueLabel,
  });

  final String label;
  final double value;
  final double max;
  final Color color;
  final String? valueLabel;

  @override
  Widget build(BuildContext context) {
    final ratio = max <= 0 ? 0.0 : (value / max).clamp(0.0, 1.0);

    return Padding(
      padding: const EdgeInsets.only(bottom: T.s8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(label,
                    style: const TextStyle(fontSize: 14, height: 1.6),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis),
              ),
              Text(valueLabel ?? money(value),
                  style: const TextStyle(
                      fontFamily: T.fontMono, fontSize: 14, fontWeight: FontWeight.w700)),
            ],
          ),
          const SizedBox(height: 4),
          ClipRRect(
            borderRadius: BorderRadius.circular(T.rChip),
            child: LinearProgressIndicator(
              value: ratio,
              minHeight: 10,
              backgroundColor: const Color(0xFFEEF1F5),
              valueColor: AlwaysStoppedAnimation(color),
            ),
          ),
        ],
      ),
    );
  }
}

class StatTile extends StatelessWidget {
  const StatTile({super.key, required this.label, required this.value, this.tone});

  final String label;
  final String value;
  final Color? tone;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.all(T.s12),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(label,
                style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6),
                maxLines: 2),
            const SizedBox(height: 2),
            Text(value,
                style: TextStyle(
                    fontFamily: T.fontMono,
                    fontSize: 22,
                    fontWeight: FontWeight.w700,
                    color: tone ?? T.text)),
          ],
        ),
      );
}
