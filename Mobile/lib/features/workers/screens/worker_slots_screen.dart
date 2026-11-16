import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
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
import '../../dispatch/models/worker_cancel.dart';
import '../../dispatch/routes.dart';
import '../../dispatch/services/dispatch_service.dart';
import '../../dispatch/widgets/worker_cancel_section.dart';
import '../../payouts/logic/payout_logic.dart';
import '../models/booking_slot_models.dart';
import '../models/worker_assignment_models.dart';
import '../routes.dart';
import '../services/map_launcher.dart';
import '../services/workers_assignments_service.dart';
import '../services/workers_location_service.dart';
import '../services/workers_profile_service.dart';
import '../services/workers_slots_service.dart';

/// Screen for managing Worker Weekly Availability Block Slots (contract workers.md §2.3).
/// Implements MOB-M4-02: weekly grid toggle for Sáng/Chiều/Tối shifts.
/// MOB-UI-20 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage` (tab, or pushed when it has a back route),
/// tokens only, pressable day tiles, a retry state when the week cannot be loaded.
/// MOB-WORKER-ORDERS-01 (#551): the jobs booked for the worker (`GET /api/v1/workers/me/assignments`) show in the same
/// week: a marker on the day, the job under its shift (the shift is locked, it has an order), and a sheet with the
/// details and the next step (check in, continue).
class WorkerSlotsScreen extends StatefulWidget {
  final WorkersSlotsService? slotsService;
  final WorkersLocationService? locationService;
  final WorkersAssignmentsService? assignmentsService;

  /// Cancels an accepted job from its sheet (`Huỷ ca`, MOB-SLOT-CANCEL-01); replaceable in tests.
  final DispatchService? dispatchService;

  /// Opens the phone's map app for `Chỉ đường`.
  final IMapLauncher? mapLauncher;

  /// Tells an agency worker from a freelancer (D43): an agency worker only reads the roster the agency set; replaceable in tests.
  final WorkersProfileService? profileService;
  final DateTime? initialDate;

  const WorkerSlotsScreen({
    super.key,
    this.slotsService,
    this.locationService,
    this.assignmentsService,
    this.dispatchService,
    this.mapLauncher,
    this.profileService,
    this.initialDate,
  });

  @override
  State<WorkerSlotsScreen> createState() => _WorkerSlotsScreenState();
}

class _WorkerSlotsScreenState extends State<WorkerSlotsScreen> {
  late final WorkersSlotsService _service;
  late final WorkersLocationService _locationService;
  late final WorkersAssignmentsService _assignmentsService;
  late final DispatchService _dispatchService;
  late final IMapLauncher _mapLauncher;
  late DateTime _weekStartDate;

  /// "Today" of the screen (the device's local date, or `initialDate` in tests). Days before it are read-only (MOB-SLOTS-ERR-01).
  late final String _todayStr;

  bool _isLoading = false;

  /// D43: the worker belongs to an agency, so the shifts are the roster the agency set and cannot be switched here.
  /// Until the profile says so (or if it cannot be read) the screen behaves as a freelancer's.
  bool _agencyWorker = false;

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
    _dispatchService = widget.dispatchService ?? DispatchService();
    _mapLauncher = widget.mapLauncher ?? const UrlMapLauncher();

