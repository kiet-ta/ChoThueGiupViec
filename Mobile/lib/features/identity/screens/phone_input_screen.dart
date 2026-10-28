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
        _errorMessage = 'Số điện thoại chưa đúng. Ví dụ: 0912 345 678';
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
        ? 'Người giúp việc'
        : 'Khách hàng';

    // MOB-UI-10: the welcome look. A housework photo with the title on top, the form on a
    // sheet over its bottom edge. MOB-UI-12: the button bar is part of the body, so the
    // keyboard pushes it up instead of covering it. The screen fills the full width.
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      body: Column(
        children: [
          Expanded(
            child: ListView(
              padding: EdgeInsets.zero,
              children: [
                AuthPhotoHeader(
                  role: widget.role,
                  title: 'Nhập số điện thoại',
                  titleKey: const Key('phone-input-title'),
                  subtitle: 'Mã xác nhận gửi qua tin nhắn.',
                ),
                AuthFormSheet(children: _form(roleLabel)),
              ],
            ),
          ),
          BottomActionBar(
            children: [
              NordicButton(
                label: 'Nhận mã',
                width: double.infinity,
                isLoading: _isLoading,
                onPressed: _handleRequestOtp,
              ),
            ],
          ),
        ],
      ),
    );
  }

  List<Widget> _form(String roleLabel) {
    return [
      Align(
        alignment: Alignment.centerLeft,
        child: StatusPill(roleLabel, tone: PillTone.ok),
      ),
      const SizedBox(height: 20.0),
      PhoneInputField(
        controller: _phoneController,
        enabled: !_isLoading,
        errorText: _errorMessage,
        isValid: AuthService.normalizePhone(_phoneController.text) != null,
        // Rebuild on every digit: the clear button and the check mark follow it.
        onChanged: (_) => setState(() => _errorMessage = null),
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
              'Không cần mật khẩu.',
              style: NordicTypography.bodySmall,
            ),
          ),
        ],
      ),
    ];
  }
}
