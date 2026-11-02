import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/step_dots.dart';
import '../services/api_error.dart';
import '../services/auth_service.dart';

/// Profile of a new worker, shown after the OTP of a phone that has no worker yet (contract workers.md 2.1.1).
/// Until now the app only said "complete your profile" and had no screen for it, so no freelancer could register.
/// The server answers with the profile and no tokens: the worker signs in again with a new OTP.
/// MOB-UI-24 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage.pushed`, the submit button in `actions` above the keyboard,
/// tokens only, a wrong entry shakes the form.
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

  /// Bumped on every wrong entry so the form shakes.
  int _errors = 0;

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
      if (problems.isNotEmpty) _errors++;
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
        _errors++;
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
    return TextField(
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
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    return NordicPage.pushed(
      title: 'Hồ sơ chuyên viên',
      actions: [
        NordicButton(
          label: _submitting ? 'Đang gửi…' : 'Hoàn tất hồ sơ',
          onPressed: _submitting ? null : _submit,
        ),
      ],
      children: [
        // The phone is verified; the profile is being filled; eKYC comes after the new login.
        const StepDots(
          steps: [
            StepItem('SĐT', DotState.done),
            StepItem('Hồ sơ', DotState.current),
            StepItem('eKYC', DotState.todo),
          ],
        ),
        const SizedBox(height: NordicSpace.lg),
        const Text('Hoàn tất hồ sơ', style: NordicTypography.h2),
        const SizedBox(height: NordicSpace.xxs),
        const Text(
          'Số điện thoại đã được xác thực. Điền thông tin để bắt đầu nhận việc; sau đó bạn xác minh danh tính (eKYC).',
          style: NordicTypography.bodySmall,
        ),
        const SizedBox(height: NordicSpace.gutter),
        ErrorShake(
          errors: _errors,
          child: Column(
            children: [
              _field('Họ và tên', _fullName, 'fullName'),
              const SizedBox(height: NordicSpace.md),
              _field(
                'Số CCCD (12 chữ số)',
                _nationalId,
                'nationalId',
                keyboardType: TextInputType.number,
                maxLength: 12,
              ),
              const SizedBox(height: NordicSpace.md),
              _field('Địa chỉ', _address, 'address'),
              const SizedBox(height: NordicSpace.md),
              _field('Giới thiệu (tuỳ chọn)', _bio, 'bio', maxLines: 2),
              if (_message != null) ...[
                const SizedBox(height: NordicSpace.md),
                Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(NordicSpace.sm),
                  decoration: const BoxDecoration(
                    color: NordicColors.errorContainer,
                    borderRadius: NordicRadius.tileAll,
                  ),
                  child: Text(
                    _message!,
                    style: NordicTypography.bodySmall.copyWith(
                      color: NordicColors.error,
                    ),
                  ),
                ),
              ],
            ],
          ),
        ),
      ],
    );
  }
}
