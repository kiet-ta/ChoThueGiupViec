import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../customers/routes.dart';
import '../../payments/models/payment_models.dart';
import '../../payments/routes.dart';
import '../logic/booking_logic.dart';
import '../models/booking_models.dart';
import '../routes.dart';
import '../services/booking_service.dart';

/// Booking flow of a customer (MOB-M2-01 choose the segment, MOB-M2-02 address -> day and shift -> note -> fixed price -> order);
/// contract booking.md 3.1 to 3.3. The price is the server's quote, shown before the order exists; the device computes no money.
class BookingScreen extends StatefulWidget {
  final IBookingService? service;
  final IBookingAddressSource? addresses;

  /// The clock, so the selectable days can be tested; the real screen uses the device time.
  final DateTime Function()? now;

  /// What happens with the new order; by default the payment screen opens for it.
  final void Function(BookingOrder order)? onOrderCreated;

  /// What "Thêm địa chỉ" does; by default the Customers feature's address book opens.
  final VoidCallback? onManageAddresses;

  const BookingScreen({
    super.key,
    this.service,
    this.addresses,
    this.now,
    this.onOrderCreated,
    this.onManageAddresses,
  });

  @override
  State<BookingScreen> createState() => _BookingScreenState();
}

class _BookingScreenState extends State<BookingScreen> {
  late final IBookingService _service;
  late final IBookingAddressSource _addressSource;
  late final List<DateTime> _days;

  final _noteController = TextEditingController();

  BookingOptions? _options;
  List<BookingAddress>? _addresses;
  String? _loadError;
  bool _loading = true;

  String? _tier;
  int? _addressId;
  DateTime? _day;
  String? _shiftCode;

  PriceQuote? _quote;
  String? _quoteError;
  bool _quoteLoading = false;
  int _quoteRequest = 0; // an older quote must not overwrite a newer choice

