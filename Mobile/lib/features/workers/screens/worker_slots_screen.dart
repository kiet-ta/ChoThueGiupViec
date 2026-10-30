import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/pressable.dart';
import '../../../core/widgets/stat_tile.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../models/booking_slot_models.dart';
import '../services/workers_location_service.dart';
import '../services/workers_slots_service.dart';

/// Screen for managing Worker Weekly Availability Block Slots (contract workers.md §2.3).
/// Implements MOB-M4-02: weekly grid toggle for Sáng/Chiều/Tối shifts.
/// MOB-UI-20 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage` (tab, or pushed when it has a back route),
/// tokens only, pressable day tiles, a retry state when the week cannot be loaded.
class WorkerSlotsScreen extends StatefulWidget {
  final WorkersSlotsService? slotsService;
  final WorkersLocationService? locationService;
  final DateTime? initialDate;

  const WorkerSlotsScreen({
    super.key,
    this.slotsService,
    this.locationService,
    this.initialDate,
  });

  @override
  State<WorkerSlotsScreen> createState() => _WorkerSlotsScreenState();
}

class _WorkerSlotsScreenState extends State<WorkerSlotsScreen> {
  late final WorkersSlotsService _service;
  late final WorkersLocationService _locationService;
  late DateTime _weekStartDate;

  bool _isLoading = false;

  /// The week could not be loaded: the whole list is replaced by a retry state.
  String? _loadError;

  /// A switch could not be saved: the switch is put back and this line explains why.
  String? _toggleError;
  final Map<String, bool> _slotActiveState = {}; // Key: "yyyy-MM-dd:SHIFT_CODE"

  /// Index (0 = Monday) of the day whose shifts are listed.
  late int _selectedDay;

  static const _shiftIcons = [
    Icons.wb_sunny_outlined,
    Icons.light_mode_outlined,
    Icons.bedtime_outlined,
  ];

  static const List<Map<String, String>> _shifts = [
    {'code': 'SHIFT_MORNING', 'name': 'Ca Sáng', 'time': '08:00 – 12:00'},
    {'code': 'SHIFT_AFTERNOON', 'name': 'Ca Chiều', 'time': '13:00 – 17:00'},
    {'code': 'SHIFT_EVENING', 'name': 'Ca Tối', 'time': '17:30 – 20:30'},
  ];

  @override
  void initState() {
    super.initState();
    _service = widget.slotsService ?? WorkersSlotsService();
    _locationService = widget.locationService ?? WorkersLocationService();

    final now = widget.initialDate ?? DateTime.now();
    // Align to Monday of current week
    _weekStartDate = now.subtract(Duration(days: now.weekday - 1));
    _selectedDay = now.weekday - 1;
    _loadWeeklySlots();
    _shareLocation();
  }

  /// Best effort: the dispatch search needs the worker's position (decisions D19). A failure must not change or block the screen.
  Future<void> _shareLocation() async {
    try {
      await _locationService.shareCurrentLocation();
    } catch (_) {
      // The slot toggle is what the worker asked for; the position is sent again the next time.
    }
  }

  String _formatDate(DateTime dt) {
    final y = dt.year.toString().padLeft(4, '0');
    final m = dt.month.toString().padLeft(2, '0');
    final d = dt.day.toString().padLeft(2, '0');
    return '$y-$m-$d';
  }

  String _formatDisplayDate(DateTime dt) {
    const days = [
      'Thứ 2',
      'Thứ 3',
      'Thứ 4',
      'Thứ 5',
      'Thứ 6',
      'Thứ 7',
      'Chủ nhật',
    ];
    final dayName = days[dt.weekday - 1];
    return '$dayName, ${dt.day.toString().padLeft(2, '0')}/${dt.month.toString().padLeft(2, '0')}';
  }

