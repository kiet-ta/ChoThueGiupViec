import 'package:flutter/material.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
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

  const BookingScreen({super.key, this.service, this.addresses, this.now, this.onOrderCreated, this.onManageAddresses});

  @override
  State<BookingScreen> createState() => _BookingScreenState();
}

class _BookingScreenState extends State<BookingScreen> {
  late final IBookingService _service;
  late final IBookingAddressSource _addressSource;
  late final List<DateTime> _days;

  final _noteController = TextEditingController();
  final _skillController = TextEditingController();

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
    _skillController.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _loadError = null;
    });
    try {
      final results = await Future.wait([_service.getOptions(), _addressSource.getAddresses()]);
      if (!mounted) return;
      final addresses = results[1] as List<BookingAddress>;
      setState(() {
        _options = results[0] as BookingOptions;
        _addresses = addresses;
        // The default address is preselected; the customer can change it.
        _addressId ??= addresses.where((a) => a.isDefault).map((a) => a.addressId).firstOrNull;
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
      final quote = await _service.getQuote(addressId: addressId, serviceTier: tier);
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
      if (tier != ServiceTier.premium) _skillController.clear(); // a required skill is a Premium-only field
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
    final skillError = validateSkill(_skillController.text);
    final missing = missingChoice(tier: _tier, addressId: _addressId, day: _day, shiftCode: _shiftCode, hasQuote: _quote != null);
    if (missing != null || noteError != null || skillError != null) {
      setState(() {
        _hint = missing;
        _failure = CreateOrderFailure(fields: {'customerNote': ?noteError, 'requiredSkill': ?skillError});
      });
      return;
    }

    final note = _noteController.text.trim();
    final skill = _skillController.text.trim();
    final input = CreateOrderInput(
      addressId: _addressId!,
      serviceTier: _tier!,
      scheduledDate: dateKey(_day!),
      shiftCode: _shiftCode!,
      customerNote: note.isEmpty ? null : note,
      requiredSkill: _tier == ServiceTier.premium && skill.isNotEmpty ? skill : null,
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
        _failure = createOrderFailure(e, premiumMinLeadHours: _options?.premiumMinLeadHours ?? 4);
      });
    }
  }

  void _opened(BookingOrder order) {
    if (widget.onOrderCreated != null) return widget.onOrderCreated!(order);
    Navigator.of(context)
        .pushNamed(PaymentRoutes.payment, arguments: PaymentScreenArgs(orderId: order.orderId, orderCode: order.orderCode))
        .then((_) {
      // Paid or not, the order exists: the customer follows it (and can pay or cancel it) on its own screen.
      if (mounted) Navigator.of(context).pushReplacementNamed(BookingRoutes.bookingDetail, arguments: OrderDetailArgs(orderId: order.orderId));
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
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: const Text('Đặt ca làm việc')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _content(),
        ),
      ),
    );
  }

  Widget _content() {
    if (_loading && _options == null) return const LoadingStateWidget(message: 'Đang tải lựa chọn đặt đơn…');
    if (_loadError != null) return ErrorStateWidget(title: 'Chưa tải được', message: _loadError!, onRetry: _load);
    final options = _options!;
    final addresses = _addresses ?? const <BookingAddress>[];

    return ListView(
      padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 16.0),
      children: [
        const EyebrowBadge(text: 'Dọn dẹp theo ca'),
        const SizedBox(height: 12.0),
        const Text('Đặt ca giúp việc', style: NordicTypography.h2),
        const SizedBox(height: 20.0),

        _heading('1. Phân khúc dịch vụ'),
        for (final tier in options.serviceTiers) ...[
          _ChoiceCard(
            key: Key('tier-$tier'),
            selected: _tier == tier,
            title: tierLabel(tier),
            subtitle: tierDescription(tier, options),
            onTap: () => _chooseTier(tier),
          ),
          const SizedBox(height: 8.0),
        ],
        _fieldError('serviceTier'),
        const SizedBox(height: 12.0),

        _heading('2. Địa chỉ làm việc'),
        if (addresses.isEmpty)
          EmptyStateWidget(
            title: 'Bạn chưa có địa chỉ nào',
            subtitle: 'Thêm địa chỉ để hệ thống tính diện tích và báo giá.',
            icon: Icons.home_outlined,
            action: NordicButton(label: 'Thêm địa chỉ', onPressed: _manageAddresses),
          )
        else ...[
          for (final address in addresses) ...[
            _ChoiceCard(
              key: Key('address-${address.addressId}'),
              selected: _addressId == address.addressId,
              title: address.label,
              subtitle: '${address.addressLine} · ${formatArea(address.totalAreaM2)}',
              onTap: () => _chooseAddress(address.addressId),
            ),
            const SizedBox(height: 8.0),
          ],
          Align(
            alignment: Alignment.centerLeft,
            child: NordicButton(label: 'Sổ địa chỉ', variant: NordicButtonVariant.ghost, onPressed: _manageAddresses),
          ),
        ],
        _fieldError('addressId'),
        const SizedBox(height: 12.0),

        _heading('3. Ngày và ca'),
        SizedBox(
          key: const Key('day-list'),
          height: 44.0,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            itemCount: _days.length,
            separatorBuilder: (_, _) => const SizedBox(width: 8.0),
            itemBuilder: (_, i) {
              final day = _days[i];
              return ChoiceChip(
                key: Key('day-${dateKey(day)}'),
                label: Text(dayLabel(day)),
                selected: _day == day,
                onSelected: (_) => setState(() {
                  _day = day;
                  _failure = null;
                  _hint = null;
                }),
              );
            },
          ),
        ),
        _fieldError('scheduledDate'),
        const SizedBox(height: 8.0),
        for (final shift in options.shifts) ...[
          _ChoiceCard(
            key: Key('shift-${shift.shiftCode}'),
            selected: _shiftCode == shift.shiftCode,
            title: shiftText(shift),
            onTap: () => setState(() {
              _shiftCode = shift.shiftCode;
              _failure = null;
              _hint = null;
            }),
          ),
          const SizedBox(height: 8.0),
        ],
        _fieldError('shiftCode'),
        const SizedBox(height: 12.0),

        _heading('4. Ghi chú cho thợ'),
        NordicTextInput(
          key: const Key('note-input'),
          controller: _noteController,
          hintText: 'Ví dụ: nhà có thú cưng, cần lau kính ban công',
          maxLines: 3,
        ),
        _fieldError('customerNote'),
        if (_tier == ServiceTier.premium) ...[
          const SizedBox(height: 8.0),
          NordicTextInput(
            key: const Key('skill-input'),
            controller: _skillController,
            label: 'Kỹ năng yêu cầu (không bắt buộc)',
            hintText: 'Ví dụ: vệ sinh sau xây dựng',
          ),
          _fieldError('requiredSkill'),
        ],
        const SizedBox(height: 16.0),

        _heading('5. Giá cố định'),
        _quoteCard(options),
        const SizedBox(height: 16.0),

        if (_hint != null) _message(_hint!, key: const Key('booking-hint')),
        if (_failure?.message != null) _message(_failure!.message!, key: const Key('booking-error'), isError: true),
        NordicButton(
          key: const Key('submit-order'),
          label: 'Đặt đơn',
          isLoading: _submitting,
          onPressed: _submitting ? null : _submit,
        ),
        const SizedBox(height: 8.0),
        const Text('Bạn thanh toán sau khi đặt đơn. Giá trên là giá cuối cùng, không có phụ phí.', style: NordicTypography.bodySmall),
        const SizedBox(height: 24.0),
      ],
    );
  }

  Widget _quoteCard(BookingOptions options) {
    final quote = _quote;
    if (_quoteLoading) {
      return const NordicCard(child: Text('Đang tính giá…', key: Key('quote-loading'), style: NordicTypography.bodyRegular));
    }
    if (_quoteError != null) {
      return NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Chưa lấy được giá: $_quoteError', key: const Key('quote-error'), style: NordicTypography.bodyRegular),
            const SizedBox(height: 8.0),
            NordicButton(label: 'Thử lại', variant: NordicButtonVariant.secondary, onPressed: _refreshQuote),
          ],
        ),
      );
    }
    if (quote == null) {
      return const NordicCard(
        child: Text('Chọn phân khúc và địa chỉ để xem giá.', key: Key('quote-empty'), style: NordicTypography.bodyRegular),
      );
    }
    return NordicCard(
      isHighlighted: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(formatVnd(quote.totalAmount), key: const Key('quote-total'), style: NordicTypography.priceHighlight),
          const SizedBox(height: 4.0),
          Text(
            '${formatVnd(quote.unitPrice)} × ${quote.requiredWorkers} thợ · ${tierLabel(quote.serviceTier)}',
            key: const Key('quote-breakdown'),
            style: NordicTypography.bodyRegular,
          ),
          const SizedBox(height: 4.0),
          Text(workersNote(quote, options), key: const Key('quote-workers-note'), style: NordicTypography.bodySmall),
        ],
      ),
    );
  }

  Widget _heading(String text) => Padding(
        padding: const EdgeInsets.only(bottom: 8.0),
        child: Text(text, style: NordicTypography.h3),
      );

  Widget _fieldError(String field) {
    final message = _failure?.fields[field];
    if (message == null) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(top: 4.0),
      child: Text(message, key: Key('field-error-$field'), style: NordicTypography.bodySmall.copyWith(color: NordicColors.error)),
    );
  }

  Widget _message(String text, {required Key key, bool isError = false}) => Padding(
        padding: const EdgeInsets.only(bottom: 12.0),
        child: Container(
          width: double.infinity,
          padding: const EdgeInsets.all(12.0),
          decoration: BoxDecoration(
            color: isError ? NordicColors.errorContainer : NordicColors.surfaceSubtle,
            borderRadius: BorderRadius.circular(12.0),
          ),
          child: Text(
            text,
            key: key,
            style: NordicTypography.bodyRegular.copyWith(color: isError ? NordicColors.error : NordicColors.textTitle),
          ),
        ),
      );
}

/// A selectable card: one of several choices (segment, address, shift).
class _ChoiceCard extends StatelessWidget {
  final bool selected;
  final String title;
  final String? subtitle;
  final VoidCallback onTap;

  const _ChoiceCard({super.key, required this.selected, required this.title, this.subtitle, required this.onTap});

  @override
  Widget build(BuildContext context) {
    return Semantics(
      selected: selected,
      button: true,
      child: NordicCard(
        isHighlighted: selected,
        onTap: onTap,
        child: Row(
          children: [
            Icon(
              selected ? Icons.radio_button_checked : Icons.radio_button_unchecked,
              color: selected ? NordicColors.primary : NordicColors.textSecondary,
            ),
            const SizedBox(width: 12.0),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(title, style: NordicTypography.labelMedium),
                  if (subtitle != null && subtitle!.isNotEmpty) ...[
                    const SizedBox(height: 2.0),
                    Text(subtitle!, style: NordicTypography.bodySmall),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
