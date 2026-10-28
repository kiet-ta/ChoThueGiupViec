import 'package:flutter/material.dart';

import '../../../core/models/user_role.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/nordic_button.dart';
import '../services/auth_service.dart';
import 'worker_registration_screen.dart';
import '../widgets/auth_photo_header.dart';
import '../widgets/otp_code_field.dart';
import '../widgets/resend_timer.dart';

/// Screen for verifying 6-digit OTP code.
class OtpVerifyScreen extends StatefulWidget {
  final String phoneNumber;
  final AppRole role;
  final int resendCooldownSeconds;
  final AuthService? authService;

  const OtpVerifyScreen({
    super.key,
    required this.phoneNumber,
    this.role = AppRole.customer,
    this.resendCooldownSeconds = 60,
    this.authService,
  });

  @override
  State<OtpVerifyScreen> createState() => _OtpVerifyScreenState();
}

class _OtpVerifyScreenState extends State<OtpVerifyScreen> {
  final TextEditingController _otpController = TextEditingController();
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
    _otpController.dispose();
    super.dispose();
  }

  Future<void> _handleVerify([String? code]) async {
    final effectiveCode = (code ?? _otpController.text).trim();
    if (effectiveCode.length != 6) {
      setState(() {
        _errorMessage = 'Vui lòng nhập đủ 6 chữ số mã OTP.';
      });
      return;
    }

    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    final response = await _authService.verifyOtp(
      phoneNumber: widget.phoneNumber,
      role: widget.role,
      code: effectiveCode,
    );

    if (!mounted) return;
    setState(() => _isLoading = false);

    if (response.isSuccess) {
      if (response.isNewWorker) {
        // A new worker has no profile yet: ask for it (there is no session until it exists and the worker signs in again).
        Navigator.of(context).pushReplacement(
          MaterialPageRoute(
            builder: (context) =>
                WorkerRegistrationScreen(authService: _authService),
          ),
        );
        return;
      }
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Đăng nhập thành công vào TỔ ẤM!'),
          backgroundColor: NordicColors.primary,
        ),
      );
      Navigator.of(context).popUntil((route) => route.isFirst);
    } else {
      setState(() {
        _errorMessage =
            response.errorMessage ??
            'Xác thực không thành công. Vui lòng thử lại.';
      });
    }
  }

  Future<void> _handleResend() async {
    setState(() {
      _errorMessage = null;
      _otpController.clear();
    });

    try {
      await _authService.requestOtp(
        phoneNumber: widget.phoneNumber,
        role: widget.role,
      );

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Mã OTP mới đã được gửi đến số điện thoại của bạn.'),
          backgroundColor: NordicColors.secondaryAccent,
        ),
      );
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _errorMessage = e
            .toString()
            .replaceAll('Exception: ', '')
            .replaceAll('HttpException: ', '');
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    // MOB-UI-10: the welcome look. Photo with the title and where the code went on top, then the
    // 6 cells and resend on a sheet; the button at the bottom.
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
                title: 'Nhập mã xác thực',
                subtitle: 'Đã gửi tới ${widget.phoneNumber}',
              ),
              AuthFormSheet(
                children: [
                  OtpCodeField(
                    controller: _otpController,
                    enabled: !_isLoading,
                    onCompleted: (code) => _handleVerify(code),
                    onChanged: (_) {
                      if (_errorMessage != null) {
                        setState(() => _errorMessage = null);
                      }
                    },
                  ),
                  if (_errorMessage != null) ...[
                    const SizedBox(height: 14.0),
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 12.0,
                        vertical: 8.0,
                      ),
                      decoration: BoxDecoration(
                        color: NordicColors.errorContainer,
                        borderRadius: BorderRadius.zero,
                      ),
                      child: Row(
                        children: [
                          const Icon(
                            Icons.error_outline,
                            size: 16.0,
                            color: NordicColors.error,
                          ),
                          const SizedBox(width: 8.0),
                          Expanded(
                            child: Text(
                              _errorMessage!,
                              style: NordicTypography.bodySmall.copyWith(
                                color: NordicColors.error,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                  const SizedBox(height: 16.0),
                  ResendTimer(
                    initialSeconds: widget.resendCooldownSeconds,
                    onResend: _handleResend,
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
            label: 'Xác Nhận Đăng Nhập',
            width: double.infinity,
            isLoading: _isLoading,
            onPressed: () => _handleVerify(),
          ),
        ],
      ),
    );
  }
}
