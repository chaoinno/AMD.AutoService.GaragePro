import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:signature/signature.dart';

import '../../api/client.dart';
import '../../models/attachment.dart';
import '../../core/tokens.dart';
import '../../models/quotation.dart';
import '../../widgets/common.dart';

/// ตรวจทานและเซ็นยืนยัน — ขั้นสุดท้ายของการอนุมัติบนมือถือ
///
/// [BIZ] ลายเซ็นผูกกับเวอร์ชันของใบเสนอราคานี้เท่านั้น
/// [UI] ลบลายเซ็นต้องยืนยัน · ออกจากหน้าต้องยืนยัน
class SignaturePage extends ConsumerStatefulWidget {
  const SignaturePage({super.key, required this.quotation});

  final Quotation quotation;

  @override
  ConsumerState<SignaturePage> createState() => _SignaturePageState();
}

class _SignaturePageState extends ConsumerState<SignaturePage> {
  late final SignatureController _controller;
  bool _submitting = false;
  String? _errorTh;
  String? _errorTrace;

  static const _consentText =
      'ข้าพเจ้าได้ตรวจสอบรายการซ่อมและราคาตามใบเสนอราคานี้แล้ว '
      'และยินยอมให้อู่ดำเนินการซ่อมเฉพาะรายการที่ข้าพเจ้าอนุมัติไว้';

