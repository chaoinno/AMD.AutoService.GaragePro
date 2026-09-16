import 'package:flutter/material.dart';

import '../../../core/format.dart';
import '../../../core/tokens.dart';
import '../../../models/job.dart';
import '../../../widgets/common.dart';

/// การ์ดจ๊อบในรายการ — [UI] เกินกำหนดใช้แถบซ้าย 5px + ข้อความบอกว่าเกินมานานเท่าไร
/// (ห้ามสื่อความหมายด้วยสีอย่างเดียว)
class JobCard extends StatelessWidget {
  const JobCard({super.key, required this.job, required this.onTap});

  final Job job;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Container(
      margin: const EdgeInsets.only(bottom: T.s12),
      decoration: BoxDecoration(
        color: T.cardBg,
        border: Border.all(color: T.border),
        borderRadius: BorderRadius.circular(T.rCard),
      ),
      clipBehavior: Clip.antiAlias,
      child: Material(
        color: Colors.transparent,
        child: InkWell(
          onTap: onTap,
          child: IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (job.isOverdue) Container(width: 5, color: T.red600),
                Expanded(
                  child: Padding(
                    padding: const EdgeInsets.all(T.s16),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: Text(
                                job.vehicleRegistration,
                                style: const TextStyle(
                                    fontSize: 18, fontWeight: FontWeight.w700, height: 1.4),
                              ),
                            ),
                            JobStatusChip(job.status, label: job.statusLabel, compact: true),
                          ],
                        ),
                        if (job.vehicleModel?.trim().isNotEmpty ?? false)
                          Text(job.vehicleModel!.trim(),
                              style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                        const SizedBox(height: T.s8),
                        Row(
                          children: [
                            const Icon(Icons.person_outline, size: 16, color: T.muted),
                            const SizedBox(width: 6),
                            Expanded(
                              child: Text(job.customerName,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(fontSize: 15, height: 1.6)),
                            ),
                          ],
                        ),
                        const SizedBox(height: 4),
                        Row(
                          children: [
                            const Icon(Icons.tag, size: 16, color: T.muted),
                            const SizedBox(width: 6),
                            Text(job.jobNo,
                                style: const TextStyle(
                                    fontFamily: T.fontMono, fontSize: 14, color: T.muted)),
                            const Spacer(),
                            Text(shortDateTime(job.createdAt),
                                style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6)),
                          ],
                        ),
                        if (job.isOverdue && job.promiseAt != null) ...[
                          const SizedBox(height: T.s8),
                          Row(
                            children: [
                              const Icon(Icons.warning_amber_rounded, size: 16, color: T.red600),
                              const SizedBox(width: 6),
                              Expanded(
                                child: Text(
                                  'เกินเวลานัดส่งมาแล้ว ${relativeSince(job.promiseAt!)}',
                                  style: const TextStyle(
                                      fontSize: 14,
                                      color: T.red600,
                                      fontWeight: FontWeight.w600,
                                      height: 1.6),
                                ),
                              ),
                            ],
                          ),
                        ],
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
