import 'package:flutter/material.dart';

import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_radius.dart';
import '../../core/theme/nordic_spacing.dart';
import '../../core/theme/nordic_typography.dart';
import '../../core/widgets/avatar_initials.dart';
import '../../core/widgets/grouped_list_card.dart';
import '../../core/widgets/nordic_card.dart';
import '../../core/widgets/nordic_button.dart';
import '../../core/widgets/nordic_page.dart';
import '../../core/widgets/pressable.dart';
import '../../core/widgets/stat_tile.dart';
import '../payouts/logic/payout_logic.dart';
import '../payouts/models/payout_models.dart';
import '../payouts/services/payouts_service.dart';
import '../workers/models/booking_slot_models.dart';
import '../workers/models/worker_work_area_models.dart';
import '../workers/routes.dart';
import '../workers/services/workers_profile_service.dart';
import '../workers/services/workers_slots_service.dart';
import '../workers/services/workers_work_area_service.dart';

/// The worker's Hôm nay tab: the month's earnings and finished jobs from
/// `GET /api/workers/me/earnings`, and the next 7 days of on/off slots from `GET /api/workers/me/slots`.
/// The header shows the worker's name from `GET /api/workers/me` (`Thợ #id` until it loads). A cell of the week grid
/// opens the Lịch tab on that day. Every section loads on its own; a failed call leaves its numbers as "–".
/// Not shown (README "needs API"): the next job, booked cells, the lifetime job count.
/// MOB-UI-20 (`Mobile/DESIGN-SYSTEM-WORKER.md`): `NordicPage.tab`, tokens only, pressable cells.
class WorkerTodayTab extends StatefulWidget {
  final int workerId;
  final VoidCallback? onShowSlots;

  /// A cell of the week grid was tapped: open the schedule on that day.
  final ValueChanged<DateTime>? onShowSlotsOn;
  final VoidCallback? onShowIncome;
  final VoidCallback? onOpenOffers;
  final IPayoutsService? payouts;
  final WorkersSlotsService? slots;
  final WorkersProfileService? profile;
  final WorkersWorkAreaService? workArea;

  /// The clock, so the month and the week can be tested.
  final DateTime Function()? now;

  const WorkerTodayTab({
    super.key,
    required this.workerId,
    this.onShowSlots,
    this.onShowSlotsOn,
    this.onShowIncome,
    this.onOpenOffers,
    this.payouts,
    this.slots,
    this.profile,
    this.workArea,
    this.now,
  });

  @override
  State<WorkerTodayTab> createState() => _WorkerTodayTabState();
}

