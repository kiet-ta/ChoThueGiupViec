// Pure logic of the agency staff schedule (MOB-M5-01): grouping the worker's slots by day.
import '../../workers/models/booking_slot_models.dart';

/// `SHIFT_MORNING` -> `Ca sáng` (the codes of BOOKING_SLOT.shift_code).
String scheduleShiftName(String shiftCode) => switch (shiftCode) {
  'SHIFT_MORNING' => 'Ca sáng',
  'SHIFT_AFTERNOON' => 'Ca chiều',
  'SHIFT_EVENING' => 'Ca tối',
  _ => shiftCode,
};

/// `T2`, `T3`, ..., `CN` for a date (Monday = 1).
String weekdayShort(DateTime date) =>
    const ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'][date.weekday - 1];

/// `T4 14/10` for a `yyyy-MM-dd` day; the text itself when it does not parse.
String dayTitle(String slotDate) {
  final date = DateTime.tryParse(slotDate);
  if (date == null) return slotDate;
  final d = date.day.toString().padLeft(2, '0');
  final m = date.month.toString().padLeft(2, '0');
  return '${weekdayShort(date)} $d/$m';
}

/// `08:00 – 12:00` from the API's `HH:mm` (a longer `HH:mm:ss` is cut to minutes).
String timeRange(String start, String end) {
  String hm(String t) => t.length >= 5 ? t.substring(0, 5) : t;
  return '${hm(start)} – ${hm(end)}';
}

/// One day of the schedule with its shifts in clock order.
class ScheduleDay {
  final String slotDate;
  final String title;
  final List<BookingSlotDto> slots;

  const ScheduleDay({
    required this.slotDate,
    required this.title,
    required this.slots,
  });
}

/// Days in date order; inside a day the shifts by start time. Slots the worker switched off (`isActive` false) are not shifts the
/// agency asked for, so they are left out.
List<ScheduleDay> groupSchedule(List<BookingSlotDto> slots) {
  final byDay = <String, List<BookingSlotDto>>{};
  for (final slot in slots.where((s) => s.isActive)) {
    byDay.putIfAbsent(slot.slotDate, () => []).add(slot);
  }
  final days = byDay.keys.toList()..sort();
  return [
    for (final day in days)
      ScheduleDay(
        slotDate: day,
        title: dayTitle(day),
        slots: byDay[day]!..sort((a, b) => a.startTime.compareTo(b.startTime)),
      ),
  ];
}

/// `yyyy-MM-dd` of a date.
String isoDate(DateTime date) =>
    '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';

/// How many days the screen looks ahead (today and the 13 days after).
const int scheduleDays = 14;
