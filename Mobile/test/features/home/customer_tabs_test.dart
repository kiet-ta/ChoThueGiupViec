import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/network/api_client.dart';
import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/booking/services/booking_service.dart';
import 'package:mobile/features/booking/services/order_service.dart';
import 'package:mobile/features/customers/services/customer_address_service.dart';
import 'package:mobile/features/customers/services/customer_profile_service.dart';
import 'package:mobile/features/customers/services/favorite_worker_service.dart';
import 'package:mobile/features/home/customer_account_tab.dart';
import 'package:mobile/features/home/customer_home_tab.dart';
import 'package:mobile/features/home/customer_shell.dart';

import '../workers/design_frame.dart';

const _options = BookingOptions(
  serviceTiers: [ServiceTier.economy, ServiceTier.premium],
  shifts: [
    ShiftOption(
      shiftCode: 'SHIFT_MORNING',
      startLocal: '08:00',
      endLocal: '12:00',
    ),
  ],
  premiumMinLeadHours: 4,
  shiftMaxHours: 4,
  standardMaxAreaM2: 80,
  paymentQrExpiryMinutes: 15,
  sandbox: true,
);

OrderSummary _summary(int id, String status) => OrderSummary(
  orderId: id,
  orderCode: 'GV$id',
  serviceTier: ServiceTier.economy,
  scheduledDate: '2026-10-26',
  shiftCode: 'SHIFT_MORNING',
  requiredWorkers: 1,
  totalAmount: 260000,
  orderStatus: status,
  createdAt: null,
);

class _Orders implements IOrderService {
  List<OrderSummary> items;
  int total;
  Object? error;

  _Orders(this.items, {int? total}) : total = total ?? items.length;

  @override
  Future<OrderPage> listOrders({int page = 1, int pageSize = 20}) =>
      error != null
      ? Future.error(error!)
      : Future.value(
          OrderPage(items: items, page: page, pageSize: pageSize, total: total),
        );

  @override
  Future<OrderProgress> getProgress(int orderId) => Future.value(
    OrderProgress(
      orderId: orderId,
      orderStatus: OrderStatus.assigned,
      requiredWorkers: 1,
      assignments: const [
        ProgressAssignment(
          assignmentId: 7,
          assignmentSeq: 1,
          workerId: 57,
          workerName: 'Trần Thị Hà',
          workerRatingAvg: 4.9,
          assignmentStatus: AssignmentStatus.assigned,
          acceptedAt: null,
          completedAt: null,
        ),
      ],
      extension: null,
    ),
  );

  @override
  Future<BookingOptions> getOptions() => Future.value(_options);

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _Booking implements IBookingService {
  @override
  Future<BookingOptions> getOptions() => Future.value(_options);

  @override
  Future<PriceQuote> getQuote({
    required int addressId,
    required String serviceTier,
  }) => Future.value(
    PriceQuote(
      addressId: addressId,
      serviceTier: serviceTier,
      totalAreaM2: 58,
      areaBracket: 'FROM_31_TO_80',
      requiredWorkers: 1,
      unitPrice: serviceTier == ServiceTier.premium ? 390000 : 260000,
      totalAmount: serviceTier == ServiceTier.premium ? 390000 : 260000,
      currency: 'VND',
    ),
  );

  @override
  dynamic noSuchMethod(Invocation invocation) => super.noSuchMethod(invocation);
}

class _Addresses implements IBookingAddressSource {
  final List<BookingAddress> list;

  _Addresses(this.list);

