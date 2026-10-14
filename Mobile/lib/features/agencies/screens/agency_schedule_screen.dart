import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
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

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: const Text('Lịch ca của tôi')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.all(16.0),
              children: [
                const EyebrowBadge(text: 'Thợ Agency · 14 ngày tới'),
                const SizedBox(height: 12.0),
                const Text(
                  'Ca doanh nghiệp xếp cho bạn',
                  style: NordicTypography.h2,
                ),
                const SizedBox(height: 4.0),
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
          child: Padding(
            padding: EdgeInsets.all(16.0),
            child: Text(
              'Chưa có ca nào trong 14 ngày tới.',
              key: Key('schedule-empty'),
              style: NordicTypography.bodyRegular,
            ),
          ),
        ),
      ];
    }
    return [
      for (final day in _days) ...[
        Text(day.title, style: NordicTypography.h3),
        const SizedBox(height: 8.0),
        for (final slot in day.slots)
          Padding(
            padding: const EdgeInsets.only(bottom: 8.0),
            child: NordicCard(
              key: Key('slot-${slot.slotId}'),
              child: Padding(
                padding: const EdgeInsets.all(12.0),
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        scheduleShiftName(slot.shiftCode),
                        style: NordicTypography.labelMedium,
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
          ),
        const SizedBox(height: 8.0),
      ],
    ];
  }
}
