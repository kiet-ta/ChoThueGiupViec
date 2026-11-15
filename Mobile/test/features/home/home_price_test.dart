import 'package:flutter_test/flutter_test.dart';

import 'package:mobile/features/booking/models/booking_models.dart';
import 'package:mobile/features/home/home_price.dart';

PriceQuote quote({
  required int unit,
  required int total,
  int workers = 1,
  bool continuous = false,
}) => PriceQuote(
  addressId: 1,
  serviceTier: 'ECONOMY',
  totalAreaM2: 120,
  areaBracket: 'OVER_80',
  requiredWorkers: workers,
  unitPrice: unit,
  totalAmount: total,
  currency: 'VND',
  isContinuousWork: continuous,
);

/// HOME-PRICE-01 (#940), decision D36: a large Economy house is one worker priced as 2 units.
void main() {
  test('a large Economy house shows the whole price and says it is all-in', () {
    final large = quote(unit: 260000, total: 520000, continuous: true);

    expect(homeCardAmount(large), 520000);
    expect(homeCardCaption(large), 'trọn gói · 1 người, có thể làm thêm giờ');
    expect(homeBookAmount(large), 520000);
  });

  test('a small house keeps the price of one worker', () {
    final small = quote(unit: 260000, total: 260000);

    expect(homeCardAmount(small), 260000);
    expect(homeCardCaption(small), 'mỗi người, mỗi ca · cần 1 người');
    expect(homeBookAmount(small), 260000);
  });

  test('a Premium large house keeps the unit card but the button shows the order total', () {
    final premium = quote(unit: 390000, total: 780000, workers: 2);

    expect(homeCardAmount(premium), 390000);
    expect(homeCardCaption(premium), 'mỗi người, mỗi ca · cần 2 người');
    expect(homeBookAmount(premium), 780000);
  });
}
