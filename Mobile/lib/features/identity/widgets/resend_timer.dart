import 'dart:async';
import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';

/// Countdown timer handling OTP resend cooldown.
class ResendTimer extends StatefulWidget {
  final int initialSeconds;
  final VoidCallback onResend;

  const ResendTimer({
    super.key,
    required this.initialSeconds,
    required this.onResend,
  });

  @override
  State<ResendTimer> createState() => _ResendTimerState();
}

class _ResendTimerState extends State<ResendTimer> {
  late int _remaining;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _remaining = widget.initialSeconds;
    _startTimer();
  }

  void _startTimer() {
    _timer?.cancel();
    _timer = Timer.periodic(const Duration(seconds: 1), (timer) {
      if (_remaining > 0) {
        if (mounted) setState(() => _remaining--);
      } else {
        timer.cancel();
      }
    });
  }

  void restart(int seconds) {
    if (mounted) {
      setState(() {
        _remaining = seconds;
      });
      _startTimer();
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_remaining > 0) {
      return Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          const Icon(
            Icons.timer_outlined,
            size: 16.0,
            color: NordicColors.textSecondary,
          ),
          const SizedBox(width: 6.0),
          Text(
            'Gửi lại mã sau ${_remaining}s',
            style: NordicTypography.bodySmall.copyWith(
              color: NordicColors.textSecondary,
              fontWeight: FontWeight.w500,
            ),
          ),
        ],
      );
    }

    return TextButton.icon(
      onPressed: () {
        widget.onResend();
        restart(60);
      },
      icon: const Icon(
        Icons.refresh_rounded,
        size: 16.0,
        color: NordicColors.primary,
      ),
      label: Text(
        'Gửi lại mã xác thực OTP',
        style: NordicTypography.bodyRegular.copyWith(
          color: NordicColors.primary,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
