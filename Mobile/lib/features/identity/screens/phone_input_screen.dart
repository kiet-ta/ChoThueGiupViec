import 'package:flutter/material.dart';
import '../../../core/models/user_role.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/trust_badge.dart';
import '../services/auth_service.dart';
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
        _errorMessage = e.toString().replaceAll('Exception: ', '').replaceAll('HttpException: ', '');
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final roleLabel = widget.role == AppRole.worker ? 'Chuyên Viên / Thợ' : 'Khách Hàng';

    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: Text('Đăng Nhập', style: NordicTypography.h3),
        centerTitle: true,
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.symmetric(horizontal: 20.0, vertical: 16.0),
            children: [
              const Align(
                alignment: Alignment.centerLeft,
                child: EyebrowBadge(text: 'XÁC THỰC BẢO MẬT'),
              ),
              const SizedBox(height: 12.0),
              Text(
                'Đăng nhập bằng số điện thoại',
                style: NordicTypography.h1,
              ),
              const SizedBox(height: 8.0),
              Text(
                'Nhập số điện thoại để nhận mã xác thực OTP 6 chữ số qua tin nhắn SMS.',
                style: NordicTypography.bodyRegular,
              ),
              const SizedBox(height: 12.0),
              Align(
                alignment: Alignment.centerLeft,
                child: Container(
                  padding: const EdgeInsets.symmetric(horizontal: 12.0, vertical: 6.0),
                  decoration: BoxDecoration(
                    color: NordicColors.surfaceSubtle,
                    borderRadius: BorderRadius.circular(8.0),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Icon(
                        widget.role == AppRole.worker ? Icons.handyman_outlined : Icons.person_outline,
                        size: 16.0,
                        color: NordicColors.primary,
                      ),
                      const SizedBox(width: 6.0),
                      Flexible(
                        child: Text(
                          'Vai trò: $roleLabel',
                          overflow: TextOverflow.ellipsis,
                          style: NordicTypography.bodySmall.copyWith(
                            color: NordicColors.primary,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 24.0),

              NordicCard(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text('Số điện thoại', style: NordicTypography.labelMedium),
                    const SizedBox(height: 8.0),
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
                    const SizedBox(height: 20.0),
                    NordicButton(
                      label: 'Gửi Mã Xác Thực',
                      width: double.infinity,
                      isLoading: _isLoading,
                      onPressed: _handleRequestOtp,
                    ),
                  ],
                ),
              ),

              const SizedBox(height: 32.0),

              // Reassurance badges
              const Center(
                child: Wrap(
                  alignment: WrapAlignment.center,
                  spacing: 8.0,
                  runSpacing: 8.0,
                  children: [
                    TrustBadge(text: 'Bảo mật thông tin'),
                    TrustBadge(text: 'Xác thực nhanh chóng'),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
