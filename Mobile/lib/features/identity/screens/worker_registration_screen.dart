import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../services/api_error.dart';
import '../services/auth_service.dart';

/// Profile of a new worker, shown after the OTP of a phone that has no worker yet (contract workers.md 2.1.1).
/// Until now the app only said "complete your profile" and had no screen for it, so no freelancer could register.
/// The server answers with the profile and no tokens: the worker signs in again with a new OTP.
class WorkerRegistrationScreen extends StatefulWidget {
  final AuthService? authService;

  const WorkerRegistrationScreen({super.key, this.authService});

  @override
  State<WorkerRegistrationScreen> createState() =>
      _WorkerRegistrationScreenState();
}

class _WorkerRegistrationScreenState extends State<WorkerRegistrationScreen> {
  final _fullName = TextEditingController();
  final _nationalId = TextEditingController();
  final _address = TextEditingController();
  final _bio = TextEditingController();
  late final AuthService _authService;
  Map<String, String> _fieldErrors = {};
  String? _message;
  bool _submitting = false;

  @override
  void initState() {
    super.initState();
    _authService = widget.authService ?? AuthService();
  }

  @override
  void dispose() {
    _fullName.dispose();
    _nationalId.dispose();
    _address.dispose();
    _bio.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final problems = AuthService.workerProfileProblems(
      fullName: _fullName.text,
      nationalId: _nationalId.text,
    );
    setState(() {
      _fieldErrors = problems;
      _message = null;
    });
    if (problems.isNotEmpty) return;

    setState(() => _submitting = true);
    try {
      await _authService.registerWorker(
        fullName: _fullName.text,
        nationalId: _nationalId.text,
        address: _address.text,
        bio: _bio.text,
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Đã tạo hồ sơ chuyên viên. Hãy đăng nhập lại bằng số điện thoại để bắt đầu.',
          ),
          backgroundColor: NordicColors.primary,
        ),
      );
      Navigator.of(context).popUntil((route) => route.isFirst);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _submitting = false;
        _message = e.message;
        final errors = e.fieldErrors;
        if (errors != null) {
          _fieldErrors = {
            for (final entry in errors.entries)
              if (entry.value.isNotEmpty) entry.key: entry.value.first,
          };
        }
      });
    }
  }

  Widget _field(
    String label,
    TextEditingController controller,
    String key, {
    TextInputType? keyboardType,
    int maxLines = 1,
    int? maxLength,
  }) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 12.0),
      child: TextField(
        key: Key('worker-reg-$key'),
        controller: controller,
        enabled: !_submitting,
        keyboardType: keyboardType,
        maxLines: maxLines,
        maxLength: maxLength,
        decoration: InputDecoration(
          labelText: label,
          errorText: _fieldErrors[key],
          counterText: '',
          border: OutlineInputBorder(borderRadius: BorderRadius.circular(12.0)),
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: const Text('Hồ sơ chuyên viên')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.all(16.0),
            children: [
              const EyebrowBadge(text: 'Đăng ký thợ'),
              const SizedBox(height: 8.0),
              const Text('Hoàn tất hồ sơ', style: NordicTypography.h2),
              const SizedBox(height: 6.0),
              const Text(
                'Số điện thoại đã được xác thực. Điền thông tin để bắt đầu nhận việc; sau đó bạn xác minh danh tính (eKYC).',
                style: NordicTypography.bodySmall,
              ),
              const SizedBox(height: 16.0),
              NordicCard(
                child: Column(
                  children: [
                    _field('Họ và tên', _fullName, 'fullName'),
                    _field(
                      'Số CCCD (12 chữ số)',
                      _nationalId,
                      'nationalId',
                      keyboardType: TextInputType.number,
                      maxLength: 12,
                    ),
                    _field('Địa chỉ', _address, 'address'),
                    _field('Giới thiệu (tuỳ chọn)', _bio, 'bio', maxLines: 2),
                    if (_message != null)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 12.0),
                        child: Text(
                          _message!,
                          style: const TextStyle(
                            color: Color(0xFF991B1B),
                            fontSize: 13.0,
                          ),
                        ),
                      ),
                    NordicButton(
                      label: _submitting ? 'Đang gửi…' : 'Hoàn tất hồ sơ',
                      width: double.infinity,
                      onPressed: _submitting ? null : _submit,
                    ),
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
