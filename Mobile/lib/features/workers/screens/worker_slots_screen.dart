import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/pressable.dart';
import '../../../core/widgets/stat_tile.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../dispatch/routes.dart';
import '../../payouts/logic/payout_logic.dart';
import '../models/booking_slot_models.dart';
import '../models/worker_assignment_models.dart';
import '../routes.dart';
import '../services/workers_assignments_service.dart';
import '../services/workers_location_service.dart';
import '../services/workers_slots_service.dart';

/// Screen for managing Worker Weekly Availability Block Slots (contract workers.md §2.3).
/// Implements MOB-M4-02: weekly grid toggle for Sáng/Chiều/Tối shifts.
/// MOB-UI-20 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage` (tab, or pushed when it has a back route),
/// tokens only, pressable day tiles, a retry state when the week cannot be loaded.
/// MOB-WORKER-ORDERS-01 (#551): the jobs booked for the worker (`GET /api/workers/me/assignments`) show in the same
/// week: a marker on the day, the job under its shift (the shift is locked, it has an order), and a sheet with the
/// details and the next step (check in, continue).
class WorkerSlotsScreen extends StatefulWidget {
  final WorkersSlotsService? slotsService;
  final WorkersLocationService? locationService;
  final WorkersAssignmentsService? assignmentsService;
  final DateTime? initialDate;

  const WorkerSlotsScreen({
    super.key,
    this.slotsService,
    this.locationService,
    this.assignmentsService,
    this.initialDate,
  });

  @override
  State<WorkerSlotsScreen> createState() => _WorkerSlotsScreenState();
}

class _WorkerSlotsScreenState extends State<WorkerSlotsScreen> {
  late final WorkersSlotsService _service;
  late final WorkersLocationService _locationService;
  late final WorkersAssignmentsService _assignmentsService;
  late DateTime _weekStartDate;

  bool _isLoading = false;

  /// The week could not be loaded: the whole list is replaced by a retry state.
  String? _loadError;

  /// A switch could not be saved: the switch is put back and this line explains why.
  String? _toggleError;
  final Map<String, bool> _slotActiveState = {}; // Key: "yyyy-MM-dd:SHIFT_CODE"

  /// The jobs booked for the displayed week. A failed read leaves the switches alone and shows its own retry line.
  List<WorkerAssignmentDto> _assignments = const [];
  bool _assignmentsFailed = false;

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
    _assignmentsService =
        widget.assignmentsService ?? WorkersAssignmentsService();

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
    // The jobs load on their own: their failure must not hide the switches.
    _loadAssignments();

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

  Future<void> _loadAssignments() async {
    final weekStart = _weekStartDate;
    try {
      final response = await _assignmentsService.getAssignments(
        startDate: _formatDate(weekStart),
        endDate: _formatDate(weekStart.add(const Duration(days: 6))),
      );
      if (!mounted || weekStart != _weekStartDate) return; // an older week
      setState(() {
        _assignments = response.data ?? const [];
        _assignmentsFailed = !response.success;
      });
    } catch (_) {
      if (!mounted || weekStart != _weekStartDate) return;
      setState(() {
        _assignments = const [];
        _assignmentsFailed = true;
      });
    }
  }

  WorkerAssignmentDto? _assignmentOf(String dateStr, String shiftCode) {
    for (final a in _assignments) {
      if (a.slotDate == dateStr && a.shiftCode == shiftCode) return a;
    }
    return null;
  }

