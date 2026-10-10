import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_card.dart';
import '../models/booking_slot_models.dart';
import '../services/workers_slots_service.dart';
import '../widgets/tinted_card.dart';

/// Screen for managing Worker Weekly Availability Block Slots (contract workers.md §2.3).
/// Implements MOB-M4-02: weekly grid toggle for Sáng/Chiều/Tối shifts.
class WorkerSlotsScreen extends StatefulWidget {
  final WorkersSlotsService? slotsService;
  final DateTime? initialDate;

  const WorkerSlotsScreen({
    super.key,
    this.slotsService,
    this.initialDate,
  });

  @override
  State<WorkerSlotsScreen> createState() => _WorkerSlotsScreenState();
}

class _WorkerSlotsScreenState extends State<WorkerSlotsScreen> {
  late final WorkersSlotsService _service;
  late DateTime _weekStartDate;

  bool _isLoading = false;
  String? _errorMessage;
  final Map<String, bool> _slotActiveState = {}; // Key: "yyyy-MM-dd:SHIFT_CODE"

  static const List<Map<String, String>> _shifts = [
    {
      'code': 'SHIFT_MORNING',
      'name': 'Ca Sáng',
      'time': '08:00 – 12:00',
    },
    {
      'code': 'SHIFT_AFTERNOON',
      'name': 'Ca Chiều',
      'time': '13:00 – 17:00',
    },
    {
      'code': 'SHIFT_EVENING',
      'name': 'Ca Tối',
      'time': '17:30 – 20:30',
    },
  ];

  @override
  void initState() {
    super.initState();
    _service = widget.slotsService ?? WorkersSlotsService();

    final now = widget.initialDate ?? DateTime.now();
    // Align to Monday of current week
    _weekStartDate = now.subtract(Duration(days: now.weekday - 1));
    _loadWeeklySlots();
  }

  String _formatDate(DateTime dt) {
    final y = dt.year.toString().padLeft(4, '0');
    final m = dt.month.toString().padLeft(2, '0');
    final d = dt.day.toString().padLeft(2, '0');
    return '$y-$m-$d';
  }

  String _formatDisplayDate(DateTime dt) {
    const days = ['Thứ 2', 'Thứ 3', 'Thứ 4', 'Thứ 5', 'Thứ 6', 'Thứ 7', 'Chủ Nhật'];
    final dayName = days[dt.weekday - 1];
    return '$dayName, ${dt.day.toString().padLeft(2, '0')}/${dt.month.toString().padLeft(2, '0')}';
  }

  Future<void> _loadWeeklySlots() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    final startDateStr = _formatDate(_weekStartDate);
    final endDateStr = _formatDate(_weekStartDate.add(const Duration(days: 6)));