  @override
  Future<List<BookingAddress>> getAddresses() => Future.value(list);
}

const _home = BookingAddress(
  addressId: 1,
  label: 'Nhà',
  addressLine: '12 Lê Lợi',
  totalAreaM2: 58,
  isDefault: true,
);

Widget _app(Widget child, List<String> opened) => MaterialApp(
  home: child,
  onGenerateRoute: (settings) {
    opened.add(settings.name ?? '');
    return MaterialPageRoute(
      settings: settings,
      builder: (_) => Scaffold(body: Text('opened ${settings.name}')),
    );
  },
);

CustomerHomeTab _homeTab(_Orders orders, {List<BookingAddress>? addresses}) =>
    CustomerHomeTab(
      orders: orders,
      booking: _Booking(),
      addresses: _Addresses(addresses ?? const [_home]),
      favorites: FavoriteWorkerService(isMock: true),
      profile: CustomerProfileService(isMock: true),
      // Sunday 25/10/2026, 10:00 in Ho Chi Minh.
      clock: () => DateTime.utc(2026, 10, 25, 3),
    );

void main() {
  group('CustomerHomeTab (ui-templates customer/home.html)', () {
    testWidgets(
      'greets by the last name, shows the next order, the server prices and favourites',
      (tester) async {
        useDesignFrame(tester);
        await tester.pumpWidget(
          _app(
            _homeTab(
              _Orders([
                _summary(1, OrderStatus.completed),
                _summary(2, OrderStatus.assigned),
              ]),
            ),
            <String>[],
          ),
        );
        await tester.pumpAndSettle();

        expect(find.text('Chủ Nhật, 25/10'), findsOneWidget);
        expect(find.text('Chào An'), findsOneWidget);
        expect(find.byKey(const Key('home-next-order')), findsOneWidget);
        expect(find.text('08:00'), findsOneWidget);
        expect(find.text('Trần Thị Hà'), findsOneWidget);
        expect(find.text('260.000 đ'), findsOneWidget);
        expect(find.text('390.000 đ'), findsOneWidget);
        await scrollTo(tester, find.text('Thợ yêu thích'));
        expect(find.text('Thợ yêu thích'), findsOneWidget);
      },
    );

    testWidgets('a tap on the next order opens its detail', (tester) async {
      useDesignFrame(tester);
      final opened = <String>[];
      await tester.pumpWidget(
        _app(_homeTab(_Orders([_summary(2, OrderStatus.dispatching)])), opened),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('home-next-order')));
      await tester.pumpAndSettle();

      expect(opened, ['/booking/detail']);
    });

    testWidgets(
      'without an open order it offers to book, and without an address it shows no prices',
      (tester) async {
        useDesignFrame(tester);
        final opened = <String>[];
        await tester.pumpWidget(
          _app(
            _homeTab(
              _Orders([_summary(1, OrderStatus.cancelled)]),
              addresses: [],
            ),
            opened,
          ),
        );
        await tester.pumpAndSettle();

        expect(find.text('Đặt nhanh'), findsNothing);
        await tester.tap(find.byKey(const Key('home-no-order')));
        await tester.pumpAndSettle();

        expect(opened, ['/booking']);
      },
    );

    testWidgets('a failed order list hides only that section', (tester) async {
      useDesignFrame(tester);
      final orders = _Orders([])
        ..error = const ApiException(0, 'Không kết nối được máy chủ.');
      await tester.pumpWidget(_app(_homeTab(orders), <String>[]));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('home-no-order')), findsOneWidget);
      expect(find.text('Đặt nhanh'), findsOneWidget);
    });
  });

  group('CustomerAccountTab (ui-templates customer/account.html)', () {
    testWidgets('shows the customer and counts read from real answers', (
      tester,
    ) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        _app(
          CustomerAccountTab(
            onSignOut: () {},
            profile: CustomerProfileService(isMock: true),
            addresses: CustomerAddressService(isMock: true),
            orders: _Orders(const [], total: 15),
          ),
          <String>[],
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Nguyễn Văn An'), findsOneWidget);
      expect(find.text('0901 234 567'), findsOneWidget);
      expect(find.text('15'), findsOneWidget);
      expect(find.text('2026'), findsOneWidget);
    });

    testWidgets('the rows open their screens and the last one signs out', (
      tester,
    ) async {
      useDesignFrame(tester);
      final opened = <String>[];
      var signedOut = false;
      await tester.pumpWidget(
        _app(
          CustomerAccountTab(
            onSignOut: () => signedOut = true,
            profile: CustomerProfileService(isMock: true),
            addresses: CustomerAddressService(isMock: true),
            orders: _Orders(const []),
          ),
          opened,
        ),
      );
      await tester.pumpAndSettle();

      await tapVisible(tester, find.byKey(const Key('account-addresses')));
      expect(opened, ['/addresses']);
      tester.state<NavigatorState>(find.byType(Navigator)).pop();
      await tester.pumpAndSettle();

      await tapVisible(tester, find.byKey(const Key('account-disputes')));
      expect(opened.last, '/disputes');
      tester.state<NavigatorState>(find.byType(Navigator)).pop();
      await tester.pumpAndSettle();

      await tapVisible(tester, find.byKey(const Key('home-sign-out')));
      expect(signedOut, isTrue);
    });
  });

  group('CustomerShell', () {
    testWidgets('switches tabs from the bottom bar', (tester) async {
      useDesignFrame(tester);
      await tester.pumpWidget(
        _app(
          CustomerShell(
            onSignOut: () {},
            tabs: const [
              Text('tab home'),
              Text('tab orders'),
              Text('tab favourites'),
              Text('tab account'),
            ],
          ),
          <String>[],
        ),
      );

      for (final (index, label) in [
        (1, 'tab orders'),
        (2, 'tab favourites'),
        (3, 'tab account'),
        (0, 'tab home'),
      ]) {
        await tester.tap(find.byKey(Key('customer-tab-$index')));
        await tester.pump();
        expect(
          tester.widget<IndexedStack>(find.byType(IndexedStack)).index,
          index,
          reason: label,
        );
      }
    });

    testWidgets('the centre button opens booking', (tester) async {
      useDesignFrame(tester);
      final opened = <String>[];
      await tester.pumpWidget(
        _app(
          CustomerShell(
            onSignOut: () {},
            tabs: const [Text('a'), Text('b'), Text('c'), Text('d')],
          ),
          opened,
        ),
      );

      await tester.tap(find.byKey(const Key('customer-shell-book')));
      await tester.pumpAndSettle();

      expect(opened, ['/booking']);
    });
  });
}
