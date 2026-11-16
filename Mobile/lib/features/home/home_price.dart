import '../booking/models/booking_models.dart';

/// HOME-PRICE-01 (#940): what the price cards of the customer home say.
///
/// Since D36 an ECONOMY house over the standard area is ONE worker priced as 2 units (`isContinuousWork`): its quote has
/// `unitPrice` 260000 but `totalAmount` 520000, and the customer pays the total. The home must show what is paid.

/// The amount of a tier card: the whole price for a large ECONOMY house, else the price of one worker for a shift.
int homeCardAmount(PriceQuote quote) =>
    quote.isContinuousWork ? quote.totalAmount : quote.unitPrice;

/// The line under the amount of a tier card.
String homeCardCaption(PriceQuote quote) => quote.isContinuousWork
    ? 'trọn gói · 1 người, có thể làm thêm giờ'
    : 'mỗi người, mỗi ca · cần ${quote.requiredWorkers} người';

/// The amount on the book button: always what the order costs (equal to the unit price of a small house).
int homeBookAmount(PriceQuote quote) => quote.totalAmount;