  bool _submitting = false; // one order per tap
  CreateOrderFailure? _failure;
  String? _hint;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? BookingService();
    _addressSource = widget.addresses ?? CustomerAddressBookSource();
    _days = selectableDates((widget.now ?? DateTime.now)());
    _load();
  }

  @override
  void dispose() {
    _noteController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _loadError = null;
    });
    try {
      final results = await Future.wait([
        _service.getOptions(),
        _addressSource.getAddresses(),
      ]);
      if (!mounted) return;
      final addresses = results[1] as List<BookingAddress>;
      setState(() {
        _options = results[0] as BookingOptions;
        _addresses = addresses;
        // The default address is preselected; the customer can change it.
        _addressId ??= addresses
            .where((a) => a.isDefault)
            .map((a) => a.addressId)
            .firstOrNull;
        _loading = false;
      });
      _refreshQuote();
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _loadError = loadFailureMessage(e);
        _loading = false;
      });
    }
  }

  Future<void> _refreshQuote() async {
    final tier = _tier;
    final addressId = _addressId;
    final id = ++_quoteRequest;
    if (tier == null || addressId == null) {
      setState(() {
        _quote = null;
        _quoteError = null;
        _quoteLoading = false;
      });
      return;
    }
    setState(() {
      _quoteLoading = true;
      _quoteError = null;
    });
    try {
      final quote = await _service.getQuote(
        addressId: addressId,
        serviceTier: tier,
      );
      if (!mounted || id != _quoteRequest) return;
      setState(() {
        _quote = quote;
        _quoteLoading = false;
      });
    } on ApiException catch (e) {
      if (!mounted || id != _quoteRequest) return;
      setState(() {
        _quote = null;
        _quoteError = loadFailureMessage(e);
        _quoteLoading = false;
      });
    }
  }

  void _chooseTier(String tier) {
    setState(() {
      _tier = tier;
      _failure = null;
      _hint = null;
    });
    _refreshQuote();
  }

  void _chooseAddress(int addressId) {
    setState(() {
      _addressId = addressId;
      _failure = null;
      _hint = null;
    });
    _refreshQuote();
  }

  Future<void> _submit() async {
    if (_submitting) return;
    final noteError = validateNote(_noteController.text);
    final missing = missingChoice(
      tier: _tier,
      addressId: _addressId,
      day: _day,
      shiftCode: _shiftCode,
      hasQuote: _quote != null,
    );
    if (missing != null || noteError != null) {
      setState(() {
        _hint = missing;
        _failure = CreateOrderFailure(fields: {'customerNote': ?noteError});
      });
      return;
    }

    final note = _noteController.text.trim();
    final input = CreateOrderInput(
      addressId: _addressId!,
      serviceTier: _tier!,
      scheduledDate: dateKey(_day!),
      shiftCode: _shiftCode!,
      customerNote: note.isEmpty ? null : note,
    );

    setState(() {
      _submitting = true;
      _failure = null;
      _hint = null;
    });
    try {
      final order = await _service.createOrder(input);
      if (!mounted) return;
      setState(() => _submitting = false);
      _opened(order);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _submitting = false;
        _failure = createOrderFailure(
          e,
          premiumMinLeadHours: _options?.premiumMinLeadHours ?? 4,
        );
      });
    }
  }

  void _opened(BookingOrder order) {
    if (widget.onOrderCreated != null) return widget.onOrderCreated!(order);
    Navigator.of(context)
        .pushNamed(
          PaymentRoutes.payment,
          arguments: PaymentScreenArgs(
            orderId: order.orderId,
            orderCode: order.orderCode,
          ),
        )
        .then((_) {
          // Paid or not, the order exists: the customer follows it (and can pay or cancel it) on its own screen.
          if (mounted) {
            Navigator.of(context).pushReplacementNamed(
              BookingRoutes.bookingDetail,
              arguments: OrderDetailArgs(orderId: order.orderId),
            );
          }
        });
  }

  void _manageAddresses() {
    if (widget.onManageAddresses != null) return widget.onManageAddresses!();
    Navigator.of(context).pushNamed(CustomerRoutes.addressList).then((_) {
      if (mounted) _load(); // the address book may have changed
    });
  }

  @override
  Widget build(BuildContext context) {
    final quote = _quote;
    final ready = _options != null && _loadError == null;
    return Scaffold(
      appBar: AppBar(leading: const CloseButton(), title: const Text('Đặt ca')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _content(),
        ),
      ),
      // ui-templates `customer/booking.html`: the server's total and the order button stay at the bottom.
      bottomNavigationBar: ready
          ? BottomActionBar(
              children: [
                Column(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      quote == null
                          ? 'Chưa có giá'
                          : '${quote.requiredWorkers} thợ × ${formatVnd(quote.unitPrice)}',
                      style: const TextStyle(
                        fontSize: 11.0,
                        color: NordicColors.textSecondary,
                      ),
                    ),
                    Text(
                      quote == null ? '–' : formatVnd(quote.totalAmount),
                      style: NordicTypography.numberSmall,
                    ),
                  ],
                ),
                NordicButton(
                  key: const Key('submit-order'),
                  label: 'Đặt đơn',
                  isLoading: _submitting,
                  onPressed: _submitting ? null : _submit,
                ),
              ],
            )
          : null,
    );
  }

  Widget _content() {
    if (_loading && _options == null) {
      return const LoadingStateWidget(message: 'Đang tải lựa chọn đặt đơn…');
    }
    if (_loadError != null) {
      return ErrorStateWidget(
        title: 'Chưa tải được',
        message: _loadError!,
        onRetry: _load,
      );
    }
    final options = _options!;
    final addresses = _addresses ?? const <BookingAddress>[];
    final monthOfFirstDay = _days.isEmpty ? '' : 'Tháng ${_days.first.month}';

    return ListView(
      padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 24.0),
      children: [
        _heading('GÓI'),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (var i = 0; i < options.serviceTiers.length; i++) ...[
              if (i > 0) const SizedBox(width: 12.0),
              Expanded(
                child: _TierTile(
                  key: Key('tier-${options.serviceTiers[i]}'),
                  selected: _tier == options.serviceTiers[i],
                  title: tierLabel(options.serviceTiers[i]),
                  subtitle: tierDescription(options.serviceTiers[i], options),
                  onTap: () => _chooseTier(options.serviceTiers[i]),
                ),
              ),
            ],
          ],
        ),
        _fieldError('serviceTier'),

        _heading('ĐỊA CHỈ', top: 24.0),
        if (addresses.isEmpty)
          EmptyStateWidget(
            title: 'Bạn chưa có địa chỉ nào',
            subtitle: 'Thêm địa chỉ để hệ thống tính diện tích và báo giá.',
            icon: Icons.home_outlined,
            action: NordicButton(
              label: 'Thêm địa chỉ',
              onPressed: _manageAddresses,
            ),
          )
        else ...[
          for (final address in addresses) ...[
            _ChoiceCard(
              key: Key('address-${address.addressId}'),
              selected: _addressId == address.addressId,
              title: address.label,
              subtitle:
                  '${address.addressLine} · ${formatArea(address.totalAreaM2)}',
              onTap: () => _chooseAddress(address.addressId),
            ),
            const SizedBox(height: 8.0),
          ],
          Align(
            alignment: Alignment.centerLeft,
            child: TextButton.icon(
              onPressed: _manageAddresses,
              icon: const Icon(Icons.location_on_outlined, size: 18.0),
              label: const Text('Sổ địa chỉ'),
            ),
          ),
        ],
        _fieldError('addressId'),

        _heading('NGÀY', top: 16.0, trailing: monthOfFirstDay),
        SizedBox(
          key: const Key('day-list'),
          height: 64.0,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            itemCount: _days.length,
            separatorBuilder: (_, _) => const SizedBox(width: 8.0),
            itemBuilder: (_, i) {
              final day = _days[i];
              return _DayTile(
                key: Key('day-${dateKey(day)}'),
                day: day,
                selected: _day == day,
                onTap: () => setState(() {
                  _day = day;
                  _failure = null;
                  _hint = null;
                }),
              );
            },
          ),
        ),
        _fieldError('scheduledDate'),

        _heading('CA', top: 24.0),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (var i = 0; i < options.shifts.length; i++) ...[
              if (i > 0) const SizedBox(width: 8.0),
              Expanded(
                child: _ShiftTile(
                  key: Key('shift-${options.shifts[i].shiftCode}'),
                  shift: options.shifts[i],
                  selected: _shiftCode == options.shifts[i].shiftCode,
                  onTap: () => setState(() {
                    _shiftCode = options.shifts[i].shiftCode;
                    _failure = null;
                    _hint = null;
                  }),
                ),
              ),
            ],
          ],
        ),
        _fieldError('shiftCode'),

        _heading('GHI CHÚ', top: 24.0),
        NordicTextInput(
          key: const Key('note-input'),
          controller: _noteController,
          hintText: 'Có thú cưng, chìa khoá ở bảo vệ…',
          maxLines: 3,
        ),
        _fieldError('customerNote'),

        _heading('GIÁ CỐ ĐỊNH', top: 24.0),
        _quoteCard(options),
        const SizedBox(height: 16.0),

        if (_hint != null) _message(_hint!, key: const Key('booking-hint')),
        if (_failure?.message != null)
          _message(
            _failure!.message!,
            key: const Key('booking-error'),
            isError: true,
          ),
        const Text(
          'Thanh toán sau khi đặt. Không phụ phí.',
          style: NordicTypography.bodySmall,
        ),
      ],
    );
  }

  Widget _quoteCard(BookingOptions options) {
    final quote = _quote;
    if (_quoteLoading) {
      return const NordicCard(
        child: Text(
          'Đang tính giá…',
          key: Key('quote-loading'),
          style: NordicTypography.bodyRegular,
        ),
      );
    }
    if (_quoteError != null) {
      return NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              'Chưa lấy được giá: $_quoteError',
              key: const Key('quote-error'),
              style: NordicTypography.bodyRegular,
            ),
            const SizedBox(height: 8.0),
            NordicButton(
              label: 'Thử lại',
              variant: NordicButtonVariant.secondary,
              onPressed: _refreshQuote,
            ),
          ],
        ),
      );
    }
    if (quote == null) {
      return const NordicCard(
        child: Text(
          'Chọn gói và địa chỉ để xem giá.',
          key: Key('quote-empty'),
          style: NordicTypography.bodySmall,
        ),
      );
    }
    return NordicCard(
      isHighlighted: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            formatVnd(quote.totalAmount),
            key: const Key('quote-total'),
            style: NordicTypography.numberMedium.copyWith(
              color: NordicColors.primary,
            ),
          ),
          const SizedBox(height: 8.0),
          Text(
            '${formatVnd(quote.unitPrice)} × ${quote.requiredWorkers} thợ · ${tierLabel(quote.serviceTier)}',
            key: const Key('quote-breakdown'),
            style: NordicTypography.bodyRegular,
          ),
          const SizedBox(height: 2.0),
          Text(
            workersNote(quote, options),
            key: const Key('quote-workers-note'),
            style: NordicTypography.bodySmall,
          ),
        ],
      ),
    );
  }

  /// ui-templates `.caps` label above each group, with an optional note on the right.
  Widget _heading(String text, {double top = 0.0, String? trailing}) => Padding(
    padding: EdgeInsets.only(top: top, bottom: 8.0),
    child: Row(
      children: [
        Expanded(child: Text(text, style: NordicTypography.caps)),
        if (trailing != null) Text(trailing, style: NordicTypography.bodySmall),
      ],
    ),
  );

  Widget _fieldError(String field) {
    final message = _failure?.fields[field];
    if (message == null) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(top: 4.0),
      child: Text(
        message,
        key: Key('field-error-$field'),
        style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
      ),
    );
  }

  Widget _message(String text, {required Key key, bool isError = false}) =>
      Padding(
        padding: const EdgeInsets.only(bottom: 12.0),
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.all(12.0),
          decoration: BoxDecoration(
            color: isError
                ? NordicColors.errorContainer
                : NordicColors.amberSoft,
            borderRadius: BorderRadius.zero,
          ),
          child: Text(
            text,
            key: key,
            style: NordicTypography.bodyRegular.copyWith(
              color: isError ? NordicColors.error : NordicColors.onAmberSoft,
            ),
          ),
        ),
      );
}