class _WorkerTodayTabState extends State<WorkerTodayTab> {
  static const _shiftCodes = [
    'SHIFT_MORNING',
    'SHIFT_AFTERNOON',
    'SHIFT_EVENING',
  ];
  static const _shiftNames = ['Sáng', 'Chiều', 'Tối'];
  static const _labelWidth = 44.0;
  static const _weekdays = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];

  late final IPayoutsService _payouts = widget.payouts ?? PayoutsService();
  late final WorkersSlotsService _slots = widget.slots ?? WorkersSlotsService();
  late final WorkersProfileService _profile =
      widget.profile ?? WorkersProfileService();
  late final DateTime _today = (widget.now ?? DateTime.now)();

  String? _fullName;
  Earnings? _earnings;

  /// Whether the worker belongs to an agency, and the registered work area; null until each is read (a failed read leaves it null).
  bool? _agencyWorker;
  WorkerWorkArea? _area;
  Set<String>? _activeSlots; // "yyyy-MM-dd:SHIFT_CODE"

  @override
  void initState() {
    super.initState();
    _load();
  }

  static String _date(DateTime d) =>
      '${d.year.toString().padLeft(4, '0')}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

  Future<void> _load() {
    final summary = _profile
        .getSummary()
        .then((s) {
          if (mounted) setState(() => _agencyWorker = s.agencyWorker);
        })
        .catchError((_) {});
    final area = _loadArea();
    final name = _profile
        .getFullName()
        .then((name) {
          if (mounted && name != null) setState(() => _fullName = name);
        })
        .catchError((_) {});
    final earnings = _payouts
        .getEarnings(monthKeyOf(_today))
        .then((e) {
          if (mounted) setState(() => _earnings = e);
        })
        .catchError((_) {});
    final slots = _slots
        .getSlots(
          startDate: _date(_today),
          endDate: _date(_today.add(const Duration(days: 6))),
        )
        .then((response) {
          if (!mounted) return;
          setState(() {
            _activeSlots = {
              for (final BookingSlotDto s in response.data ?? const [])
                if (s.isActive) '${s.slotDate}:${s.shiftCode}',
            };
          });
        })
        .catchError((_) {});
    return Future.wait([name, earnings, slots, summary, area]);
  }

  @override
  Widget build(BuildContext context) {
    final earnings = _earnings;
    final name = _fullName ?? 'Thợ #${widget.workerId}';
    return NordicPage.tab(
      title: 'Hôm nay',
      onRefresh: _load,
      headerAction: IconButton.outlined(
        key: const Key('worker-today-offers'),
        tooltip: 'Nhận việc',
        onPressed: widget.onOpenOffers,
        icon: const Icon(Icons.notifications_outlined),
      ),
      children: [
        if (_agencyWorker == false && _area != null && !_area!.isSet) ...[
          _workAreaCard(),
          const SizedBox(height: NordicSpace.md),
        ],
        Row(
          children: [
            AvatarInitials(name, size: 44),
            const SizedBox(width: NordicSpace.sm),
            Expanded(
              child: Text(
                name,
                key: const Key('worker-today-name'),
                style: NordicTypography.titleMedium,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ],
        ),
        const SizedBox(height: NordicSpace.lg),
        Row(
          children: [
            Text('Tháng ${_today.month}', style: NordicTypography.titleSmall),
            const Spacer(),
            TextButton(
              key: const Key('worker-today-income'),
              onPressed: widget.onShowIncome,
              child: const Text('Chi tiết'),
            ),
          ],
        ),
        Row(
          children: [
            Expanded(
              child: StatTile(
                key: const Key('worker-today-net'),
                icon: Icons.payments_outlined,
                label: 'Thực nhận',
                value: earnings == null ? '–' : formatVnd(earnings.netAmount),
              ),
            ),
            const SizedBox(width: NordicSpace.sm),
            Expanded(
              child: StatTile(
                key: const Key('worker-today-jobs'),
                icon: Icons.task_alt_outlined,
                label: 'Ca hoàn tất',
                value: earnings == null ? '–' : '${earnings.jobCount}',
              ),
            ),
          ],
        ),
        const SizedBox(height: NordicSpace.section),
        Row(
          children: [
            const Text('7 ngày tới', style: NordicTypography.titleSmall),
            const Spacer(),
            TextButton(
              key: const Key('worker-today-slots'),
              onPressed: widget.onShowSlots,
              child: const Text('Lịch'),
            ),
          ],
        ),
        _weekStrip(),
        const SizedBox(height: NordicSpace.section),
        GroupedListCard(
          items: [
            GroupedListItem(
              key: const Key('worker-today-offer-row'),
              icon: Icons.notifications_active_outlined,
              title: 'Nhận việc',
              subtitle: 'Việc đang chờ, đếm ngược 30 giây',
              onTap: widget.onOpenOffers,
            ),
          ],
        ),
      ],
    );
  }

  Future<void> _loadArea() {
    return (widget.workArea ?? WorkersWorkAreaService())
        .getWorkArea()
        .then((response) {
          if (mounted) setState(() => _area = response.data);
        })
        .catchError((_) {});
  }

  /// Shown to a freelancer who has not registered where they work: until then no job is offered to them (decisions D24).
  Widget _workAreaCard() {
    return NordicCard(
      isHighlighted: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              const Icon(Icons.place_outlined, color: NordicColors.primary),
              const SizedBox(width: NordicSpace.xs),
              const Expanded(
                child: Text(
                  'Chưa có nơi làm việc, bạn sẽ chưa nhận được việc',
                  key: Key('worker-today-work-area-hint'),
                  style: NordicTypography.titleSmall,
                ),
              ),
            ],
          ),
          const SizedBox(height: NordicSpace.sm),
          NordicButton(
            key: const Key('worker-today-work-area'),
            label: 'Đăng ký nơi làm việc',
            onPressed: () =>
                Navigator.of(context)
                    .pushNamed(WorkerRoutes.workArea)
                    .then((_) => _loadArea()),
          ),
        ],
      ),
    );
  }

  /// A 3 x 7 grid: rows are the shifts (Sáng, Chiều, Tối), columns the next 7 days. A green cell with a
  /// tick is a shift the worker switched on; tapping a cell opens the schedule on that day.
  Widget _weekStrip() {
    final active = _activeSlots;
    final days = [for (var i = 0; i < 7; i++) _today.add(Duration(days: i))];
    return NordicCard(
      padding: const EdgeInsets.all(NordicSpace.sm),
      child: Column(
        children: [
          Row(
            children: [
              const SizedBox(width: _labelWidth),
              for (final d in days)
                Expanded(
                  child: Column(
                    children: [
                      Text(
                        d == days.first ? 'Nay' : _weekdays[d.weekday - 1],
                        style: NordicTypography.bodySmall.copyWith(
                          fontWeight: d == days.first
                              ? FontWeight.w700
                              : FontWeight.w400,
                          color: d == days.first
                              ? NordicColors.primary
                              : NordicColors.textSecondary,
                        ),
                      ),
                      Text(
                        '${d.day}/${d.month}',
                        style: NordicTypography.bodySmall.copyWith(
                          fontWeight: d == days.first
                              ? FontWeight.w700
                              : FontWeight.w400,
                          color: d == days.first
                              ? NordicColors.textTitle
                              : NordicColors.textSecondary,
                        ),
                      ),
                    ],
                  ),
                ),
            ],
          ),
          for (var s = 0; s < _shiftCodes.length; s++)
            Padding(
              padding: const EdgeInsets.only(top: NordicSpace.xs),
              child: Row(
                children: [
                  SizedBox(
                    width: _labelWidth,
                    child: Text(
                      _shiftNames[s],
                      style: NordicTypography.bodySmall.copyWith(
                        color: NordicColors.textSecondary,
                      ),
                    ),
                  ),
                  for (final d in days) Expanded(child: _cell(d, s, active)),
                ],
              ),
            ),
          const SizedBox(height: NordicSpace.sm),
          const Row(
            children: [
              _Legend(color: NordicColors.primaryFixed, label: 'Đang rảnh'),
              SizedBox(width: NordicSpace.md),
              _Legend(color: NordicColors.surfaceContainer, label: 'Đang tắt'),
            ],
          ),
          const SizedBox(height: NordicSpace.xs),
          Align(
            alignment: Alignment.centerLeft,
            child: Text(
              'Bấm vào một ô để xem lịch ngày đó',
              style: NordicTypography.bodySmall.copyWith(
                color: NordicColors.textSecondary,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _cell(DateTime d, int shift, Set<String>? active) {
    final on = active?.contains('${_date(d)}:${_shiftCodes[shift]}') ?? false;
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: NordicSpace.xxs),
      child: Pressable(
        child: InkWell(
          key: Key('week-cell-${_date(d)}-${_shiftCodes[shift]}'),
          borderRadius: NordicRadius.tileAll,
          onTap: widget.onShowSlotsOn == null
              ? null
              : () => widget.onShowSlotsOn!(d),
          child: Container(
            height: 32,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: on
                  ? NordicColors.primaryFixed
                  : NordicColors.surfaceContainer,
              borderRadius: NordicRadius.tileAll,
            ),
            child: on
                ? const Icon(Icons.check, size: 16, color: NordicColors.primary)
                : null,
          ),
        ),
      ),
    );
  }
}

class _Legend extends StatelessWidget {
  final Color color;
  final String label;

  const _Legend({required this.color, required this.label});

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Container(
          width: 10,
          height: 10,
          decoration: BoxDecoration(color: color, shape: BoxShape.circle),
        ),
        const SizedBox(width: NordicSpace.xs),
        Text(
          label,
          style: NordicTypography.bodySmall.copyWith(
            color: NordicColors.textSecondary,
          ),
        ),
      ],
    );
  }
}
