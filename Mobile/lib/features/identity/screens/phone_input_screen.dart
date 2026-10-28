import 'package:flutter/material.dart';

import '../../../core/models/user_role.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/status_pill.dart';
import '../services/auth_service.dart';
import '../widgets/auth_photo_header.dart';
import '../widgets/phone_input_field.dart';
import 'otp_verify_screen.dart';

/// Screen for entering phone number to request OTP.
class PhoneInputScreen extends StatefulWidget {
  final AppRole role;
  final AuthService? authService;

  const PhoneInputScreen({
    super.key,
    this.role = AppRole.customer,
    this.authService,
  });

  @override
  State<PhoneInputScreen> createState() => _PhoneInputScreenState();
}

class _PhoneInputScreenState extends State<PhoneInputScreen> {
  final TextEditingController _phoneController = TextEditingController();
  late final AuthService _authService;
  bool _isLoading = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _authService = widget.authService ?? AuthService();
  }

  @override
  void dispose() {
    _phoneController.dispose();
    super.dispose();
  }

  Future<void> _handleRequestOtp() async {
    final rawPhone = _phoneController.text.trim();
    final normalized = AuthService.normalizePhone(rawPhone);

    if (normalized == null) {
      setState(() {
        _errorMessage = 'Vui lòng nhập số điện thoại di động 10 số hợp lệ (ví dụ: 0912345678).';
      });
      return;
    }

    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final result = await _authService.requestOtp(
        phoneNumber: normalized,
        role: widget.role,
      );

      if (!mounted) return;
      setState(() => _isLoading = false);

      Navigator.push(
        context,
        MaterialPageRoute(
          builder: (context) => OtpVerifyScreen(
            phoneNumber: normalized,
            role: widget.role,
            resendCooldownSeconds: result.resendAvailableInSeconds,
            authService: _authService,
          ),
        ),
      );
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isLoading = false;
        _errorMessage = e
            .toString()
            .replaceAll('Exception: ', '')
            .replaceAll('HttpException: ', '');
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final roleLabel = widget.role == AppRole.worker
        ? 'Chuyên Viên / Thợ'
        : 'Khách Hàng';

    // MOB-UI-10: the welcome look. A housework photo with the title on top, the form on a
    // sheet over its bottom edge; the button sits at the bottom.
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: EdgeInsets.zero,
            children: [
              AuthPhotoHeader(
                role: widget.role,
                title: 'Số điện thoại',
                titleKey: const Key('phone-input-title'),
                subtitle: 'Mã 6 số sẽ gửi qua SMS.',
              ),
              AuthFormSheet(
                children: [
                  Align(
                    alignment: Alignment.centerLeft,
                    child: StatusPill('Vai trò: $roleLabel', tone: PillTone.ok),
                  ),
                  const SizedBox(height: 20.0),
                  PhoneInputField(
                    controller: _phoneController,
                    enabled: !_isLoading,
                    errorText: _errorMessage,
                    onChanged: (_) {
                      if (_errorMessage != null) {
                        setState(() => _errorMessage = null);
                      }
                    },
                  ),
                  const SizedBox(height: 16.0),
                  const Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(
                        Icons.lock_outline,
                        size: 14.0,
                        color: NordicColors.textSecondary,
                      ),
                      SizedBox(width: 6.0),
                      Expanded(
                        child: Text(
                          'Không cần mật khẩu: số điện thoại là tài khoản của bạn.',
                          style: NordicTypography.bodySmall,
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
      bottomNavigationBar: BottomActionBar(
        children: [
          NordicButton(
            label: 'Gửi Mã Xác Thực',
            width: double.infinity,
            isLoading: _isLoading,
            onPressed: _handleRequestOtp,
          ),
        ],
      ),
    );
  }
}
