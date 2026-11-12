import '../models/customer_address.dart';

// The house type rules of an address (PRD 1.1, .spec/contracts/customers.md 2.2), shared by the address form and the
// first-login onboarding. Pure functions, no widgets.

/// The area a new address starts with.
const String defaultFloorAreaText = '60.0';

/// A room is at most this many m2.
const double roomMaxAreaM2 = 30.0;

/// The column holds 6 digits with 2 decimals.
const double maxFloorAreaM2 = 9999.99;

/// A house has between 1 and this many floors.
const int houseMaxFloors = 10;

/// An apartment and a room are always one floor; only a house chooses its floors.
bool hasFixedSingleFloor(HousingType type) =>
    type == HousingType.apartment || type == HousingType.room;

/// The floors that count for the price: 1 for a fixed type, the typed number (or 1) for a house.
int effectiveFloors(HousingType type, String floorsText) {
  if (hasFixedSingleFloor(type)) return 1;
  return int.tryParse(floorsText) ?? 1;
}

/// The area typed in a field, 0 when it is not a number.
double parseFloorArea(String text) => double.tryParse(text) ?? 0.0;

/// The floors text to show after the type changed: "1" for a fixed type, otherwise the text stays (null).
String? floorsTextAfterTypeChange(HousingType type) =>
    hasFixedSingleFloor(type) ? '1' : null;

/// The area text to show after the type changed: a room that is bigger than the limit is cut to it, otherwise null.
String? floorAreaTextAfterTypeChange(HousingType type, double currentArea) =>
    type == HousingType.room && currentArea > roomMaxAreaM2 ? '30.0' : null;

/// The message under the area field, or null when the area is fine for the type.
String? floorAreaError(HousingType type, String? text) {
  final d = double.tryParse(text ?? '');
  if (d == null || d <= 0) return 'Nhập số lớn hơn 0.';
  if (d > maxFloorAreaM2) return 'Tối đa 9999,99 m².';
  if (type == HousingType.room && d > roomMaxAreaM2) {
    return 'Phòng trọ tối đa 30 m².';
  }
  return null;
}

/// The message under the floors field, or null (a fixed type has nothing to check).
String? floorsError(HousingType type, String? text) {
  if (hasFixedSingleFloor(type)) return null;
  final f = int.tryParse(text ?? '');
  if (f == null || f < 1 || f > houseMaxFloors) return 'Từ 1 đến 10 tầng.';
  return null;
}

/// The hint under the area field.
String floorAreaHelper(HousingType type) =>
    type == HousingType.room ? 'Phòng trọ tối đa 30 m²' : 'Diện tích một sàn';

/// The hint under the floors field.
String floorsHelper(HousingType type) =>
    hasFixedSingleFloor(type) ? 'Cố định 1 tầng' : 'Từ 1 đến 10 tầng';