    try {
      final response = await _service.getSlots(
        startDate: startDateStr,
        endDate: endDateStr,
      );

      setState(() {
        _isLoading = false;
        _slotActiveState.clear();

        if (response.data != null) {
          for (final slot in response.data!) {
            final key = '${slot.slotDate}:${slot.shiftCode}';
            _slotActiveState[key] = slot.isActive;
          }
        }
      });
    } catch (e) {
      setState(() {
        _isLoading = false;
        _errorMessage = e.toString();
      });
    }
  }

  Future<void> _toggleSlot(String dateStr, String shiftCode, bool currentActive) async {
    final newActive = !currentActive;
    final key = '$dateStr:$shiftCode';

    // Optimistic UI update
    setState(() {
      _slotActiveState[key] = newActive;
    });

    try {
      await _service.toggleSlot(
        ToggleBookingSlotRequest(
          slotDate: dateStr,
          shiftCode: shiftCode,
          isActive: newActive,
        ),
      );
    } catch (e) {
      // Revert on error
      setState(() {
        _slotActiveState[key] = currentActive;
        _errorMessage = 'Không thể thay đổi lịch rảnh: ${e.toString()}';
      });
    }
  }

  void _navigateWeek(int weekDelta) {
    setState(() {
      _weekStartDate = _weekStartDate.add(Duration(days: weekDelta * 7));
    });
    _loadWeeklySlots();
  }

  @override
  Widget build(BuildContext context) {
    final endDate = _weekStartDate.add(const Duration(days: 6));

    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Lịch Rảnh Ca Làm'),
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
              const EyebrowBadge(text: 'Quản Lý Thời Gian Rảnh'),
              const SizedBox(height: 8.0),
              const Text(
                'Block Slots Lịch Tuần',
                style: NordicTypography.h2,
              ),
              const SizedBox(height: 6.0),
              const Text(
                'Bật ca làm để nhận công việc tự động từ hệ thống. Tắt ca làm khi bạn bận.',
                style: NordicTypography.bodySmall,
              ),
              const SizedBox(height: 16.0),

              _buildWeekNavigator(endDate),
              const SizedBox(height: 16.0),

              if (_errorMessage != null) ...[
                TintedCard(
                  backgroundColor: const Color(0xFFFEE2E2),
                  child: Padding(
                    padding: const EdgeInsets.all(12.0),
                    child: Text(
                      _errorMessage!,
                      style: const TextStyle(color: Color(0xFF991B1B), fontSize: 13.0),
                    ),
                  ),
                ),
                const SizedBox(height: 16.0),
              ],

              if (_isLoading) ...[
                const Center(
                  child: Padding(
                    padding: EdgeInsets.all(32.0),
                    child: CircularProgressIndicator(color: NordicColors.primary),
                  ),
                ),
              ] else ...[
                for (int dayOffset = 0; dayOffset < 7; dayOffset++)
                  _buildDaySlotCard(_weekStartDate.add(Duration(days: dayOffset))),
              ],
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildWeekNavigator(DateTime endDate) {
    return NordicCard(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12.0, vertical: 8.0),
        child: Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            IconButton(
              icon: const Icon(Icons.chevron_left),
              onPressed: () => _navigateWeek(-1),
              tooltip: 'Tuần trước',
            ),
            Text(
              '${_weekStartDate.day}/${_weekStartDate.month} – ${endDate.day}/${endDate.month}/${endDate.year}',
              style: NordicTypography.labelMedium,
            ),
            IconButton(
              icon: const Icon(Icons.chevron_right),
              onPressed: () => _navigateWeek(1),
              tooltip: 'Tuần sau',
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildDaySlotCard(DateTime date) {
    final dateStr = _formatDate(date);
    final displayDate = _formatDisplayDate(date);
    final isToday = _formatDate(DateTime.now()) == dateStr;

    return Container(
      margin: const EdgeInsets.only(bottom: 12.0),
      child: TintedCard(
        backgroundColor: isToday ? const Color(0xFFF0FDF4) : Colors.white,
        child: Padding(
          padding: const EdgeInsets.all(14.0),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceBetween,
                children: [
                  Text(
                    displayDate,
                    style: TextStyle(
                      fontWeight: FontWeight.bold,
                      fontSize: 15.0,
                      color: isToday ? NordicColors.primary : NordicColors.textTitle,
                    ),
                  ),
                  if (isToday)
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8.0, vertical: 2.0),
                      decoration: BoxDecoration(
                        color: NordicColors.primary,
                        borderRadius: BorderRadius.circular(10.0),
                      ),
                      child: const Text(
                        'Hôm nay',
                        style: TextStyle(color: Colors.white, fontSize: 10.0, fontWeight: FontWeight.bold),
                      ),
                    ),
                ],
              ),
              const SizedBox(height: 10.0),
              for (final shift in _shifts) ...[
                _buildShiftRow(dateStr, shift),
                if (shift != _shifts.last) const SizedBox(height: 8.0),
              ],
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildShiftRow(String dateStr, Map<String, String> shift) {
    final shiftCode = shift['code']!;
    final shiftName = shift['name']!;
    final shiftTime = shift['time']!;

    final key = '$dateStr:$shiftCode';
    final isActive = _slotActiveState[key] ?? false;

    return Container(
      padding: const EdgeInsets.all(10.0),
      decoration: BoxDecoration(
        color: isActive ? const Color(0xFFDCFCE7) : const Color(0xFFF8FAFC),
        borderRadius: BorderRadius.circular(8.0),
        border: Border.all(
          color: isActive ? const Color(0xFF86EFAC) : NordicColors.border,
        ),
      ),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Row(
            children: [
              Icon(
                isActive ? Icons.event_available : Icons.event_busy,
                size: 20.0,
                color: isActive ? const Color(0xFF15803D) : NordicColors.textSecondary,
              ),
              const SizedBox(width: 8.0),
              Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    shiftName,
                    style: TextStyle(
                      fontWeight: FontWeight.bold,
                      fontSize: 13.0,
                      color: isActive ? const Color(0xFF15803D) : NordicColors.textTitle,
                    ),
                  ),
                  Text(
                    shiftTime,
                    style: const TextStyle(fontSize: 11.0, color: NordicColors.textSecondary),
                  ),
                ],
              ),
            ],
          ),
          Switch.adaptive(
            value: isActive,
            activeColor: NordicColors.primary,
            onChanged: (_) => _toggleSlot(dateStr, shiftCode, isActive),
          ),
        ],
      ),
    );
  }
}
