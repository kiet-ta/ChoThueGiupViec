import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/nordic_button.dart';
import '../routes.dart';
import '../services/workers_photos_service.dart';

/// Screen for Worker Job Execution during active shift (contract workers.md §2.5), ui-templates `worker/execution.html`.
/// Implements MOB-M4-03: 4-hour shift timer tracking. The template has no checklist: the API has none (issue #442), so the
/// screen no longer shows a made-up one.
class WorkerExecutionScreen extends StatefulWidget {
  final int assignmentId;

  /// Where the worker is; shown only when the caller knows it (no endpoint returns it today).
  final String? workZone;
  final int maxShiftMinutes; // Default 240 mins (4 hours) per PRD BR-01
  final int initialElapsedSeconds;
  final WorkersPhotosService? photosService;

  const WorkerExecutionScreen({
    super.key,
    required this.assignmentId,
    this.photosService,
    this.workZone,
    this.maxShiftMinutes = 240,
    this.initialElapsedSeconds = 0,
  });

  @override
  State<WorkerExecutionScreen> createState() => _WorkerExecutionScreenState();
}

class _WorkerExecutionScreenState extends State<WorkerExecutionScreen> {
  Timer? _timer;
  late int _elapsedSeconds;
  bool _isTimerRunning = true;
  bool _isSubmitting = false;

  @override
  void initState() {
    super.initState();
    _elapsedSeconds = widget.initialElapsedSeconds;
    _startTimer();
  }

  void _startTimer() {
    _timer?.cancel();
    _timer = Timer.periodic(const Duration(seconds: 1), (timer) {
      if (_isTimerRunning) {
        setState(() {
          _elapsedSeconds++;
        });
      }
    });
  }

  void _toggleTimer() {
    setState(() {
      _isTimerRunning = !_isTimerRunning;
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  String _formatDuration(int totalSeconds) {
    final hours = totalSeconds ~/ 3600;
    final minutes = (totalSeconds % 3600) ~/ 60;
    final seconds = totalSeconds % 60;
    return '${hours.toString().padLeft(2, '0')}:${minutes.toString().padLeft(2, '0')}:${seconds.toString().padLeft(2, '0')}';
  }

  Future<void> _submitCompletion() async {
    setState(() => _isSubmitting = true);
    final messenger = ScaffoldMessenger.of(context);
    try {
      await (widget.photosService ?? WorkersPhotosService()).submitCompletion(
        widget.assignmentId,
      );
      if (!mounted) return;
      messenger.showSnackBar(
        const SnackBar(
          content: Text('Đã gửi nghiệm thu. Chờ khách hàng xác nhận.'),
        ),
      );
      Navigator.of(context).popUntil((route) => route.isFirst);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _isSubmitting = false);
      messenger.showSnackBar(
        SnackBar(
          content: Text('Gửi nghiệm thu không thành công: ${e.message}'),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final maxSeconds = widget.maxShiftMinutes * 60;
    final progress = (_elapsedSeconds / maxSeconds).clamp(0.0, 1.0);
    final isNearLimit =
        _elapsedSeconds >= (maxSeconds - 1800); // within 30 mins of 4h limit
    final color = isNearLimit ? NordicColors.error : NordicColors.primary;

    return Scaffold(
      appBar: AppBar(title: Text('Màn Thi Công #${widget.assignmentId}')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 24.0),
            children: [
              if (widget.workZone != null) ...[
                Text(widget.workZone!, style: NordicTypography.h2),
                const SizedBox(height: 16.0),
              ],
              // Elapsed ring: the one big number of the screen.
              Center(
                child: SizedBox(
                  width: 220.0,
                  height: 220.0,
                  child: Stack(
                    alignment: Alignment.center,
                    children: [
                      SizedBox(
                        width: 220.0,
                        height: 220.0,
                        child: CircularProgressIndicator(
                          value: progress,
                          strokeWidth: 14.0,
                          strokeCap: StrokeCap.round,
                          backgroundColor: NordicColors.surfaceContainer,
                          valueColor: AlwaysStoppedAnimation<Color>(color),
                        ),
                      ),
                      Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const Text(
                            'Thời Gian Thi Công',
                            style: NordicTypography.bodySmall,
                          ),
                          const SizedBox(height: 6.0),
                          Text(
                            _formatDuration(_elapsedSeconds),
                            style: NordicTypography.numberLarge.copyWith(
                              color: color,
                            ),
                          ),
                          const SizedBox(height: 6.0),
                          Text(
                            '/ ${widget.maxShiftMinutes ~/ 60}:00 giờ',
                            style: NordicTypography.bodySmall,
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 12.0),
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    'Đã làm: ${(_elapsedSeconds / 60).toStringAsFixed(0)} phút',
                    style: NordicTypography.bodySmall,
                  ),
                  IconButton.outlined(
                    key: const Key('execution-toggle-timer'),
                    icon: Icon(
                      _isTimerRunning
                          ? Icons.pause_circle_filled
                          : Icons.play_circle_filled,
                      color: NordicColors.primary,
                      size: 28.0,
                    ),
                    onPressed: _toggleTimer,
                    tooltip: _isTimerRunning ? 'Tạm dừng' : 'Tiếp tục',
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
      bottomNavigationBar: BottomActionBar(
        children: [
          Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              NordicButton(
                label: 'Chụp Ảnh Before / After (VoL)',
                variant: NordicButtonVariant.primary,
                icon: const Icon(Icons.photo_camera_outlined, size: 18.0),
                onPressed: () => Navigator.of(context).pushNamed(
                  WorkerRoutes.photos,
                  arguments: widget.assignmentId,
                ),
              ),
              const SizedBox(height: 8.0),
              NordicButton(
                key: const Key('submit_completion_button'),
                label: _isSubmitting ? 'Đang gửi…' : 'Gửi Nghiệm Thu Ca Làm',
                variant: NordicButtonVariant.secondary,
                onPressed: _isSubmitting ? null : _submitCompletion,
              ),
            ],
          ),
        ],
      ),
    );
  }
}
