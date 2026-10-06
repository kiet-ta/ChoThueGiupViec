import 'dart:async';
import 'package:flutter/material.dart';
import '../theme/nordic_colors.dart';
import '../theme/nordic_typography.dart';

/// 30-second countdown timer widget for job dispatch offers (BR-03).
/// Displays a smooth circular countdown with remaining seconds and invokes [onTimeout].
class CountdownTimerWidget extends StatefulWidget {
  final int totalSeconds;
  final int initialSeconds;
  final VoidCallback? onTimeout;
  final ValueChanged<int>? onTick;

  const CountdownTimerWidget({
    super.key,
    this.totalSeconds = 30,
    this.initialSeconds = 30,
    this.onTimeout,
    this.onTick,
  });

  @override
  State<CountdownTimerWidget> createState() => _CountdownTimerWidgetState();
}

class _CountdownTimerWidgetState extends State<CountdownTimerWidget> {
  late int _remainingSeconds;
  Timer? _timer;

  @override
  void initState() {
    super.initState();
    _remainingSeconds = widget.initialSeconds;
    _startTimer();
  }

  void _startTimer() {
    if (_remainingSeconds <= 0) return;
    _timer = Timer.periodic(const Duration(seconds: 1), (timer) {
      if (!mounted) {
        timer.cancel();
        return;
      }

      if (_remainingSeconds > 1) {
        setState(() {
          _remainingSeconds--;
        });
        widget.onTick?.call(_remainingSeconds);
      } else {
        setState(() {
          _remainingSeconds = 0;
        });
        timer.cancel();
        widget.onTimeout?.call();
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final progress = widget.totalSeconds > 0
        ? _remainingSeconds / widget.totalSeconds
        : 0.0;
    final isUrgent = _remainingSeconds <= 10;
    final timerColor = isUrgent ? NordicColors.accentAmber : NordicColors.primary;

    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          SizedBox(
            width: 72.0,
            height: 72.0,
            child: Stack(
              alignment: Alignment.center,
              children: [
                CircularProgressIndicator(
                  value: progress,
                  strokeWidth: 6.0,
                  backgroundColor: NordicColors.border,
                  valueColor: AlwaysStoppedAnimation<Color>(timerColor),
                ),
                Text(
                  '${_remainingSeconds}s',
                  style: NordicTypography.h3.copyWith(
                    color: timerColor,
                    fontWeight: FontWeight.bold,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 8.0),
          Text(
            isUrgent ? 'Sắp hết thời gian nhận ca!' : 'Thời gian phản hồi',
            style: NordicTypography.bodySmall.copyWith(
              color: isUrgent ? NordicColors.accentAmber : NordicColors.textSecondary,
              fontWeight: FontWeight.w500,
            ),
          ),
        ],
      ),
    );
  }
}
