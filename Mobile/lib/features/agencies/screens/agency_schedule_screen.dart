import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/stat_tile.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../workers/models/booking_slot_models.dart';
import '../../workers/services/workers_slots_service.dart';
import '../logic/agency_schedule_logic.dart';

/// MOB-M5-01: the shifts the agency put the worker on for the next 14 days (the roster, contract agencies.md 5), read with the
/// worker's own slots endpoint (workers.md 2.3.1). The agency edits the roster on the Partner Portal; the worker only reads it here.
class AgencyScheduleScreen extends StatefulWidget {
  final WorkersSlotsService? slotsService;

  /// Today; injectable so a test is not tied to the clock.
  final DateTime? today;

  const AgencyScheduleScreen({super.key, this.slotsService, this.today});

  @override
  State<AgencyScheduleScreen> createState() => _AgencyScheduleScreenState();
}

class _AgencyScheduleScreenState extends State<AgencyScheduleScreen> {
  late final WorkersSlotsService _service;
  List<ScheduleDay> _days = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _service = widget.slotsService ?? WorkersSlotsService();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    final today = widget.today ?? DateTime.now();
    try {
      final response = await _service.getSlots(
        startDate: isoDate(today),
        endDate: isoDate(today.add(const Duration(days: scheduleDays - 1))),
      );
      if (!mounted) return;
      setState(() {
        _loading = false;
        if (response.success) {
          _days = groupSchedule(response.data ?? <BookingSlotDto>[]);
        } else {
          _error = response.message.isEmpty
              ? 'Không tải được lịch ca.'
              : response.message;
        }
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'Không kết nối được máy chủ.';
      });
    }
  }

  DateTime get _today => widget.today ?? DateTime.now();

  /// Monday of the week of [_today], at midnight.
  DateTime get _weekStart {
    final t = _today;
    return DateTime(t.year, t.month, t.day - (t.weekday - 1));
  }

  Set<String> get _rostered => {
    for (final day in _days)
      for (final slot in day.slots) '${slot.slotDate}:${slot.shiftCode}',
  };

  @override
  Widget build(BuildContext context) {
    final pushed = ModalRoute.of(context)?.canPop ?? false;
    return Scaffold(
      appBar: pushed ? AppBar(title: const Text('Lịch ca của tôi')) : null,
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 390.0),
            child: RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 32.0),
                children: [
                  if (!pushed)
                    const Padding(
                      padding: EdgeInsets.only(top: 8, bottom: 16),
                      child: Text(
                        'Lịch ca của tôi',
                        style: NordicTypography.screenTitle,
                      ),
                    ),
                  const Text(
                    'Doanh nghiệp xếp ca trên Cổng đối tác. Bạn chỉ xem ở đây; cần đổi ca, hãy liên hệ doanh nghiệp.',
                    style: NordicTypography.bodySmall,
                  ),
                  const SizedBox(height: 16.0),
                  ..._content(),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  List<Widget> _content() {
    if (_loading) {
      return const [
        Padding(
          padding: EdgeInsets.all(24.0),
          child: Center(
            child: CircularProgressIndicator(key: Key('schedule-loading')),
          ),
        ),
      ];
    }
    if (_error != null) {
      return [
        Text(
          _error!,
          key: const Key('schedule-error'),
          style: NordicTypography.bodyRegular.copyWith(
            color: NordicColors.error,
          ),
        ),
        const SizedBox(height: 12.0),
        NordicButton(
          key: const Key('schedule-retry'),
          label: 'Thử lại',
          variant: NordicButtonVariant.secondary,
          onPressed: _load,
        ),
      ];
    }
    if (_days.isEmpty) {
      return const [
        NordicCard(
          child: Text(
            'Chưa có ca nào trong 14 ngày tới.',
            key: Key('schedule-empty'),
            style: NordicTypography.bodyRegular,
          ),
        ),
      ];
    }
    final weekStart = _weekStart;
    final weekEnd = weekStart.add(const Duration(days: 7));
    final total = _days.fold<int>(0, (sum, d) => sum + d.slots.length);
    final thisWeek = _days
        .where((d) {
          final date = DateTime.tryParse(d.slotDate);
          return date != null &&
              !date.isBefore(weekStart) &&
              date.isBefore(weekEnd);
        })
        .fold<int>(0, (sum, d) => sum + d.slots.length);
    return [
      Row(
        children: [
          Expanded(
            child: StatTile(
              key: const Key('schedule-week-count'),
              label: 'Ca tuần này',
              value: '$thisWeek',
            ),
          ),
          const SizedBox(width: 12.0),
          Expanded(
            child: StatTile(
              key: const Key('schedule-total-count'),
              label: 'Ca 14 ngày',
              value: '$total',
            ),
          ),
        ],
      ),
      const SizedBox(height: 24.0),
      _weekGrid(weekStart),
      const SizedBox(height: 24.0),
      const Text('Ca kế tiếp', style: NordicTypography.titleSmall),
      const SizedBox(height: 8.0),
      for (final day in _days) ...[
        Padding(
          padding: const EdgeInsets.only(bottom: 4.0),
          child: Text(day.title, style: NordicTypography.caps),
        ),
        for (final slot in day.slots)
          Padding(
            padding: const EdgeInsets.only(bottom: 8.0),
            child: NordicCard(
              key: Key('slot-${slot.slotId}'),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      scheduleShiftName(slot.shiftCode),
                      style: NordicTypography.titleSmall,
                    ),
                  ),
                  Text(
                    timeRange(slot.startTime, slot.endTime),
                    style: NordicTypography.bodyRegular,
                  ),
                ],
              ),
            ),
          ),
        const SizedBox(height: 4.0),
      ],
    ];
  }

  /// `.grid7`: Sáng / Chiều / Tối by weekday of this week; a filled cell is a shift the agency put the worker on.
  Widget _weekGrid(DateTime weekStart) {
    const shifts = [
      ('Sáng', 'SHIFT_MORNING'),
      ('Chiều', 'SHIFT_AFTERNOON'),
      ('Tối', 'SHIFT_EVENING'),
    ];
    final rostered = _rostered;
    final days = [for (var i = 0; i < 7; i++) weekStart.add(Duration(days: i))];
    return NordicCard(
      child: Column(
        children: [
          Row(
            children: [
              const SizedBox(width: 40.0),
              for (final d in days)
                Expanded(
                  child: Center(
                    child: Text(
                      weekdayShort(d),
                      style: const TextStyle(
                        fontSize: 11.0,
                        color: NordicColors.textSecondary,
                      ),
                    ),
                  ),
                ),
            ],
          ),
          for (final (label, code) in shifts)
            Padding(
              padding: const EdgeInsets.only(top: 5.0),
              child: Row(
                children: [
                  SizedBox(
                    width: 40.0,
                    child: Text(
                      label,
                      style: const TextStyle(
                        fontSize: 11.0,
                        color: NordicColors.textSecondary,
                      ),
                    ),
                  ),
                  for (final d in days)
                    Expanded(
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 2.5),
                        child: Container(
                          key: Key('roster-cell-${isoDate(d)}-$code'),
                          height: 34.0,
                          decoration: BoxDecoration(
                            color: rostered.contains('${isoDate(d)}:$code')
                                ? NordicColors.primary
                                : NordicColors.surfaceContainer,
                            borderRadius: BorderRadius.zero,
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          const SizedBox(height: 12.0),
          const Row(
            children: [
              _Legend(color: NordicColors.primary, label: 'Được xếp'),
              SizedBox(width: 16.0),
              _Legend(color: NordicColors.surfaceContainer, label: 'Trống'),
            ],
          ),
        ],
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
          width: 10.0,
          height: 10.0,
          decoration: BoxDecoration(
            color: color,
            borderRadius: BorderRadius.zero,
          ),
        ),
        const SizedBox(width: 6.0),
        Text(
          label,
          style: const TextStyle(
            fontSize: 11.0,
            color: NordicColors.textSecondary,
          ),
        ),
      ],
    );
  }
}