  @override
  void initState() {
    super.initState();
    _controller = SignatureController(
      penStrokeWidth: 3,
      penColor: T.navy900,
      exportBackgroundColor: Colors.white,
    );
    _controller.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final q = widget.quotation;
    final approved = q.lines.where((l) => l.isApproved).toList();
    final rejected = q.lines.where((l) => l.isRejected).toList();
    final totals = q.totals.approved;

    return PopScope(
      canPop: _controller.isEmpty && !_submitting,
      onPopInvokedWithResult: (didPop, _) async {
        if (didPop) return;
        final leave = await _confirmLeave();
        if (leave && mounted) {
          Navigator.of(this.context).pop(false);
        }
      },
      child: Scaffold(
        backgroundColor: T.pageBg,
        appBar: AppBar(
          backgroundColor: T.navy900,
          foregroundColor: Colors.white,
          title: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('${q.code} · เวอร์ชัน ${q.version}',
                  style: const TextStyle(fontSize: 12, color: T.faint, height: 1.5)),
              const Text('ตรวจทานและเซ็นยืนยัน',
                  style: TextStyle(
                      fontSize: 19, fontWeight: FontWeight.w700, height: 1.5)),
            ],
          ),
        ),
        body: SafeArea(
          bottom: false,
          child: ListView(
            padding: const EdgeInsets.all(T.s16),
            children: [
              if (_errorTh != null) ...[
                InfoBanner(
                  icon: Icons.error_outline,
                  title: 'บันทึกการอนุมัติไม่สำเร็จ',
                  body: _errorTrace == null
                      ? _errorTh
                      : '$_errorTh\nรหัสอ้างอิง $_errorTrace',
                  tone: StateTone.error,
                ),
                const SizedBox(height: T.s16),
              ],

              _card(
                title: 'รายการที่อนุมัติ (${approved.length})',
                child: Column(
                  children: [
                    for (final l in approved) _reviewRow(l, approved: true),
                  ],
                ),
              ),

              if (rejected.isNotEmpty) ...[
                const SizedBox(height: T.s12),
                _card(
                  title: 'รายการที่ไม่อนุมัติ (${rejected.length})',
                  child: Column(
                    children: [
                      for (final l in rejected) _reviewRow(l, approved: false),
                    ],
                  ),
                ),
              ],

              const SizedBox(height: T.s12),

              // ยอดที่ลูกค้าจะเซ็นยืนยัน
              Container(
                padding: const EdgeInsets.all(T.s16),
                decoration: BoxDecoration(
                  color: T.navy900,
                  borderRadius: BorderRadius.circular(T.rCard),
                ),
                child: Column(
                  children: [
                    _totalRow('ยอดก่อนภาษี', totals?.net ?? 0, faint: true),
                    const SizedBox(height: 6),
                    _totalRow(
                        'ภาษีมูลค่าเพิ่ม ${(q.totals.vatRate * 100).toStringAsFixed(0)}%',
                        totals?.vat ?? 0,
                        faint: true),
                    const Padding(
                      padding: EdgeInsets.symmetric(vertical: T.s12),
                      child: Divider(height: 1, color: Color(0x33FFFFFF)),
                    ),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.spaceBetween,
                      children: [
                        const Text('ยอดที่อนุมัติ',
                            style: TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.w700,
                                color: Colors.white,
                                height: 1.6)),
                        Text('${money(totals?.total ?? 0)} บาท',
                            style: const TextStyle(
                                fontFamily: T.fontMono,
                                fontSize: 22,
                                fontWeight: FontWeight.w700,
                                color: Colors.white)),
                      ],
                    ),
                  ],
                ),
              ),

              const SizedBox(height: T.s16),

              // ข้อความยินยอม — ต้องแสดงเต็มก่อนเซ็น
              Container(
                padding: const EdgeInsets.all(T.s12),
                decoration: BoxDecoration(
                  color: const Color(0xFFFFFBF3),
                  border: Border.all(color: const Color(0xFFF0D9AC)),
                  borderRadius: BorderRadius.circular(T.rInput),
                ),
                child: const Text(_consentText,
                    style: TextStyle(
                        fontSize: 14, color: Color(0xFF8A5A00), height: 1.7)),
              ),

              const SizedBox(height: T.s16),

              Row(
                children: [
                  const Text('ลายเซ็นลูกค้า',
                      style: TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w700,
                          color: T.text,
                          height: 1.6)),
                  const Spacer(),
                  if (_controller.isNotEmpty)
                    TextButton.icon(
                      onPressed: _confirmClear,
                      icon: const Icon(Icons.refresh, size: 17),
                      label: const Text('ลบและเซ็นใหม่',
                          style: TextStyle(fontSize: 14, fontWeight: FontWeight.w700)),
                      style: TextButton.styleFrom(
                        foregroundColor: T.red600,
                        minimumSize: const Size(0, T.touchMin),
                      ),
                    ),
                ],
              ),
              const SizedBox(height: T.s8),
              Container(
                height: 220,
                decoration: BoxDecoration(
                  color: Colors.white,
                  border: Border.all(
                      color: _controller.isNotEmpty ? T.blue600 : T.borderStrong,
                      width: _controller.isNotEmpty ? 1.5 : 1),
                  borderRadius: BorderRadius.circular(T.rCard),
                ),
                child: Stack(
                  children: [
                    ClipRRect(
                      borderRadius: BorderRadius.circular(T.rCard),
                      child: Signature(
                        controller: _controller,
                        backgroundColor: Colors.white,
                        height: 220,
                      ),
                    ),
                    if (_controller.isEmpty)
                      const IgnorePointer(
                        child: Center(
                          child: Column(
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              Icon(Icons.draw_outlined, size: 30, color: Color(0xFFCBD5E1)),
                              SizedBox(height: 6),
                              Text('แตะและลากเพื่อเซ็นชื่อ',
                                  style: TextStyle(
                                      fontSize: 15, color: Color(0xFF94A3B8), height: 1.6)),
                            ],
                          ),
                        ),
                      ),
                  ],
                ),
              ),
              const SizedBox(height: T.s12),
              Text(
                'ลายเซ็นนี้จะผูกกับใบเสนอราคาเวอร์ชันที่ ${q.version} '
                'หากมีการออกฉบับแก้ไข ลายเซ็นนี้จะเป็นโมฆะและต้องเซ็นใหม่',
                style: const TextStyle(fontSize: 13, color: T.muted, height: 1.7),
              ),
              const SizedBox(height: T.s32),
            ],
          ),
        ),
        bottomNavigationBar: StickyActionBar(
          label: _submitting ? 'กำลังบันทึก…' : 'บันทึกการอนุมัติ',
          secondaryLabel: 'กลับไปแก้',
          onSecondary: _submitting ? null : () => Navigator.of(context).pop(false),
          disabledReason: _controller.isEmpty ? 'กรุณาให้ลูกค้าเซ็นชื่อก่อนบันทึก' : null,
          onPressed: _submitting || _controller.isEmpty ? null : _submit,
        ),
      ),
    );
  }

  Widget _card({required String title, required Widget child}) => Container(
        padding: const EdgeInsets.all(T.s16),
        decoration: BoxDecoration(
          color: T.cardBg,
          border: Border.all(color: T.border),
          borderRadius: BorderRadius.circular(T.rCard),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title,
                style: const TextStyle(
                    fontSize: 15, fontWeight: FontWeight.w700, color: T.text, height: 1.6)),
            const SizedBox(height: T.s8),
            child,
          ],
        ),
      );

  Widget _reviewRow(QuotationLine l, {required bool approved}) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 5),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Icon(approved ? Icons.check_circle : Icons.cancel,
                size: 16,
                color: approved ? const Color(0xFF0B6D5E) : const Color(0xFFA31D1D)),
            const SizedBox(width: 7),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(l.name,
                      style: TextStyle(
                        fontSize: 14,
                        height: 1.6,
                        color: approved ? T.text : T.muted,
                        decoration: approved ? null : TextDecoration.lineThrough,
                      )),
                  if (!approved && l.rejectReason != null)
                    Text(l.rejectReason!,
                        style: const TextStyle(
                            fontSize: 12, color: Color(0xFFA31D1D), height: 1.6)),
                ],
              ),
            ),
            const SizedBox(width: T.s8),
            Text(money(l.netAmount),
                style: TextStyle(
                  fontFamily: T.fontMono,
                  fontSize: 14,
                  fontWeight: FontWeight.w600,
                  color: approved ? T.text : T.faint,
                  decoration: approved ? null : TextDecoration.lineThrough,
                )),
          ],
        ),
      );

  Widget _totalRow(String label, double value, {bool faint = false}) => Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Text(label,
              style: TextStyle(
                  fontSize: 14,
                  color: faint ? const Color(0xFFC7D6E8) : Colors.white,
                  height: 1.6)),
          Text(money(value),
              style: TextStyle(
                  fontFamily: T.fontMono,
                  fontSize: 15,
                  color: faint ? const Color(0xFFC7D6E8) : Colors.white)),
        ],
      );

  Future<bool> _confirmLeave() async =>
      await showDialog<bool>(
        context: context,
        builder: (ctx) => AlertDialog(
          title: const Text('ออกจากหน้าเซ็นชื่อ?',
              style: TextStyle(fontSize: 19, fontWeight: FontWeight.w700, height: 1.5)),
          content: const Text('ลายเซ็นที่วาดไว้จะหายไป และยังไม่มีการบันทึกการอนุมัติ',
              style: TextStyle(fontSize: 15, height: 1.7)),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(ctx, false),
              child: const Text('เซ็นต่อ', style: TextStyle(fontSize: 16)),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(ctx, true),
              style: FilledButton.styleFrom(backgroundColor: T.red600),
              child: const Text('ออกและทิ้งลายเซ็น', style: TextStyle(fontSize: 16)),
            ),
          ],
        ),
      ) ??
      false;

  Future<void> _confirmClear() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('ลบลายเซ็น?',
            style: TextStyle(fontSize: 19, fontWeight: FontWeight.w700, height: 1.5)),
        content: const Text('ลายเซ็นปัจจุบันจะถูกลบเพื่อให้เซ็นใหม่',
            style: TextStyle(fontSize: 15, height: 1.7)),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('ยกเลิก', style: TextStyle(fontSize: 16)),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            style: FilledButton.styleFrom(backgroundColor: T.red600),
            child: const Text('ลบ', style: TextStyle(fontSize: 16)),
          ),
        ],
      ),
    );

    if (ok == true) _controller.clear();
  }

  Future<void> _submit() async {
    setState(() {
      _submitting = true;
      _errorTh = null;
      _errorTrace = null;
    });

    try {
      final session = ref.read(sessionProvider);
      if (session == null) {
        throw ApiException('AUTH_REQUIRED', 'เซสชันหมดอายุ — กรุณาเข้าสู่ระบบใหม่');
      }

      // อัปโหลดลายเซ็นขึ้น server ก่อน แล้วเก็บ path ที่ server คืนมา
      // ไม่ใช้ path ในเครื่อง เพราะเว็บต้องดึงรูปนี้ไปแสดงบนเอกสารได้
      final file = await _writeSignatureFile();
      final stored = await ref.read(attachmentsApiProvider).upload(
            file: file,
            jobId: widget.quotation.jobId,
            kind: AttachmentKind.signature,
            entityId: widget.quotation.id,
          );

      await ref.read(quotationsApiProvider).sign(
            widget.quotation.id,
            signatureImagePath: stored.relativePath,
            consentText: _consentText,
            deviceInfo: _deviceInfo(),
            witnessEmployeeId: session.user.userId,
            witnessEmployeeName: session.user.displayName,
          );

      if (mounted) Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _errorTh = e.messageTh;
          _errorTrace = e.traceId;
        });
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  /// เขียนลายเซ็นเป็นไฟล์ชั่วคราวก่อนอัปโหลด
  Future<File> _writeSignatureFile() async {
    final bytes = await _controller.toPngBytes();
    if (bytes == null) throw ApiException('SIGNATURE_EMPTY', 'ยังไม่มีลายเซ็น');

    final file = File('${Directory.systemTemp.path}/'
        'sig-${widget.quotation.jobNo}-v${widget.quotation.version}.png');
    await file.writeAsBytes(bytes);
    return file;
  }

  String _deviceInfo() =>
      '${Platform.operatingSystem} ${Platform.operatingSystemVersion}';
}