    final now = widget.initialDate ?? DateTime.now();
    _todayStr = _formatDate(now);
    // Align to Monday of current week
    _weekStartDate = now.subtract(Duration(days: now.weekday - 1));
    _selectedDay = now.weekday - 1;
    _loadWeeklySlots();
    _shareLocation();
    _loadAgencyFlag();
  }

  Future<void> _loadAgencyFlag() async {
    try {
      final summary = await (widget.profileService ?? WorkersProfileService())
          .getSummary();
      if (mounted && summary.agencyWorker) {
        setState(() => _agencyWorker = true);
      }
    } catch (_) {
      // Stays a freelancer's screen; the server still decides what a switch may do.
    }
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

  /// A day before today: the server refuses to change it (400 `slotDate`), so the app does not offer it.
  bool _isPastDay(String dateStr) => dateStr.compareTo(_todayStr) < 0;

  /// A shift of today whose start time has passed (08:00 / 13:00 / 17:30): the server refuses it too (409 `SHIFT_ALREADY_STARTED`).
  bool _isShiftStarted(String dateStr, Map<String, String> shift) {
    if (dateStr != _todayStr) return false;
    final start = shift['time']!.substring(0, 5).split(':');
    final now = widget.initialDate ?? DateTime.now();
    return now.hour * 60 + now.minute >=
        int.parse(start[0]) * 60 + int.parse(start[1]);
  }

  /// One plain sentence for a failed toggle, never the raw server text (MOB-SLOTS-ERR-01).
  static String _toggleErrorMessage(Object error) {
    if (error is ApiException) {
      if (error.fieldErrors?.containsKey('slotDate') ?? false) {
        return 'Ngày này đã qua nên không thể thay đổi lịch rảnh. Hãy chọn từ hôm nay trở đi.';
      }
      if (error.code == 'SHIFT_ALREADY_STARTED') {
        return 'Ca này đã bắt đầu hoặc đã qua giờ nên không thể bật hoặc tắt.';
      }
      if (error.code == 'SLOT_IN_USE') {
        return 'Ca này đang có đơn (đang mời hoặc đã nhận) nên không thể bật hoặc tắt.';
      }
      if (error.statusCode == 0) {
        return 'Không có kết nối mạng. Vui lòng thử lại.';
      }
    }
    return 'Không thể thay đổi lịch rảnh lúc này. Vui lòng thử lại sau.';
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

  /// Every job of the worker in this shift, earliest first: one shift can take several orders (SHIFT-MULTI-01, decisions D42).
  List<WorkerAssignmentDto> _assignmentsOf(String dateStr, String shiftCode) {
    final jobs = [
      for (final a in _assignments)
        if (a.slotDate == dateStr && a.shiftCode == shiftCode) a,
    ];
    // "HH:mm" sorts as text; the id keeps two jobs with the same start in a stable order.
    jobs.sort((a, b) {
      final byTime = a.startTime.compareTo(b.startTime);
      return byTime != 0 ? byTime : a.assignmentId.compareTo(b.assignmentId);
    });
    return jobs;
  }

  bool _dayHasOrder(String dateStr) =>
      _assignments.any((a) => a.slotDate == dateStr);

  Future<void> _toggleSlot(
    String dateStr,
    String shiftCode,
    bool currentActive,
  ) async {
    if (_agencyWorker) return; // D43: the agency's roster, nothing to switch
    // The shift's time is over: say so, change nothing and send nothing (the server would refuse it too).
    final shift = _shifts.firstWhere((x) => x['code'] == shiftCode);
    if (_isPastDay(dateStr)) {
      setState(
        () => _toggleError =
            'Không thể bật hoặc tắt vì đã qua thời gian của ca (ngày ${dateStr.substring(8)}/${dateStr.substring(5, 7)} đã qua).',
      );
      return;
    }
    if (_isShiftStarted(dateStr, shift)) {
      setState(
        () => _toggleError =
            'Không thể bật hoặc tắt vì ${shift['name']!.toLowerCase()} (${shift['time']}) đã bắt đầu hoặc đã qua giờ.',
      );
      return;
    }

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
        _toggleError = _toggleErrorMessage(e);
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
      if (_agencyWorker) ...[
        StatTile(
          key: const Key('slots-rostered'),
          label: 'Ca doanh nghiệp xếp',
          value: '$_activeInWeek',
        ),
        const SizedBox(height: NordicSpace.xs),
        Text(
          'Lịch này do doanh nghiệp của bạn xếp nên bạn chỉ xem được. Cần đổi ca thì liên hệ doanh nghiệp.',
          key: const Key('slots-agency-hint'),
          style: NordicTypography.bodySmall.copyWith(
            color: NordicColors.textSecondary,
          ),
        ),
      ] else
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

    final title = _agencyWorker ? 'Lịch làm việc' : 'Lịch rảnh';
    return pushed
        ? NordicPage.pushed(
            title: title,
            onRefresh: _loadWeeklySlots,
            children: children,
          )
        : NordicPage.tab(
            title: title,
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
        if (_isPastDay(dateStr) && !_agencyWorker)
          Padding(
            padding: const EdgeInsets.only(bottom: NordicSpace.xs),
            child: Text(
              'Ngày này đã qua, không thể thay đổi lịch rảnh.',
              key: const Key('slots-past-day-hint'),
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.textSecondary,
              ),
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
    final assignments = _assignmentsOf(dateStr, shiftCode);
    final booked = assignments.isNotEmpty;
    final started = _isShiftStarted(dateStr, shift);
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
                    if (started && !booked && !_agencyWorker)
                      Text(
                        'Ca đã bắt đầu hoặc đã qua giờ, không thể bật hoặc tắt.',
                        key: Key('shift-started-$dateStr-$shiftCode'),
                        style: NordicTypography.bodySmall.copyWith(
                          color: NordicColors.error,
                        ),
                      ),
                  ],
                ),
              ),
              if (booked)
                StatusPill(
                  assignments.length == 1
                      ? 'Có đơn'
                      : 'Có ${assignments.length} đơn',
                  tone: PillTone.warn,
                  textKey: Key('shift-booked-$dateStr-$shiftCode'),
                )
              else if (_agencyWorker)
                // D43: read-only. The word carries the meaning, not only the colour.
                StatusPill(
                  isActive ? 'Doanh nghiệp xếp ca' : 'Chưa có ca',
                  tone: isActive ? PillTone.ok : PillTone.neutral,
                  textKey: Key('shift-roster-$dateStr-$shiftCode'),
                )
              else
                Switch.adaptive(
                  value: isActive,
                  activeTrackColor: NordicColors.primary,
                  // A locked shift keeps its switch tappable: the tap explains why nothing changes (see _toggleSlot).
                  onChanged: (_) => _toggleSlot(dateStr, shiftCode, isActive),
                ),
            ],
          ),
          for (final assignment in assignments) ...[
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

  /// `Chỉ đường`: reads the full address of the accepted job (the list has only the district) and hands it to the phone's
  /// map app, which draws the road (ROUTE-MOB-01, #760). A job that cannot be read or a phone without a map app says so.
  Future<void> _openDirections(int assignmentId) async {
    final messenger = ScaffoldMessenger.of(context);
    Uri? uri;
    try {
      final job = (await _assignmentsService.getAssignment(assignmentId)).data;
      if (job != null) {
        uri = mapDirectionsUri(
          latitude: job.latitude,
          longitude: job.longitude,
          address: job.fullAddress,
        );
      }
    } catch (_) {
      messenger.showSnackBar(
        const SnackBar(
          content: Text('Chưa tải được địa chỉ của ca. Vui lòng thử lại.'),
        ),
      );
      return;
    }
    if (uri == null) {
      messenger.showSnackBar(
        const SnackBar(content: Text('Ca này chưa có địa chỉ để chỉ đường.')),
      );
      return;
    }
    final opened = await _mapLauncher.open(uri);
    if (!opened) {
      messenger.showSnackBar(
        const SnackBar(
          content: Text(
            'Không mở được ứng dụng bản đồ. Hãy cài một ứng dụng bản đồ rồi thử lại.',
          ),
        ),
      );
    }
  }

  /// Closes the sheet, tells the worker what happened (MOB-SLOT-CANCEL-01; D43 for an agency worker) and reloads the week.
  void _afterCancel(BuildContext sheetContext, WorkerCancelResult result) {
    Navigator.of(sheetContext).pop();
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          key: const Key('worker-cancelled-notice'),
          duration: const Duration(seconds: 8),
          content: Text(workerCancelNotice(result)),
        ),
      );
    _loadWeeklySlots();
  }

  /// The details of a job and, by status, the next step: check in, or continue the work. A finished job has no button.
  void _openOrder(WorkerAssignmentDto a) {
    final date = DateTime.tryParse(a.slotDate);
    final shiftName = _shifts
        .where((s) => s['code'] == a.shiftCode)
        .map((s) => s['name']!)
        .firstOrNull;
    // ACCEPT-02: a job handed over to the customer opens the photo screen, which shows the acceptance phase and deadline.
    final awaiting = a.status == 'AWAITING_ACCEPTANCE';
    final route = a.isNotStarted
        ? DispatchRoutes.checkIn
        : (a.isUnderWay
              ? WorkerRoutes.execution
              : (awaiting ? WorkerRoutes.photos : null));
    final action = a.isNotStarted
        ? 'Đến nơi'
        : (awaiting ? 'Xem nghiệm thu' : 'Tiếp tục ca');

    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: NordicColors.canvas,
      shape: const RoundedRectangleBorder(borderRadius: NordicRadius.sheetTop),
      builder: (sheetContext) => SafeArea(
        child: SingleChildScrollView(
          padding: EdgeInsets.fromLTRB(
            NordicSpace.gutter,
            NordicSpace.md,
            NordicSpace.gutter,
            NordicSpace.lg + MediaQuery.of(sheetContext).viewInsets.bottom,
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
              if (a.isNotStarted) ...[
                const SizedBox(height: NordicSpace.md),
                NordicButton(
                  key: const Key('order-sheet-route'),
                  label: 'Chỉ đường',
                  variant: NordicButtonVariant.secondary,
                  icon: const Icon(Icons.navigation_outlined, size: 18.0),
                  onPressed: () {
                    Navigator.of(sheetContext).pop();
                    _openDirections(a.assignmentId);
                  },
                ),
              ],
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
              // Decisions D35: an accepted job that has not started can be cancelled, here as well as on the check-in screen.
              if (a.isNotStarted) ...[
                const SizedBox(height: NordicSpace.md),
                WorkerCancelSection(
                  assignmentId: a.assignmentId,
                  service: _dispatchService,
                  onCancelled: (result) => _afterCancel(sheetContext, result),
                  // D43: an agency worker's cancel only told the agency; the job stays in the list until the agency swaps it.
                  onAgencyNotified: (result) =>
                      _afterCancel(sheetContext, result),
                  onReportIncident: () {
                    Navigator.of(sheetContext).pop();
                    Navigator.of(context).pushNamed(
                      DispatchRoutes.incidentReport,
                      arguments: a.assignmentId,
                    );
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