/// Outline of a selectable tile: primary border when chosen (ui-templates `.tier.on`, `.shift.on`).
BoxDecoration _tileDecoration(bool selected) => BoxDecoration(
  color: NordicColors.surface,
  borderRadius: BorderRadius.zero,
  border: Border.all(
    color: selected ? NordicColors.primary : NordicColors.border,
    width: selected ? 2.0 : 1.0,
  ),
);

/// ui-templates `.tier`: one segment, its radio and its one-line explanation.
class _TierTile extends StatelessWidget {
  final bool selected;
  final String title;
  final String subtitle;
  final VoidCallback onTap;

  const _TierTile({
    super.key,
    required this.selected,
    required this.title,
    required this.subtitle,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Semantics(
      selected: selected,
      button: true,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.zero,
        child: Container(
          padding: const EdgeInsets.all(14.0),
          decoration: _tileDecoration(selected),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Expanded(
                    child: Text(title, style: NordicTypography.titleSmall),
                  ),
                  Icon(
                    selected
                        ? Icons.radio_button_checked
                        : Icons.radio_button_unchecked,
                    color: selected
                        ? NordicColors.primary
                        : NordicColors.borderVariant,
                  ),
                ],
              ),
              const SizedBox(height: 8.0),
              Text(
                subtitle,
                style: const TextStyle(
                  fontSize: 11.0,
                  height: 1.35,
                  color: NordicColors.textSecondary,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// ui-templates `.day`: weekday above the day number.
class _DayTile extends StatelessWidget {
  final DateTime day;
  final bool selected;
  final VoidCallback onTap;

  const _DayTile({
    super.key,
    required this.day,
    required this.selected,
    required this.onTap,
  });

  static const _weekdays = ['T2', 'T3', 'T4', 'T5', 'T6', 'T7', 'CN'];

  @override
  Widget build(BuildContext context) {
    final color = selected ? Colors.white : NordicColors.textTitle;
    return Semantics(
      selected: selected,
      button: true,
      label: dayLabel(day),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.zero,
        child: Container(
          width: 48.0,
          decoration: BoxDecoration(
            color: selected ? NordicColors.primary : NordicColors.surface,
            borderRadius: BorderRadius.zero,
            border: Border.all(
              color: selected ? NordicColors.primary : NordicColors.border,
            ),
          ),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(
                _weekdays[day.weekday - 1],
                style: TextStyle(
                  fontSize: 11.0,
                  color: selected ? Colors.white70 : NordicColors.textSecondary,
                ),
              ),
              const SizedBox(height: 2.0),
              Text(
                day.day.toString().padLeft(2, '0'),
                style: TextStyle(
                  fontSize: 17.0,
                  fontWeight: FontWeight.w700,
                  color: color,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// ui-templates `.shift`: an icon for the time of day, the start and the end.
class _ShiftTile extends StatelessWidget {
  final ShiftOption shift;
  final bool selected;
  final VoidCallback onTap;

  const _ShiftTile({
    super.key,
    required this.shift,
    required this.selected,
    required this.onTap,
  });

  IconData get _icon => switch (shift.shiftCode) {
    'SHIFT_MORNING' => Icons.wb_sunny_outlined,
    'SHIFT_AFTERNOON' => Icons.light_mode_outlined,
    'SHIFT_EVENING' => Icons.bedtime_outlined,
    _ => Icons.schedule_outlined,
  };

  @override
  Widget build(BuildContext context) {
    return Semantics(
      selected: selected,
      button: true,
      label: shiftText(shift),
      excludeSemantics: true,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.zero,
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 12.0, horizontal: 8.0),
          decoration: _tileDecoration(selected),
          child: Column(
            children: [
              Icon(
                _icon,
                color: selected
                    ? NordicColors.primary
                    : NordicColors.textSecondary,
              ),
              const SizedBox(height: 4.0),
              Text(shift.startLocal, style: NordicTypography.titleSmall),
              Text(
                '→ ${shift.endLocal}',
                style: const TextStyle(
                  fontSize: 11.0,
                  color: NordicColors.textSecondary,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

/// A selectable card: one of the customer's addresses.
class _ChoiceCard extends StatelessWidget {
  final bool selected;
  final String title;
  final String? subtitle;
  final VoidCallback onTap;

  const _ChoiceCard({
    super.key,
    required this.selected,
    required this.title,
    this.subtitle,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return Semantics(
      selected: selected,
      button: true,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.zero,
        child: Container(
          padding: const EdgeInsets.all(12.0),
          decoration: _tileDecoration(selected),
          child: Row(
            children: [
              Container(
                width: 40.0,
                height: 40.0,
                decoration: BoxDecoration(
                  color: NordicColors.surfaceLow,
                  borderRadius: BorderRadius.zero,
                ),
                child: const Icon(
                  Icons.home_outlined,
                  color: NordicColors.primary,
                ),
              ),
              const SizedBox(width: 12.0),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(title, style: NordicTypography.titleSmall),
                    if (subtitle != null && subtitle!.isNotEmpty) ...[
                      const SizedBox(height: 2.0),
                      Text(
                        subtitle!,
                        style: NordicTypography.bodySmall,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ],
                  ],
                ),
              ),
              Icon(
                selected
                    ? Icons.radio_button_checked
                    : Icons.radio_button_unchecked,
                color: selected
                    ? NordicColors.primary
                    : NordicColors.borderVariant,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
