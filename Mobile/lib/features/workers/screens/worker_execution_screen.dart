import 'dart:async';
import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../widgets/tinted_card.dart';

class ExecutionChecklistItem {
  final String id;
  final String title;
  final String subtitle;
  final bool isRequired;
  bool isCompleted;

  ExecutionChecklistItem({
    required this.id,
    required this.title,
    required this.subtitle,
    this.isRequired = true,
    this.isCompleted = false,
  });
}

/// Screen for Worker Job Execution during active shift (contract workers.md §2.5).
/// Implements MOB-M4-03: execution checklist + 4-hour shift timer tracking.
class WorkerExecutionScreen extends StatefulWidget {
  final int assignmentId;
  final String workZone;
  final int maxShiftMinutes; // Default 240 mins (4 hours) per PRD BR-01
  final int initialElapsedSeconds;

  const WorkerExecutionScreen({
    super.key,
    this.assignmentId = 2001,
    this.workZone = 'Phòng khách & Bếp - Căn hộ 402',
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

  late final List<ExecutionChecklistItem> _checklist;

  @override
  void initState() {
    super.initState();
    _elapsedSeconds = widget.initialElapsedSeconds;

    _checklist = [
      ExecutionChecklistItem(
        id: '1',
        title: 'Dọn dẹp & sắp xếp phòng khách',
        subtitle: 'Lau bụi bàn ghế, kệ TV và hút bụi thảm',
        isRequired: true,
        isCompleted: true,
      ),
      ExecutionChecklistItem(
        id: '2',
        title: 'Lau chùi cửa kính & bề mặt',
        subtitle: 'Lau sạch các bề mặt kính tường và cửa sổ',
        isRequired: true,
        isCompleted: true,
      ),
      ExecutionChecklistItem(
        id: '3',
        title: 'Hút bụi & lau sàn nhà',
        subtitle: 'Lau bằng dung dịch sát khuẩn dịu nhẹ',
        isRequired: true,
        isCompleted: false,
      ),
      ExecutionChecklistItem(
        id: '4',
        title: 'Vệ sinh & khử trùng phòng bếp',
        subtitle: 'Lau sạch bàn bếp, bồn rửa và tủ sấy',
        isRequired: false,
        isCompleted: false,
      ),
      ExecutionChecklistItem(
        id: '5',
        title: 'Thu gom rác & phân loại rác thải',
        subtitle: 'Thu gom rác sinh hoạt và thay túi rác mới',
        isRequired: true,
        isCompleted: false,
      ),
    ];

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

  int get _completedCount => _checklist.where((item) => item.isCompleted).length;

  @override
  Widget build(BuildContext context) {
    final maxSeconds = widget.maxShiftMinutes * 60;
    final progress = (_elapsedSeconds / maxSeconds).clamp(0.0, 1.0);
    final isNearLimit = _elapsedSeconds >= (maxSeconds - 1800); // within 30 mins of 4h limit

    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: Text('Màn Thi Công #${widget.assignmentId}'),
        backgroundColor: Colors.white,
        foregroundColor: NordicColors.textTitle,
        elevation: 0.5,
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.all(16.0),
            children: [
              const EyebrowBadge(text: 'Ca Làm Đang Thi Công'),
              const SizedBox(height: 8.0),
              Text(
                widget.workZone,
                style: NordicTypography.h2,
              ),
              const SizedBox(height: 16.0),

              // Shift Timer Card
              _buildShiftTimerCard(maxSeconds, progress, isNearLimit),
              const SizedBox(height: 16.0),

              // Checklist Section
              _buildChecklistSection(),
              const SizedBox(height: 20.0),

              // Action Buttons
              NordicButton(
                label: 'Chụp Ảnh Before / After (VoL)',
                variant: NordicButtonVariant.primary,
                icon: const Icon(Icons.camera_alt, size: 18.0),
                width: double.infinity,
                onPressed: () {
                  ScaffoldMessenger.of(context).showSnackBar(
                    const SnackBar(content: Text('Chuyển sang màn hình chụp ảnh VoL')),
                  );
                },
              ),
              const SizedBox(height: 10.0),
              NordicButton(
                label: 'Gửi Nghiệm Thu Ca Làm',
                variant: NordicButtonVariant.secondary,
                icon: const Icon(Icons.check_circle_outline, size: 18.0),
                width: double.infinity,
                onPressed: () {
                  ScaffoldMessenger.of(context).showSnackBar(
                    SnackBar(
                      content: Text('Đã hoàn thành $_completedCount/${_checklist.length} công việc. Đang gửi nghiệm thu...'),
                    ),
                  );
                },
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildShiftTimerCard(int maxSeconds, double progress, bool isNearLimit) {
    return TintedCard(
      backgroundColor: isNearLimit ? const Color(0xFFFEF2F2) : Colors.white,
      child: Padding(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Row(
                  children: [
                    Icon(
                      Icons.timer_outlined,
                      color: isNearLimit ? const Color(0xFFDC2626) : NordicColors.primary,
                    ),
                    const SizedBox(width: 8.0),
                    const Text('Thời Gian Thi Công', style: NordicTypography.labelMedium),
                  ],
                ),
                Text(
                  'Tối đa ${widget.maxShiftMinutes ~/ 60}h',
                  style: const TextStyle(fontSize: 12.0, color: NordicColors.textSecondary),
                ),
              ],
            ),
            const SizedBox(height: 12.0),
            Text(
              _formatDuration(_elapsedSeconds),
              style: TextStyle(
                fontSize: 36.0,
                fontWeight: FontWeight.bold,
                fontFamily: 'monospace',
                color: isNearLimit ? const Color(0xFFDC2626) : NordicColors.textTitle,
              ),
            ),
            const SizedBox(height: 10.0),
            ClipRRect(
              borderRadius: BorderRadius.circular(6.0),
              child: LinearProgressIndicator(
                value: progress,
                minHeight: 8.0,
                backgroundColor: const Color(0xFFE2E8F0),
                valueColor: AlwaysStoppedAnimation<Color>(
                  isNearLimit ? const Color(0xFFDC2626) : NordicColors.primary,
                ),
              ),
            ),
            const SizedBox(height: 12.0),
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Text(
                  'Đã làm: ${(_elapsedSeconds / 60).toStringAsFixed(0)} phút',
                  style: const TextStyle(fontSize: 12.0, color: NordicColors.textSecondary),
                ),
                IconButton(
                  icon: Icon(
                    _isTimerRunning ? Icons.pause_circle_filled : Icons.play_circle_filled,
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
    );
  }

  Widget _buildChecklistSection() {
    return NordicCard(
      child: Padding(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                const Text('Checklist Công Việc', style: NordicTypography.h3),
                Text(
                  '$_completedCount/${_checklist.length}',
                  style: const TextStyle(
                    fontWeight: FontWeight.bold,
                    color: NordicColors.primary,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12.0),
            for (final item in _checklist) ...[
              _buildChecklistItemRow(item),
              if (item != _checklist.last) const Divider(height: 16.0),
            ],
          ],
        ),
      ),
    );
  }

  Widget _buildChecklistItemRow(ExecutionChecklistItem item) {
    return InkWell(
      onTap: () {
        setState(() {
          item.isCompleted = !item.isCompleted;
        });
      },
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Checkbox(
            value: item.isCompleted,
            activeColor: NordicColors.primary,
            onChanged: (val) {
              setState(() {
                item.isCompleted = val ?? false;
              });
            },
          ),
          const SizedBox(width: 4.0),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        item.title,
                        style: TextStyle(
                          fontWeight: FontWeight.bold,
                          fontSize: 14.0,
                          decoration: item.isCompleted ? TextDecoration.lineThrough : null,
                          color: item.isCompleted ? NordicColors.textSecondary : NordicColors.textTitle,
                        ),
                      ),
                    ),
                    if (item.isRequired)
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 6.0, vertical: 2.0),
                        decoration: BoxDecoration(
                          color: const Color(0xFFFEF3C7),
                          borderRadius: BorderRadius.circular(4.0),
                        ),
                        child: const Text(
                          'Bắt buộc',
                          style: TextStyle(fontSize: 10.0, color: Color(0xFFB45309), fontWeight: FontWeight.bold),
                        ),
                      ),
                  ],
                ),
                const SizedBox(height: 2.0),
                Text(
                  item.subtitle,
                  style: const TextStyle(fontSize: 12.0, color: NordicColors.textSecondary),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
