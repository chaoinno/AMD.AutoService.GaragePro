import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../models/auth.dart';
import '../../widgets/common.dart';

/// เลือกสาขาและกะ — [BIZ] ค่าที่เลือกกำหนดคิวงานและคลังทั้งวัน
/// มีสาขาเดียวจะข้ามไปเลือกกะทันที
class BranchShiftPage extends ConsumerStatefulWidget {
  const BranchShiftPage({super.key, required this.login});

  final LoginResult login;

  @override
  ConsumerState<BranchShiftPage> createState() => _BranchShiftPageState();
}

class _BranchShiftPageState extends ConsumerState<BranchShiftPage> {
  BranchOption? _branch;
  List<ShiftOption>? _shifts;
  ShiftOption? _shift;

  bool _loadingShifts = false;
  bool _submitting = false;
  String? _errorTh;
  String? _errorTrace;
  String _search = '';

  @override
  void initState() {
    super.initState();
    // สาขาเดียวไม่ต้องให้เลือก
    if (widget.login.branches.length == 1) {
      _branch = widget.login.branches.first;
      WidgetsBinding.instance.addPostFrameCallback((_) => _loadShifts(_branch!));
    }
  }

  @override
  Widget build(BuildContext context) {
    final user = widget.login.user;
    final pickingShift = _branch != null;

    return Scaffold(
      backgroundColor: T.pageBg,
      appBar: AppBar(
        backgroundColor: T.navy900,
        foregroundColor: Colors.white,
        title: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('${user.displayName} · ${user.roleLabelTh}',
                style: const TextStyle(fontSize: 12, color: T.faint, height: 1.5)),
            Text(pickingShift ? 'เลือกกะการทำงาน' : 'เลือกสาขา',
                style: const TextStyle(
                    fontSize: 20, fontWeight: FontWeight.w700, height: 1.5)),
          ],
        ),
        leading: pickingShift && widget.login.branches.length > 1
            ? IconButton(
                icon: const Icon(Icons.arrow_back),
                tooltip: 'กลับไปเลือกสาขา',
                onPressed: () => setState(() {
                  _branch = null;
                  _shifts = null;
                  _shift = null;
                  _errorTh = null;
                }),
              )
            : null,
        automaticallyImplyLeading: false,
      ),
      body: SafeArea(
        child: Column(
          children: [
            if (_errorTh != null)
              Padding(
                padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, 0),
                child: InfoBanner(
                  icon: Icons.error_outline,
                  title: 'ทำรายการไม่สำเร็จ',
                  body: _errorTrace == null ? _errorTh : '$_errorTh\nรหัสอ้างอิง $_errorTrace',
                  tone: StateTone.error,
                ),
              ),
            Expanded(child: pickingShift ? _buildShiftList() : _buildBranchList()),
          ],
        ),
      ),
      bottomNavigationBar: pickingShift
          ? StickyActionBar(
              label: _submitting ? 'กำลังเข้าใช้งาน…' : 'เข้าใช้งาน',
              disabledReason: _shift == null ? 'กรุณาเลือกกะก่อนเข้าใช้งาน' : null,
              hint: _shift == null
                  ? null
                  : '${_branch!.name} · ${_shift!.name} ${_shift!.startTime}–${_shift!.endTime}',
              onPressed: _submitting || _shift == null ? null : _openShift,
            )
          : null,
    );
  }

  Widget _buildBranchList() {
    final all = widget.login.branches;
    final query = _search.trim().toLowerCase();
    final branches = query.isEmpty
        ? all
        : all.where((b) => b.name.toLowerCase().contains(query)).toList();

    return Column(
      children: [
        // ผู้ดูแลระบบเข้าได้หลายสิบสาขา — ต้องมีช่องค้นหา
        if (all.length > 8)
          Padding(
            padding: const EdgeInsets.fromLTRB(T.s16, T.s16, T.s16, T.s8),
            child: TextField(
              onChanged: (v) => setState(() => _search = v),
              decoration: InputDecoration(
                hintText: 'ค้นหาสาขา (${all.length} สาขา)',
                prefixIcon: const Icon(Icons.search, size: 20, color: T.muted),
                filled: true,
                fillColor: Colors.white,
                contentPadding: const EdgeInsets.symmetric(horizontal: T.s12, vertical: 14),
                border: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(T.rInput),
                  borderSide: const BorderSide(color: T.borderStrong),
                ),
              ),
              style: const TextStyle(fontSize: 16),
            ),
          ),
        Expanded(
          child: branches.isEmpty
              ? const StateBlock(
                  icon: Icons.search_off,
                  title: 'ไม่พบสาขาที่ค้นหา',
                  body: 'ลองพิมพ์ชื่อสาขาให้สั้นลง',
                )
              : ListView.separated(
                  padding: const EdgeInsets.all(T.s16),
                  itemCount: branches.length,
                  separatorBuilder: (_, _) => const SizedBox(height: T.s8),
                  itemBuilder: (_, i) => _BranchCard(
                    branch: branches[i],
                    onTap: () => _loadShifts(branches[i]),
                  ),
                ),
        ),
      ],
    );
  }

  Widget _buildShiftList() {
    if (_loadingShifts) {
      return const Center(child: CircularProgressIndicator(color: T.blue600));
    }

    final shifts = _shifts ?? const <ShiftOption>[];
    if (shifts.isEmpty) {
      return StateBlock(
        icon: Icons.schedule,
        title: 'สาขานี้ยังไม่มีกะการทำงาน',
        body: 'ติดต่อผู้จัดการสาขาเพื่อตั้งกะก่อนเริ่มใช้งาน',
        actionLabel: 'ลองใหม่',
        onAction: () => _loadShifts(_branch!),
      );
    }

    return ListView(
      padding: const EdgeInsets.all(T.s16),
      children: [
        Container(
          padding: const EdgeInsets.all(T.s12),
          decoration: BoxDecoration(
            color: Colors.white,
            border: Border.all(color: T.border),
            borderRadius: BorderRadius.circular(T.rInput),
          ),
          child: Row(
            children: [
              const Icon(Icons.store_outlined, size: 18, color: T.blue600),
              const SizedBox(width: T.s8),
              Expanded(
                child: Text(_branch!.name,
                    style: const TextStyle(
                        fontSize: 15, fontWeight: FontWeight.w700, color: T.text, height: 1.6)),
              ),
            ],
          ),
        ),
        const SizedBox(height: T.s16),
        for (final shift in shifts)
          Padding(
            padding: const EdgeInsets.only(bottom: T.s8),
            child: _ShiftCard(
              shift: shift,
              selected: _shift?.shiftId == shift.shiftId,
              onTap: () => setState(() => _shift = shift),
            ),
          ),
      ],
    );
  }

  Future<void> _loadShifts(BranchOption branch) async {
    setState(() {
      _branch = branch;
      _loadingShifts = true;
      _errorTh = null;
      _shift = null;
    });

    try {
      // token ขั้นแรกจาก login ใช้เรียก endpoint เลือกสาขา/กะได้
      final api = GarageProApi(accessToken: widget.login.accessToken);
      final shifts = await api.getShifts(branch.branchId);

      if (!mounted) return;
      setState(() {
        _shifts = shifts;
        // เลือกกะที่ตรงกับเวลาปัจจุบันให้อัตโนมัติ
        _shift = shifts.where((s) => s.isCurrent).firstOrNull;
      });
    } on ApiException catch (e) {
      if (mounted) {
        setState(() {
          _errorTh = e.messageTh;
          _errorTrace = e.traceId;
          _shifts = const [];
        });
      }
    } finally {
      if (mounted) setState(() => _loadingShifts = false);
    }
  }

  Future<void> _openShift() async {
    setState(() {
      _submitting = true;
      _errorTh = null;
    });

    try {
      final api = GarageProApi(accessToken: widget.login.accessToken);
      final session = await api.openShift(
        branchId: _branch!.branchId,
        shiftId: _shift!.shiftId,
      );

      await ref.read(sessionProvider.notifier).save(session);

      // เซสชันพร้อมแล้ว — AuthGate จะพาเข้าแอปเอง
      if (mounted) Navigator.of(context).popUntil((route) => route.isFirst);
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
}

class _BranchCard extends StatelessWidget {
  const _BranchCard({required this.branch, required this.onTap});

  final BranchOption branch;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Material(
        color: Colors.white,
        borderRadius: BorderRadius.circular(T.rCard),
        child: InkWell(
          borderRadius: BorderRadius.circular(T.rCard),
          onTap: onTap,
          child: Container(
            padding: const EdgeInsets.all(T.s16),
            decoration: BoxDecoration(
              border: Border.all(color: T.border),
              borderRadius: BorderRadius.circular(T.rCard),
            ),
            child: Row(
              children: [
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(branch.name,
                          style: const TextStyle(
                              fontSize: 16,
                              fontWeight: FontWeight.w700,
                              color: T.text,
                              height: 1.5)),
                      if (branch.address != null && branch.address!.trim().isNotEmpty)
                        Text(branch.address!,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: const TextStyle(fontSize: 13, color: T.muted, height: 1.6)),
                      const SizedBox(height: 6),
                      Wrap(
                        spacing: T.s8,
                        children: [
                          _Pill(
                            label: 'รอเสนอราคา ${branch.pendingQuotationCount}',
                            bg: const Color(0xFFE8EDF4),
                            fg: T.navy700,
                          ),
                          _Pill(
                            label: 'รออนุมัติ ${branch.waitingApprovalCount}',
                            bg: const Color(0xFFFDF3E2),
                            fg: const Color(0xFF8A5A00),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
                const Icon(Icons.chevron_right, color: T.faint),
              ],
            ),
          ),
        ),
      );
}

class _ShiftCard extends StatelessWidget {
  const _ShiftCard({required this.shift, required this.selected, required this.onTap});

  final ShiftOption shift;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
        button: true,
        selected: selected,
        child: Material(
          color: selected ? const Color(0xFFE6EFFC) : Colors.white,
          borderRadius: BorderRadius.circular(T.rCard),
          child: InkWell(
            borderRadius: BorderRadius.circular(T.rCard),
            onTap: onTap,
            child: Container(
              padding: const EdgeInsets.all(T.s16),
              decoration: BoxDecoration(
                border: Border.all(
                    color: selected ? T.blue600 : T.border, width: selected ? 1.5 : 1),
                borderRadius: BorderRadius.circular(T.rCard),
              ),
              child: Row(
                children: [
                  Icon(selected ? Icons.radio_button_checked : Icons.radio_button_unchecked,
                      color: selected ? T.blue600 : T.faint, size: 22),
                  const SizedBox(width: T.s12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Text(shift.name,
                                style: const TextStyle(
                                    fontSize: 17,
                                    fontWeight: FontWeight.w700,
                                    color: T.text,
                                    height: 1.5)),
                            if (shift.isCurrent) ...[
                              const SizedBox(width: T.s8),
                              const _Pill(
                                label: 'กะปัจจุบัน',
                                bg: Color(0xFFE3F5F1),
                                fg: Color(0xFF0B6D5E),
                              ),
                            ],
                          ],
                        ),
                        Text(
                          '${shift.startTime} – ${shift.endTime}'
                          '${shift.supervisorName != null ? ' · หัวหน้ากะ ${shift.supervisorName}' : ''}',
                          style: const TextStyle(fontSize: 14, color: T.muted, height: 1.6),
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      );
}

class _Pill extends StatelessWidget {
  const _Pill({required this.label, required this.bg, required this.fg});

  final String label;
  final Color bg;
  final Color fg;

  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
        decoration: BoxDecoration(color: bg, borderRadius: BorderRadius.circular(T.rChip)),
        child: Text(label,
            style: TextStyle(
                fontSize: 12, fontWeight: FontWeight.w700, color: fg, height: 1.5)),
      );
}
