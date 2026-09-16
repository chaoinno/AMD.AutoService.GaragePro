import 'package:flutter/material.dart';

import '../../../core/tokens.dart';

/// แถบขั้นตอน 6 ขั้นเหมือนการ์ดจ๊อบบนเว็บ — บนมือถือใช้เป็น "ตัวบอกตำแหน่ง" ที่เลื่อนแนวนอนได้
/// ไม่ใช่ตัวสลับหน้า เพราะจอ 390px ถ้าสลับเนื้อหาจะดันข้อมูลรถ/ลูกค้าออกนอกจอทันที
class JobStageStrip extends StatelessWidget {
  const JobStageStrip({super.key, required this.status});

  final String status;

  static const stages = <({String label, IconData icon})>[
    (label: 'รับรถ', icon: Icons.directions_car_outlined),
    (label: 'ตรวจสอบ', icon: Icons.fact_check_outlined),
    (label: 'เสนอราคา', icon: Icons.request_quote_outlined),
    (label: 'ซ่อม', icon: Icons.build_outlined),
    (label: 'QC', icon: Icons.verified_outlined),
    (label: 'ชำระ/ส่งมอบ', icon: Icons.local_shipping_outlined),
  ];

  /// ขั้นตอนปัจจุบันตามสถานะจ๊อบ — ให้ตรงกับ STAGE_INDEX_BY_STATUS ของเว็บ
  static int stageIndexOf(String status) => switch (status) {
        'waitinspect' => 1,
        'waitquote' || 'waitapprove' || 'approved' => 2,
        'inprogress' || 'waitparts' => 3,
        'qc' => 4,
        'ready' || 'completed' || 'cancelled' => 5,
        _ => 0,
      };

  @override
  Widget build(BuildContext context) {
    final current = stageIndexOf(status);

    return SizedBox(
      height: 68,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.symmetric(horizontal: T.s16),
        itemCount: stages.length,
        separatorBuilder: (_, _) => const SizedBox(width: T.s8),
        itemBuilder: (context, i) {
          final stage = stages[i];
          final done = i < current;
          final active = i == current;

          final (bg, fg) = active
              ? (T.blue50, T.blue600)
              : done
                  ? (const Color(0xFFE3F5F1), const Color(0xFF0B6D5E))
                  : (const Color(0xFFEEF1F5), T.muted);

          return Container(
            padding: const EdgeInsets.symmetric(horizontal: T.s12, vertical: T.s8),
            decoration: BoxDecoration(
              color: bg,
              borderRadius: BorderRadius.circular(T.rChip),
              border: Border.all(color: active ? T.blue600 : Colors.transparent),
            ),
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                // [UI] สื่อด้วยสี + ไอคอน + ข้อความ — ขั้นที่ผ่านแล้วใช้เครื่องหมายถูก
                Icon(done ? Icons.check_circle : stage.icon, size: 18, color: fg),
                const SizedBox(height: 4),
                Text('${i + 1}. ${stage.label}',
                    style: TextStyle(
                        fontSize: 13,
                        fontWeight: active ? FontWeight.w700 : FontWeight.w500,
                        color: fg,
                        height: 1.4)),
              ],
            ),
          );
        },
      ),
    );
  }
}
