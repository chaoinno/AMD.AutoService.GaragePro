import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../core/tokens.dart';
import '../../widgets/common.dart';

/// เข้าสู่ระบบด้วยบัญชีเดิมใน Garage DB (dbo.User)
/// username ไม่ได้จำกัดว่าต้องเป็นตัวเลขเสมอไป (เว็บก็รับข้อความทั่วไปแบบนี้อยู่แล้วใน LoginPage.tsx)
class LoginPage extends ConsumerStatefulWidget {
  const LoginPage({super.key});

  @override
  ConsumerState<LoginPage> createState() => _LoginPageState();
}

class _LoginPageState extends ConsumerState<LoginPage> {
  final _userNameController = TextEditingController();
  final _passwordController = TextEditingController();
  final _passwordFocus = FocusNode();

  bool _submitting = false;
  bool _obscure = true;
  String? _errorTh;
  String? _errorTrace;

  @override
  void dispose() {
    _userNameController.dispose();
    _passwordController.dispose();
    _passwordFocus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: T.navy900,
      body: SafeArea(
        child: Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(T.s24),
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 420),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const _Brand(),
                  const SizedBox(height: T.s32),
                  Container(
                    padding: const EdgeInsets.all(T.s24),
                    decoration: BoxDecoration(
                      color: Colors.white,
                      borderRadius: BorderRadius.circular(T.rCard),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        const Text('เข้าสู่ระบบพนักงาน',
                            style: TextStyle(
                                fontSize: 22,
                                fontWeight: FontWeight.w700,
                                color: T.text,
                                height: 1.5)),
                        const SizedBox(height: 4),
                        const Text('ใช้รหัสพนักงานและรหัสผ่านเดิมของอู่',
                            style: TextStyle(fontSize: 14, color: T.muted, height: 1.65)),
                        const SizedBox(height: T.s24),

                        if (_errorTh != null) ...[
                          InfoBanner(
                            icon: Icons.error_outline,
                            title: 'เข้าสู่ระบบไม่สำเร็จ',
                            body: _errorTrace == null
                                ? _errorTh
                                : '$_errorTh\nรหัสอ้างอิง $_errorTrace',
                            tone: StateTone.error,
                          ),
                          const SizedBox(height: T.s16),
                        ],

                        _label('รหัสพนักงาน'),
                        const SizedBox(height: 6),
                        TextField(
                          controller: _userNameController,
                          enabled: !_submitting,
                          keyboardType: TextInputType.text,
                          textInputAction: TextInputAction.next,
                          onSubmitted: (_) => _passwordFocus.requestFocus(),
                          autofillHints: const [AutofillHints.username],
                          decoration: _decoration('กรอกรหัสพนักงาน', Icons.badge_outlined),
                          style: const TextStyle(fontSize: 17, height: 1.5),
                        ),
                        const SizedBox(height: T.s16),

                        _label('รหัสผ่าน'),
                        const SizedBox(height: 6),
                        TextField(
                          controller: _passwordController,
                          focusNode: _passwordFocus,
                          enabled: !_submitting,
                          obscureText: _obscure,
                          textInputAction: TextInputAction.done,
                          onSubmitted: (_) => _submit(),
                          autofillHints: const [AutofillHints.password],
                          decoration: _decoration('รหัสผ่าน', Icons.lock_outline).copyWith(
                            suffixIcon: IconButton(
                              onPressed: () => setState(() => _obscure = !_obscure),
                              icon: Icon(_obscure ? Icons.visibility_outlined : Icons.visibility_off_outlined),
                              tooltip: _obscure ? 'แสดงรหัสผ่าน' : 'ซ่อนรหัสผ่าน',
                              color: T.muted,
                            ),
                          ),
                          style: const TextStyle(fontSize: 17, height: 1.5),
                        ),
                        const SizedBox(height: T.s24),

                        SizedBox(
                          height: T.ctaHeight,
                          child: FilledButton(
                            onPressed: _submitting ? null : _submit,
                            style: FilledButton.styleFrom(
                              backgroundColor: T.blue600,
                              disabledBackgroundColor: const Color(0xFFCBD5E1),
                              shape: RoundedRectangleBorder(
                                  borderRadius: BorderRadius.circular(T.rCard)),
                            ),
                            child: _submitting
                                ? const SizedBox(
                                    width: 22,
                                    height: 22,
                                    child: CircularProgressIndicator(
                                        strokeWidth: 2.4, color: Colors.white))
                                : const Text('เข้าสู่ระบบ',
                                    style: TextStyle(fontSize: 17, fontWeight: FontWeight.w700)),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: T.s16),
                  const Text(
                    'ระบบภายในของอู่ · ใช้ได้เฉพาะพนักงานที่ได้รับสิทธิ์',
                    textAlign: TextAlign.center,
                    style: TextStyle(fontSize: 13, color: T.faintOnDark, height: 1.7),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  Widget _label(String text) => Text(text,
      style: const TextStyle(
          fontSize: 14, fontWeight: FontWeight.w700, color: T.navy700, height: 1.6));

  InputDecoration _decoration(String hint, IconData icon) => InputDecoration(
        hintText: hint,
        hintStyle: const TextStyle(color: T.faint, fontSize: 16),
        prefixIcon: Icon(icon, color: T.muted, size: 20),
        filled: true,
        fillColor: const Color(0xFFF8FAFC),
        contentPadding: const EdgeInsets.symmetric(horizontal: T.s12, vertical: 16),
        border: OutlineInputBorder(
          borderRadius: BorderRadius.circular(T.rInput),
          borderSide: const BorderSide(color: T.borderStrong),
        ),
        enabledBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(T.rInput),
          borderSide: const BorderSide(color: T.borderStrong),
        ),
        focusedBorder: OutlineInputBorder(
          borderRadius: BorderRadius.circular(T.rInput),
          borderSide: const BorderSide(color: T.blue600, width: 2),
        ),
      );

  Future<void> _submit() async {
    final userName = _userNameController.text.trim();
    final password = _passwordController.text;

    if (userName.isEmpty || password.isEmpty) {
      setState(() {
        _errorTh = 'กรุณากรอกรหัสพนักงานและรหัสผ่าน';
        _errorTrace = null;
      });
      return;
    }

    setState(() {
      _submitting = true;
      _errorTh = null;
      _errorTrace = null;
    });

    try {
      final result = await ref.read(authApiProvider).login(userName, password);
      // [BIZ] ไม่มีขั้นเลือกสาขา/กะแล้ว — token จาก /auth/login ผูกสาขาของพนักงานมาให้เลย
      // router เห็นเซสชันแล้วจะพาไปหน้าแรกตามบทบาทเอง (landingFor)
      await ref.read(sessionProvider.notifier).save(result.toSession());
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

class _Brand extends StatelessWidget {
  const _Brand();

  @override
  Widget build(BuildContext context) => Column(
        children: [
          // ใช้ตราหกเหลี่ยมล้วน ไม่ใช่โลโก้เต็ม เพราะตัวอักษร "GaragePro" ในไฟล์แบรนด์เป็นสีดำ
          // ซึ่งอ่านไม่ออกบนพื้น navy ของหน้านี้ — ข้อความใช้ของแอปที่เป็นสีขาวอยู่แล้วด้านล่าง
          Image.asset(
            'assets/images/logo_mark.png',
            width: 76,
            height: 76,
            // จอความละเอียดสูงจะ decode ที่ 3x ของขนาดจริง พอดีกับไฟล์ 512px
            filterQuality: FilterQuality.medium,
          ),
          const SizedBox(height: T.s12),
          const Text('GaragePro',
              style: TextStyle(
                  fontSize: 26, fontWeight: FontWeight.w700, color: Colors.white, height: 1.4)),
          const Text('Auto Services',
              style: TextStyle(fontSize: 14, color: T.faintOnDark, height: 1.6)),
        ],
      );
}
