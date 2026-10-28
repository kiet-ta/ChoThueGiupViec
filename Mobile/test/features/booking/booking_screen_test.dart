import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/core/theme/nordic_theme.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/screens/booking_screen.dart';
import 'package:mobile/features/booking/services/booking_service.dart';
import 'package:mobile/features/customers/models/favorite_worker.dart';

const _options = BookingOptions(
  serviceTiers: ['ECONOMY', 'PREMIUM'],
  shifts: [
    ShiftOption(
      shiftCode: 'SHIFT_MORNING',
      startLocal: '08:00',
      endLocal: '12:00',
    ),
    ShiftOption(
      shiftCode: 'SHIFT_EVENING',
      startLocal: '17:30',
      endLocal: '20:30',
    ),
  ],
  premiumMinLeadHours: 4,
  shiftMaxHours: 4,
  standardMaxAreaM2: 80,
  paymentQrExpiryMinutes: 15,
  sandbox: true,
);

BookingOrder _order(CreateOrderInput input) => BookingOrder(
  orderId: 100,
  orderCode: 'GV261014ABC123',
  serviceTier: input.serviceTier,
  addressId: input.addressId,
  scheduledDate: input.scheduledDate,
  shiftCode: input.shiftCode,
  shiftStartAt: null,
  shiftEndAt: null,
  areaSnapshotM2: 50,
  requiredWorkers: 1,
  totalAmount: 260000,
  orderStatus: OrderStatus.pendingPayment,
  customerNote: input.customerNote,
  cancelReason: null,
  paymentDeadlineAt: null,
  createdAt: null,
);

class FakeBookingService implements IBookingService {
  Object? optionsError;
  Object? quoteError;
  Object? createError;
  Completer<BookingOrder>? createGate;
  final quotes = <({int addressId, String tier})>[];
  final created = <CreateOrderInput>[];

  @override
  Future<BookingOptions> getOptions() => optionsError != null
      ? Future.error(optionsError!)
      : Future.value(_options);

  @override
  Future<PriceQuote> getQuote({
    required int addressId,
    required String serviceTier,
  }) {
    quotes.add((addressId: addressId, tier: serviceTier));
    if (quoteError != null) return Future.error(quoteError!);
    final large = addressId == 2;
    final unit = serviceTier == ServiceTier.premium ? 390000 : 260000;
    return Future.value(
      PriceQuote(
        addressId: addressId,
        serviceTier: serviceTier,
        totalAreaM2: large ? 90 : 50,
        areaBracket: large ? 'OVER_80' : 'FROM_31_TO_80',
        requiredWorkers: large ? 2 : 1,
        unitPrice: unit,
        totalAmount: unit * (large ? 2 : 1),
        currency: 'VND',
      ),
    );
  }

  @override
  Future<BookingOrder> createOrder(CreateOrderInput input) {
    created.add(input);
    if (createGate != null) return createGate!.future;
    if (createError != null) return Future.error(createError!);
    return Future.value(_order(input));
  }
}

class FakeAddresses implements IBookingAddressSource {
  List<BookingAddress> list;
  Object? error;
  int calls = 0;

  FakeAddresses([List<BookingAddress>? list])
    : list =
          list ??
          const [
            BookingAddress(
              addressId: 1,
              label: 'Nhà riêng',
              addressLine: '1 Lê Lợi',
              totalAreaM2: 50,
              isDefault: true,
            ),
            BookingAddress(
              addressId: 2,
              label: 'Nhà bố mẹ',
              addressLine: '9 Hai Bà Trưng',
              totalAreaM2: 90,
            ),
          ];

  @override
  Future<List<BookingAddress>> getAddresses() {
    calls++;
    return error != null ? Future.error(error!) : Future.value(list);
  }
}

/// The favorites and their free shifts; empty by default, so the section stays hidden and nothing goes to the network.
class FakeFavorites implements IBookingFavoriteSource {
  List<FavoriteWorker> list;
  Set<String> free;
  final freeAsked = <int>[];

  FakeFavorites([this.list = const [], this.free = const {}]);

  @override
  Future<List<FavoriteWorker>> getFavorites() => Future.value(list);

  @override
  Future<Set<String>> getFreeShifts(int workerId) {
    freeAsked.add(workerId);
    return Future.value(free);
  }
}