  Future<void> _loadWeeklySlots() async {
    setState(() {
      _isLoading = true;
      _loadError = null;
      _toggleError = null;
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
        _loadError = e.toString();
      });
    }
  }

  Future<void> _toggleSlot(
    String dateStr,
    String shiftCode,
    bool currentActive,
  ) async {
    final newActive = !currentActive;
    final key = '$dateStr:$shiftCode';

    // Optimistic UI update
    setState(() {
      _slotActiveState[key] = newActive;
      _toggleError = null;
    });

    try {
      await _service.toggleSlot(
        ToggleBookingSlotRequest(
          slotDate: dateStr,
          shiftCode: shiftCode,
          isActive: newActive,
        ),
      );
      if (newActive) {
        _shareLocation();
      }
    } catch (e) {
      // Revert on error
      setState(() {
        _slotActiveState[key] = currentActive;
        _toggleError = 'Không thể thay đổi lịch rảnh: ${e.toString()}';
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
    final pushed = ModalRoute.of(context)?.canPop ?? false;
    final selected = _weekStartDate.add(Duration(days: _selectedDay));

    final children = <Widget>[
      Row(
        children: [
          Expanded(
            child: StatTile(
              key: const Key('slots-on'),
              label: 'Ca bật',
              value: '$_activeInWeek',
            ),
          ),
          const SizedBox(width: NordicSpace.sm),
          Expanded(
            child: StatTile(
              key: const Key('slots-off'),
              label: 'Ca tắt',
              value: '${7 * _shifts.length - _activeInWeek}',
            ),
          ),
        ],
      ),
      const SizedBox(height: NordicSpace.gutter),
      _buildWeekNavigator(endDate),
      const SizedBox(height: NordicSpace.xs),
      _buildDayStrip(),
      const SizedBox(height: NordicSpace.md),
      if (_toggleError != null) ...[
        Container(
          padding: const EdgeInsets.all(NordicSpace.sm),
          decoration: const BoxDecoration(
            color: NordicColors.errorContainer,
            borderRadius: NordicRadius.fieldAll,
          ),
          child: Text(
            _toggleError!,
            style: NordicTypography.bodyRegular.copyWith(
              color: NordicColors.error,
            ),
          ),
        ),
        const SizedBox(height: NordicSpace.md),
      ],
      if (_isLoading)
        const LoadingStateWidget()
      else if (_loadError != null)
        ErrorStateWidget(message: _loadError!, onRetry: _loadWeeklySlots)
      else
        _buildShiftList(selected),
    ];

    return pushed
        ? NordicPage.pushed(
            title: 'Lịch rảnh',
            onRefresh: _loadWeeklySlots,
            children: children,
          )
        : NordicPage.tab(
            title: 'Lịch rảnh',
            onRefresh: _loadWeeklySlots,
            children: children,
          );
  }

  int get _activeInWeek => _slotActiveState.values.where((v) => v).length;

  Widget _buildWeekNavigator(DateTime endDate) {
    String dm(DateTime d) =>
        '${d.day.toString().padLeft(2, '0')}/${d.month.toString().padLeft(2, '0')}';
    return Row(
      children: [
        Expanded(
          child: Text(
            '${dm(_weekStartDate)} – ${dm(endDate)}/${endDate.year}',
            style: NordicTypography.caps,
          ),
        ),
        IconButton.outlined(
          icon: const Icon(Icons.chevron_left),
          onPressed: () => _navigateWeek(-1),
          tooltip: 'Tuần trước',
        ),
        const SizedBox(width: NordicSpace.xxs),
        IconButton.outlined(
          icon: const Icon(Icons.chevron_right),
          onPressed: () => _navigateWeek(1),
          tooltip: 'Tuần sau',
        ),
      ],
    );
  }

  /// Seven tiles, the chosen one filled; three dots show which shifts of that day are on.
  Widget _buildDayStrip() {
    const names = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];
    return Row(
      children: [
        for (var i = 0; i < 7; i++)
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(right: i == 6 ? 0 : NordicSpace.xxs),
              child: _dayTile(i, names[i]),
            ),
          ),
      ],
    );
  }

  Widget _dayTile(int index, String name) {
    final date = _weekStartDate.add(Duration(days: index));
    final dateStr = _formatDate(date);
    final on = index == _selectedDay;
    final inverse = NordicColors.textInverse;
    return Pressable(
      child: InkWell(
        key: Key('slots-day-$index'),
        borderRadius: NordicRadius.tileAll,
        onTap: () => setState(() => _selectedDay = index),
        child: Container(
          height: 64,
          decoration: BoxDecoration(
            color: on ? NordicColors.primary : NordicColors.surface,
            borderRadius: NordicRadius.tileAll,
            border: Border.all(
              color: on ? NordicColors.primary : NordicColors.border,
            ),
          ),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(
                name,
                style: NordicTypography.bodySmall.copyWith(
                  color: on
                      ? inverse.withValues(alpha: 0.7)
                      : NordicColors.textSecondary,
                ),
              ),
              Text(
                date.day.toString().padLeft(2, '0'),
                style: NordicTypography.titleSmall.copyWith(
                  fontWeight: FontWeight.w700,
                  color: on ? inverse : NordicColors.textTitle,
                ),
              ),
              const SizedBox(height: NordicSpace.xxs),
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  for (final shift in _shifts)
                    Container(
                      width: 4,
                      height: 4,
                      margin: const EdgeInsets.symmetric(horizontal: 1),
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color:
                            (_slotActiveState['$dateStr:${shift['code']}'] ??
                                false)
                            ? (on ? inverse : NordicColors.primaryContainer)
                            : (on
                                  ? inverse.withValues(alpha: 0.25)
                                  : NordicColors.surfaceDim),
                      ),
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  /// One row per shift of the chosen day with its switch.
  Widget _buildShiftList(DateTime date) {
    final dateStr = _formatDate(date);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(bottom: NordicSpace.xs),
          child: Text(
            _formatDisplayDate(date),
            style: NordicTypography.titleSmall,
          ),
        ),
        NordicCard(
          padding: EdgeInsets.zero,
          child: Column(
            children: [
              for (var i = 0; i < _shifts.length; i++) ...[
                if (i > 0) const Divider(height: 1, thickness: 1),
                _buildShiftRow(dateStr, _shifts[i], _shiftIcons[i]),
              ],
            ],
          ),
        ),
      ],
    );
  }

  Widget _buildShiftRow(
    String dateStr,
    Map<String, String> shift,
    IconData icon,
  ) {
    final shiftCode = shift['code']!;
    final isActive = _slotActiveState['$dateStr:$shiftCode'] ?? false;

    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: NordicSpace.md,
        vertical: NordicSpace.xs,
      ),
      child: Row(
        children: [
          Container(
            width: 40,
            height: 40,
            decoration: BoxDecoration(
              color: isActive
                  ? NordicColors.primaryFixed
                  : NordicColors.surfaceContainer,
              borderRadius: NordicRadius.tileAll,
            ),
            child: Icon(
              icon,
              size: 22,
              color: isActive
                  ? NordicColors.primary
                  : NordicColors.textSecondary,
            ),
          ),
          const SizedBox(width: NordicSpace.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(shift['time']!, style: NordicTypography.titleSmall),
                Text(shift['name']!, style: NordicTypography.bodySmall),
              ],
            ),
          ),
          Switch.adaptive(
            value: isActive,
            activeTrackColor: NordicColors.primary,
            onChanged: (_) => _toggleSlot(dateStr, shiftCode, isActive),
          ),
        ],
      ),
    );
  }
}