  bool _dayHasOrder(String dateStr) =>
      _assignments.any((a) => a.slotDate == dateStr);

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
      _assignments = const [];
      _assignmentsFailed = false;
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
      if (_assignments.isNotEmpty) ...[
        const SizedBox(height: NordicSpace.xs),
        Row(
          children: [
            Container(
              width: 8,
              height: 8,
              decoration: const BoxDecoration(
                color: NordicColors.accentAmber,
                shape: BoxShape.circle,
              ),
            ),
            const SizedBox(width: NordicSpace.xs),
            Text(
              'Ngày có đơn',
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.textSecondary,
              ),
            ),
          ],
        ),
      ],
      const SizedBox(height: NordicSpace.md),
      if (_assignmentsFailed) ...[
        Row(
          children: [
            Expanded(
              child: Text(
                'Chưa tải được đơn của bạn.',
                key: const Key('orders-error'),
                style: NordicTypography.bodySmall.copyWith(
                  color: NordicColors.error,
                ),
              ),
            ),
            TextButton(
              key: const Key('orders-retry'),
              onPressed: _loadAssignments,
              child: const Text('Thử lại'),
            ),
          ],
        ),
        const SizedBox(height: NordicSpace.xs),
      ],
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

  /// Seven tiles, the chosen one filled; three dots show which shifts of that day are on, an amber dot a day with an order.
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
    final hasOrder = _dayHasOrder(dateStr);
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
                  if (hasOrder)
                    Container(
                      key: Key('slots-day-order-$index'),
                      width: 6,
                      height: 6,
                      margin: const EdgeInsets.only(left: 2),
                      decoration: const BoxDecoration(
                        shape: BoxShape.circle,
                        color: NordicColors.accentAmber,
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

  /// One row per shift of the chosen day with its switch; a shift with an order shows the order under it instead of a switch.
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
    final assignment = _assignmentOf(dateStr, shiftCode);
    final booked = assignment != null;
    final isActive =
        booked || (_slotActiveState['$dateStr:$shiftCode'] ?? false);

    return Padding(
      padding: const EdgeInsets.symmetric(
        horizontal: NordicSpace.md,
        vertical: NordicSpace.xs,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
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
              if (booked)
                StatusPill(
                  'Có đơn',
                  tone: PillTone.warn,
                  textKey: Key('shift-booked-$dateStr-$shiftCode'),
                )
              else
                Switch.adaptive(
                  value: isActive,
                  activeTrackColor: NordicColors.primary,
                  onChanged: (_) => _toggleSlot(dateStr, shiftCode, isActive),
                ),
            ],
          ),
          if (assignment != null) ...[
            const SizedBox(height: NordicSpace.xs),
            _orderCard(assignment),
          ],
        ],
      ),
    );
  }

  static String _statusLabel(String status) => switch (status) {
    'ASSIGNED' => 'Đã nhận',
    'CHECKED_IN' || 'IN_PROGRESS' => 'Đang làm',
    'AWAITING_ACCEPTANCE' => 'Chờ gia chủ xác nhận',
    'COMPLETED' => 'Hoàn tất',
    _ => status,
  };

  static PillTone _statusTone(String status) => switch (status) {
    'COMPLETED' || 'ASSIGNED' => PillTone.ok,
    'CHECKED_IN' || 'IN_PROGRESS' || 'AWAITING_ACCEPTANCE' => PillTone.warn,
    _ => PillTone.neutral,
  };

  static String _tierLabel(String tier) =>
      tier == 'PREMIUM' ? 'Cao cấp' : 'Cơ bản';

  /// The job under its shift: status in words, the order code small, district, tier and what the worker receives.
  Widget _orderCard(WorkerAssignmentDto a) {
    return NordicCard(
      key: Key('order-${a.assignmentId}'),
      onTap: () => _openOrder(a),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                StatusPill(_statusLabel(a.status), tone: _statusTone(a.status)),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  '${a.district} · ${_tierLabel(a.serviceTier)}',
                  style: NordicTypography.bodyRegular,
                ),
                Text(
                  a.orderCode,
                  style: NordicTypography.bodySmall.copyWith(
                    color: NordicColors.textSecondary,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: NordicSpace.xs),
          Text(formatVnd(a.payoutAmount), style: NordicTypography.titleSmall),
        ],
      ),
    );
  }

  Widget _sheetRow(String label, String value) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: NordicSpace.xxs),
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Expanded(
            child: Text(
              label,
              style: NordicTypography.bodyRegular.copyWith(
                color: NordicColors.textSecondary,
              ),
            ),
          ),
          const SizedBox(width: NordicSpace.xs),
          Flexible(
            child: Text(
              value,
              textAlign: TextAlign.end,
              style: NordicTypography.titleSmall,
            ),
          ),
        ],
      ),
    );
  }

  /// The details of a job and, by status, the next step: check in, or continue the work. A finished job has no button.
  void _openOrder(WorkerAssignmentDto a) {
    final date = DateTime.tryParse(a.slotDate);
    final shiftName = _shifts
        .where((s) => s['code'] == a.shiftCode)
        .map((s) => s['name']!)
        .firstOrNull;
    final route = a.isNotStarted
        ? DispatchRoutes.checkIn
        : (a.isUnderWay ? WorkerRoutes.execution : null);
    final action = a.isNotStarted ? 'Đến nơi' : 'Tiếp tục ca';

    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: NordicColors.canvas,
      shape: const RoundedRectangleBorder(borderRadius: NordicRadius.sheetTop),
      builder: (sheetContext) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
            NordicSpace.gutter,
            NordicSpace.md,
            NordicSpace.gutter,
            NordicSpace.lg,
          ),
          child: Column(
            key: Key('order-sheet-${a.assignmentId}'),
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text('Chi tiết ca', style: NordicTypography.titleMedium),
              const SizedBox(height: NordicSpace.sm),
              NordicCard(
                child: Column(
                  children: [
                    if (date != null)
                      _sheetRow('Ngày', _formatDisplayDate(date)),
                    _sheetRow(
                      'Ca',
                      '${shiftName ?? a.shiftCode} · ${a.startTime} – ${a.endTime}',
                    ),
                    _sheetRow('Khu vực', a.district),
                    _sheetRow('Gói', _tierLabel(a.serviceTier)),
                    _sheetRow('Bạn nhận', formatVnd(a.payoutAmount)),
                    _sheetRow('Mã đơn', a.orderCode),
                    Padding(
                      padding: const EdgeInsets.only(top: NordicSpace.xs),
                      child: Align(
                        alignment: Alignment.centerRight,
                        child: StatusPill(
                          _statusLabel(a.status),
                          tone: _statusTone(a.status),
                        ),
                      ),
                    ),
                  ],
                ),
              ),
              if (route != null) ...[
                const SizedBox(height: NordicSpace.md),
                NordicButton(
                  key: const Key('order-sheet-action'),
                  label: action,
                  onPressed: () {
                    Navigator.of(sheetContext).pop();
                    Navigator.of(context)
                        .pushNamed(route, arguments: a.assignmentId)
                        .then((_) {
                          if (mounted) _loadWeeklySlots();
                        });
                  },
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