const _mai = FavoriteWorker(
  workerId: 42,
  fullName: 'Nguyễn Thị Mai',
  ratingAvg: 4.9,
  completedJobs: 142,
  workStatus: 'IDLE',
);

Future<List<BookingOrder>> pumpBooking(
  WidgetTester tester,
  FakeBookingService service, {
  FakeAddresses? addresses,
  VoidCallback? onManageAddresses,
  BookingScreenArgs? args,
  FakeFavorites? favorites,
}) async {
  tester.view.physicalSize = const Size(390, 3200);
  tester.view.devicePixelRatio = 1.0;
  addTearDown(() {
    tester.view.resetPhysicalSize();
    tester.view.resetDevicePixelRatio();
  });
  final opened = <BookingOrder>[];
  await tester.pumpWidget(
    MaterialApp(
      theme: NordicTheme.lightTheme,
      home: BookingScreen(
        service: service,
        addresses: addresses ?? FakeAddresses(),
        now: () => DateTime.utc(2026, 10, 14, 3),
        onOrderCreated: opened.add,
        onManageAddresses: onManageAddresses,
        args: args,
        favorites: favorites ?? FakeFavorites(),
      ),
    ),
  );
  await tester.pumpAndSettle();
  return opened;
}

Future<void> tapKey(WidgetTester tester, String key) async {
  await tester.ensureVisible(find.byKey(Key(key)));
  await tester
      .pumpAndSettle(); // the scroll must be laid out before the tap is aimed
  await tester.tap(find.byKey(Key(key)));
  await tester.pumpAndSettle();
}

String textOf(WidgetTester tester, String key) =>
    tester.widget<Text>(find.byKey(Key(key))).data!;

