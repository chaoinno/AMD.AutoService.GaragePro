import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/client.dart';
import '../../app/navigation.dart';
import '../../core/tokens.dart';
import '../../widgets/common.dart';
import '../../widgets/trial_banner.dart';

/// เข้าสู่ระบบด้วยบัญชีเดิมใน Garage DB (dbo.User) หรือบัญชีของอู่ที่สมัครเอง
/// username ไม่ได้จำกัดว่าต้องเป็นตัวเลขเสมอไป (เว็บก็รับข้อความทั่วไปแบบนี้อยู่แล้วใน LoginPage.tsx)
/// · อู่ที่สมัครเอง: เจ้าของใช้อีเมล · พนักงานใช้รหัสที่เจ้าของอู่สร้าง (`SP1042.T01`) — server แยกเองจากรูปแบบ
///   แอปส่ง endpoint/ฟิลด์เดิมทุกอย่าง แอปรุ่นที่อยู่บน store จึงเข้าระบบได้โดยไม่ต้องแก้
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

  /// อู่ถูกปิดการใช้งานหลังหมดช่วงทดลองใช้ (TRIAL_EXPIRED ที่ /auth/login) — บอกช่องทางติดต่อฝ่ายขายด้วย
  bool _trialLocked = false;

  @override
  void dispose() {
    _userNameController.dispose();
    _passwordController.dispose();
    _passwordFocus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    // หน้านี้ไม่มี AppBar จึงไม่มีใครตั้งสีแถบสถานะให้ — นาฬิกา/แบตเป็นตัวดำบนพื้น navy อ่านไม่ออก
    // และกระพริบจากขาว (จอ splash ตั้ง UIStatusBarStyleLightContent ใน Info.plist) เป็นดำทันทีที่แอปขึ้น
    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: SystemUiOverlayStyle.light,
      child: Scaffold(
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
                          const Text(
                              'ใช้รหัสพนักงาน · อู่ที่สมัครเอง: อีเมลเจ้าของ หรือรหัสจากเจ้าของอู่ (เช่น SP1042.T01)',
                              style: TextStyle(fontSize: 14, color: T.muted, height: 1.65)),
                          const SizedBox(height: T.s24),

                          if (_errorTh != null) ...[
                            InfoBanner(
                              icon: _trialLocked ? Icons.lock_outline : Icons.error_outline,
                              title: 'เข้าสู่ระบบไม่สำเร็จ',
                              body: _errorTrace == null
                                  ? _errorTh
                                  : '$_errorTh\nรหัสอ้างอิง $_errorTrace',
                              // ไม่ใช่ความผิดของผู้กรอก — โทนเตือน ไม่ใช่แดง
                              tone: _trialLocked ? StateTone.warn : StateTone.error,
                            ),
                            if (_trialLocked) ...[
                              const SizedBox(height: T.s8),
                              const _SalesContact(),
                            ],
                            const SizedBox(height: T.s16),
                          ],

                          _label('รหัสพนักงานหรืออีเมล'),
                          const SizedBox(height: 6),
                          TextField(
                            controller: _userNameController,
                            enabled: !_submitting,
                            keyboardType: TextInputType.text,
                            // iOS แก้คำอัตโนมัติจะทำให้ "SP1042.T01"/อีเมล เพี้ยนก่อนส่ง
                            autocorrect: false,
                            textInputAction: TextInputAction.next,
                            onSubmitted: (_) => _passwordFocus.requestFocus(),
                            autofillHints: const [AutofillHints.username],
                            decoration:
                                _decoration('กรอกรหัสพนักงานหรืออีเมล', Icons.badge_outlined),
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
        _errorTh = 'กรุณากรอกรหัสพนักงาน (หรืออีเมล) และรหัสผ่าน';
        _errorTrace = null;
        _trialLocked = false;
      });
      return;
    }

    setState(() {
      _submitting = true;
      _errorTh = null;
      _errorTrace = null;
      _trialLocked = false;
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
          _trialLocked = e.isTrialExpired;
        });
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }
}

/// ช่องทางติดต่อฝ่ายขายเมื่ออู่ถูกปิดการใช้งาน — แอปไม่มีตัวเปิดลิงก์ภายนอก (ไม่มี url_launcher)
/// จึงให้คัดลอก ID ไปค้นใน LINE เองแทน
class _SalesContact extends StatelessWidget {
  const _SalesContact();

  static const _lineId = '@garagepro';

  @override
  Widget build(BuildContext context) => Row(
        children: [
          const Icon(Icons.chat_bubble_outline, size: 18, color: T.navy700),
          const SizedBox(width: T.s8),
          const Expanded(
            child: Text(salesLineContact,
                style: TextStyle(
                    fontSize: 15, fontWeight: FontWeight.w700, color: T.navy700, height: 1.6)),
          ),
          TextButton.icon(
            onPressed: () async {
              await Clipboard.setData(const ClipboardData(text: _lineId));
              showAppMessage('คัดลอก $_lineId แล้ว เปิด LINE แล้วค้นหาเพื่อนด้วย ID นี้');
            },
            style: TextButton.styleFrom(minimumSize: const Size(T.touchMin, T.touchMin)),
            icon: const Icon(Icons.copy, size: 16),
            label: const Text('คัดลอก ID'),
          ),
        ],
      );
}

class _Brand extends StatelessWidget {
  const _Brand();

  @override
  Widget build(BuildContext context) => Column(
        children: [
          // ตราหกเหลี่ยม ServicePro (สกัดจาก service-pro-logo/main-logo.png ให้พื้นโปร่ง)
          // ตัวอักษรวาดเองด้านล่างแทนการใช้ไฟล์ logo-with-text.jpg ที่มีพื้นหลังลายจุดติดมาด้วย
          Image.asset(
            'assets/images/logo_mark.png',
            width: 76,
            height: 76,
            // จอความละเอียดสูงจะ decode ที่ 3x ของขนาดจริง พอดีกับไฟล์ 512px
            filterQuality: FilterQuality.medium,
          ),
          const SizedBox(height: T.s12),
          // "Service" ขาว + "Pro" ฟ้าแบบเดียวกับโลโก้ — ฟ้ายกให้อ่อนกว่าในไฟล์ (#2F65C0 ได้ contrast แค่
          // ~2.8:1 บน navy900) ให้ตรงกับเว็บ (web/src/components/Brand.tsx)
          const Text.rich(
            TextSpan(children: [
              TextSpan(text: 'Service'),
              TextSpan(text: 'Pro', style: TextStyle(color: Color(0xFF4C9BFF))),
            ]),
            semanticsLabel: 'ServicePro',
            style: TextStyle(
                fontSize: 28,
                fontWeight: FontWeight.w800,
                fontStyle: FontStyle.italic,
                color: Colors.white,
                height: 1.4),
          ),
        ],
      );
}