void main() {
  group('BookingScreen (MOB-M2-01, MOB-M2-02)', () {
    testWidgets(
      'offers both segments, explained, with the lead time from the options',
      (tester) async {
        await pumpBooking(tester, FakeBookingService());

        expect(find.byKey(const Key('tier-ECONOMY')), findsOneWidget);
        expect(find.byKey(const Key('tier-PREMIUM')), findsOneWidget);
        expect(find.textContaining('Người làm tự do'), findsOneWidget);
        expect(find.textContaining('ít nhất 4 giờ'), findsOneWidget);
      },
    );

    testWidgets(
      'lists the addresses with their area, the shifts of the options, and 15 selectable days',
      (tester) async {
        await pumpBooking(tester, FakeBookingService());

        expect(find.textContaining('1 Lê Lợi · 50 m²'), findsOneWidget);
        expect(find.textContaining('9 Hai Bà Trưng · 90 m²'), findsOneWidget);
        expect(find.bySemanticsLabel('Ca sáng · 08:00–12:00'), findsOneWidget);
        expect(find.bySemanticsLabel('Ca tối · 17:30–20:30'), findsOneWidget);
        expect(find.text('08:00'), findsOneWidget);
        expect(find.text('→ 20:30'), findsOneWidget);
        expect(find.byKey(const Key('day-2026-10-14')), findsOneWidget);
      },
    );

    testWidgets('Economy offers the favorite workers; Premium does not', (
      tester,
    ) async {
      await pumpBooking(
        tester,
        FakeBookingService(),
        favorites: FakeFavorites([_mai]),
      );
      expect(find.byKey(const Key('favorite-42')), findsNothing);

      await tapKey(tester, 'tier-ECONOMY');
      expect(find.byKey(const Key('favorite-none')), findsOneWidget);
      expect(find.byKey(const Key('favorite-42')), findsOneWidget);

      await tapKey(tester, 'tier-PREMIUM');
      expect(find.byKey(const Key('favorite-42')), findsNothing);
    });

    testWidgets(
      'picking a favorite greys out the days and shifts they are busy, and sends them',
      (tester) async {
        final service = FakeBookingService();
        final favorites = FakeFavorites([_mai], {'2026-10-15|SHIFT_MORNING'});
        await pumpBooking(tester, service, favorites: favorites);

        await tapKey(tester, 'tier-ECONOMY');
        await tapKey(tester, 'favorite-42');
        expect(favorites.freeAsked, [42]);
        expect(find.byKey(const Key('favorite-note')), findsOneWidget);

        // Pressable has its own Opacity (1.0 at rest): a busy tile is the faded one.
        Finder faded(String key) => find.descendant(
          of: find.byKey(Key(key)),
          matching: find.byWidgetPredicate(
            (w) => w is Opacity && w.opacity < 0.5,
          ),
        );
        expect(faded('day-2026-10-14'), findsOneWidget);
        expect(faded('day-2026-10-15'), findsNothing);

        // A busy day cannot be chosen.
        await tapKey(tester, 'day-2026-10-14');
        await tapKey(tester, 'submit-order');
        expect(textOf(tester, 'booking-hint'), contains('ngày'));

        await tapKey(tester, 'day-2026-10-15');
        expect(faded('shift-SHIFT_MORNING'), findsNothing);
        expect(faded('shift-SHIFT_EVENING'), findsOneWidget);
        await tapKey(tester, 'shift-SHIFT_MORNING');
        await tapKey(tester, 'submit-order');

        expect(service.created.single.preferredWorkerId, 42);
        expect(service.created.single.toJson()['preferredWorkerId'], 42);
      },
    );

    testWidgets(
      'opened from the favorites list, the worker and the Economy segment are ticked',
      (tester) async {
        final service = FakeBookingService();
        final favorites = FakeFavorites([_mai], {'2026-10-15|SHIFT_MORNING'});
        await pumpBooking(
          tester,
          service,
          favorites: favorites,
          args: const BookingScreenArgs(preferredWorkerId: 42),
        );

        expect(service.quotes.single.tier, 'ECONOMY');
        expect(favorites.freeAsked, [42]);
        expect(
          find.descendant(
            of: find.byKey(const Key('favorite-42')),
            matching: find.byIcon(Icons.radio_button_checked),
          ),
          findsOneWidget,
        );
        expect(find.byKey(const Key('favorite-note')), findsOneWidget);
      },
    );

    testWidgets(
      'a preselected worker who is no longer a favorite is dropped',
      (tester) async {
        final favorites = FakeFavorites([_mai]);
        await pumpBooking(
          tester,
          FakeBookingService(),
          favorites: favorites,
          args: const BookingScreenArgs(preferredWorkerId: 7),
        );

        expect(favorites.freeAsked, isEmpty);
        expect(find.byKey(const Key('favorite-note')), findsNothing);
      },
    );

    testWidgets(
      'opened from a quick-booking tile, the segment and address are ticked',
      (tester) async {
        final service = FakeBookingService();
        await pumpBooking(
          tester,
          service,
          args: const BookingScreenArgs(
            serviceTier: ServiceTier.premium,
            addressId: 2,
          ),
        );

        expect(service.quotes.single, (addressId: 2, tier: 'PREMIUM'));
        expect(find.byKey(const Key('quote-total')), findsOneWidget);
      },
    );

    testWidgets(
      'a preselected segment or address that does not exist is dropped',
      (tester) async {
        final service = FakeBookingService();
        await pumpBooking(
          tester,
          service,
          args: const BookingScreenArgs(serviceTier: 'GOLD', addressId: 99),
        );

        expect(service.quotes, isEmpty);
        await tapKey(tester, 'tier-ECONOMY');
        expect(service.quotes.single, (addressId: 1, tier: 'ECONOMY'));
      },
    );

    testWidgets(
      'shows no price until a segment is chosen, then the fixed price for the default address',
      (tester) async {
        final service = FakeBookingService();
        await pumpBooking(tester, service);

        expect(find.byKey(const Key('quote-empty')), findsOneWidget);
        expect(service.quotes, isEmpty);

        await tapKey(tester, 'tier-ECONOMY');

        expect(service.quotes.single, (addressId: 1, tier: 'ECONOMY'));
        expect(textOf(tester, 'quote-total'), '260.000 đ');
        expect(
          textOf(tester, 'quote-breakdown'),
          '260.000 đ × 1 người · Cơ bản',
        );
        expect(
          textOf(tester, 'quote-workers-note'),
          '1 người, một ca tối đa 4 giờ.',
        );
      },
    );

    testWidgets(
      'asks the price again when the segment or the address changes, and says when two workers are needed',
      (tester) async {
        final service = FakeBookingService();
        await pumpBooking(tester, service);

        await tapKey(tester, 'tier-PREMIUM');
        await tapKey(tester, 'address-2');

        expect(service.quotes, [
          (addressId: 1, tier: 'PREMIUM'),
          (addressId: 2, tier: 'PREMIUM'),
        ]);
        expect(textOf(tester, 'quote-total'), '780.000 đ');
        expect(
          textOf(tester, 'quote-breakdown'),
          '390.000 đ × 2 người · Cao cấp',
        );
        expect(textOf(tester, 'quote-workers-note'), contains('cần 2 người'));
      },
    );

    testWidgets(
      'neither tier asks for a required skill (skill matching was removed, decision D8)',
      (tester) async {
        await pumpBooking(tester, FakeBookingService());

        expect(find.byKey(const Key('skill-input')), findsNothing);
        expect(find.textContaining('Kỹ năng'), findsNothing);
        await tapKey(tester, 'tier-PREMIUM');
        expect(find.byKey(const Key('skill-input')), findsNothing);
        expect(find.textContaining('Kỹ năng'), findsNothing);
        await tapKey(tester, 'tier-ECONOMY');
        expect(find.byKey(const Key('skill-input')), findsNothing);
      },
    );

    testWidgets(
      'says what is missing and creates nothing until everything is chosen',
      (tester) async {
        final service = FakeBookingService();
        await pumpBooking(tester, service);

        await tapKey(tester, 'submit-order');
        expect(textOf(tester, 'booking-hint'), contains('gói'));

        await tapKey(tester, 'tier-ECONOMY');
        await tapKey(tester, 'submit-order');
        expect(textOf(tester, 'booking-hint'), contains('ngày'));

        await tapKey(tester, 'day-2026-10-15');
        await tapKey(tester, 'submit-order');
        expect(textOf(tester, 'booking-hint'), contains('ca'));

        expect(service.created, isEmpty);
      },
    );

    testWidgets(
      'creates the order with the choices and opens the payment for it',
      (tester) async {
        final service = FakeBookingService();
        final opened = await pumpBooking(tester, service);

        await tapKey(tester, 'tier-PREMIUM');
        await tapKey(tester, 'address-2');
        // The days are a horizontal list that builds only what is on screen: scroll to a later day like a customer would.
        await tester.scrollUntilVisible(
          find.byKey(const Key('day-2026-10-16')),
          80.0,
          scrollable: find.descendant(
            of: find.byKey(const Key('day-list')),
            matching: find.byType(Scrollable),
          ),
        );
        await tapKey(tester, 'day-2026-10-16');
        await tapKey(tester, 'shift-SHIFT_EVENING');
        await tester.enterText(
          find.byKey(const Key('note-input')),
          '  có thú cưng  ',
        );
        await tapKey(tester, 'submit-order');

        final input = service.created.single;
        expect(input.addressId, 2);
        expect(input.serviceTier, 'PREMIUM');
        expect(input.scheduledDate, '2026-10-16');
        expect(input.shiftCode, 'SHIFT_EVENING');
        expect(input.customerNote, 'có thú cưng');
        expect(input.toJson().keys, [
          'addressId',
          'serviceTier',
          'scheduledDate',
          'shiftCode',
          'customerNote',
        ], reason: 'no requiredSkill in the request');
        expect(opened.single.orderId, 100);
      },
    );

    testWidgets(
      'an Economy order never sends a required skill, and an empty note is sent as null',
      (tester) async {
        final service = FakeBookingService();
        await pumpBooking(tester, service);

        await tapKey(tester, 'tier-ECONOMY');
        await tapKey(tester, 'day-2026-10-15');
        await tapKey(tester, 'shift-SHIFT_MORNING');
        await tapKey(tester, 'submit-order');

        expect(
          service.created.single.toJson().containsKey('requiredSkill'),
          isFalse,
        );
        expect(service.created.single.customerNote, isNull);
      },
    );

    testWidgets(
      'a second tap while the order is being created sends nothing more',
      (tester) async {
        final service = FakeBookingService()
          ..createGate = Completer<BookingOrder>();
        final opened = await pumpBooking(tester, service);
        await tapKey(tester, 'tier-ECONOMY');
        await tapKey(tester, 'day-2026-10-15');
        await tapKey(tester, 'shift-SHIFT_MORNING');

        await tester.tap(find.byKey(const Key('submit-order')));
        await tester.pump();
        await tester.tap(
          find.byKey(const Key('submit-order')),
          warnIfMissed: false,
        );
        await tester.pump();

        expect(service.created.length, 1);
        service.createGate!.complete(_order(service.created.single));
        await tester.pumpAndSettle();
        expect(opened.length, 1);
      },
    );

    Future<FakeBookingService> submitWith(
      WidgetTester tester,
      Object error,
    ) async {
      final service = FakeBookingService()..createError = error;
      await pumpBooking(tester, service);
      await tapKey(tester, 'tier-PREMIUM');
      await tapKey(tester, 'day-2026-10-15');
      await tapKey(tester, 'shift-SHIFT_MORNING');
      await tapKey(tester, 'submit-order');
      return service;
    }

    testWidgets(
      'FULLY_BOOKED is explained from its code, not from the message',
      (tester) async {
        await submitWith(
          tester,
          const ApiException(
            409,
            'No partner agency has capacity for this shift.',
            code: 'FULLY_BOOKED',
          ),
        );

        expect(textOf(tester, 'booking-error'), contains('kín lịch'));
        expect(find.textContaining('No partner agency'), findsNothing);
      },
    );

    testWidgets('PREMIUM_LEAD_TIME and SHIFT_IN_PAST are explained', (
      tester,
    ) async {
      await submitWith(
        tester,
        const ApiException(409, 'x', code: 'PREMIUM_LEAD_TIME'),
      );
      expect(textOf(tester, 'booking-error'), contains('ít nhất 4 giờ'));
    });

    testWidgets('a 400 puts each message under its field', (tester) async {
      await submitWith(
        tester,
        const ApiException(
          400,
          'Validation failed',
          fieldErrors: {
            'scheduledDate': ['scheduledDate must be at most 14 days ahead.'],
          },
        ),
      );

      expect(textOf(tester, 'field-error-scheduledDate'), contains('14 days'));
      expect(find.byKey(const Key('booking-error')), findsNothing);
    });

    testWidgets(
      'a note longer than 500 characters is refused before any request',
      (tester) async {
        final service = FakeBookingService();
        await pumpBooking(tester, service);
        await tapKey(tester, 'tier-ECONOMY');
        await tapKey(tester, 'day-2026-10-15');
        await tapKey(tester, 'shift-SHIFT_MORNING');
        await tester.enterText(find.byKey(const Key('note-input')), 'a' * 501);
        await tapKey(tester, 'submit-order');

        expect(textOf(tester, 'field-error-customerNote'), contains('500'));
        expect(service.created, isEmpty);
      },
    );

    testWidgets('a failed quote is shown with a retry and blocks the order', (
      tester,
    ) async {
      final service = FakeBookingService()
        ..quoteError = const ApiException(500, 'boom');
      await pumpBooking(tester, service);
      await tapKey(tester, 'tier-ECONOMY');

      expect(textOf(tester, 'quote-error'), contains('Máy chủ gặp lỗi'));

      await tapKey(tester, 'day-2026-10-15');
      await tapKey(tester, 'shift-SHIFT_MORNING');
      await tapKey(tester, 'submit-order');
      expect(textOf(tester, 'booking-hint'), contains('giá'));
      expect(service.created, isEmpty);

      service.quoteError = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();
      expect(textOf(tester, 'quote-total'), '260.000 đ');
    });

    testWidgets(
      'without any address it sends the customer to the address book',
      (tester) async {
        var opened = 0;
        await pumpBooking(
          tester,
          FakeBookingService(),
          addresses: FakeAddresses(const []),
          onManageAddresses: () => opened++,
        );

        expect(find.text('Bạn chưa có địa chỉ nào'), findsOneWidget);
        await tester.tap(find.text('Thêm địa chỉ'));
        await tester.pumpAndSettle();
        expect(opened, 1);
      },
    );

    testWidgets('a failed load shows the reason and loads again on retry', (
      tester,
    ) async {
      final service = FakeBookingService()
        ..optionsError = const ApiException(0, 'Không kết nối được máy chủ.');
      final addresses = FakeAddresses();
      await pumpBooking(tester, service, addresses: addresses);

      expect(find.text('Không kết nối được máy chủ.'), findsOneWidget);
      expect(find.byKey(const Key('tier-ECONOMY')), findsNothing);

      service.optionsError = null;
      await tester.tap(find.text('Thử lại'));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('tier-ECONOMY')), findsOneWidget);
      expect(addresses.calls, 2);
    });
  });
}
