import '../models/reverse_geocode_result.dart';
import '../widgets/admin_unit_picker_field.dart';

/// The map tiles of the address map (ADDR-MAP-01). OpenStreetMap is free for the demo and asks for its attribution and an
/// application name; a paid provider (Goong, MapTiler) replaces [urlTemplate] (and its key) here and nowhere else.
abstract final class AddressMapTiles {
  static const String urlTemplate =
      'https://tile.openstreetmap.org/{z}/{x}/{y}.png';
  static const String userAgentPackageName = 'com.example.mobile';
  static const String attribution = '© OpenStreetMap';
}

/// Where the map opens when there is no saved point and no phone position: District 1, Ho Chi Minh City (the app's default).
const double defaultMapLatitude = 10.7769;
const double defaultMapLongitude = 106.7009;

/// Zoom of a street with its houses, and of a whole district.
const double houseZoom = 17.0;
const double districtZoom = 14.0;

/// A point chosen on the map and the address the server read for it (null when the read failed).
class PickedPlace {
  final double latitude;
  final double longitude;
  final ReverseGeocodeResult? address;

  const PickedPlace({
    required this.latitude,
    required this.longitude,
    this.address,
  });
}

/// What the address form takes from an address read on the map. [city] and [ward] are names of the catalogue, or null when
/// they could not be matched (the field then asks the customer to choose).
class AddressFill {
  final String? addressLine;
  final String? city;
  final String? ward;

  const AddressFill({this.addressLine, this.city, this.ward});
}

/// The fill for [result] (decision of 2026-11-14, ADDR-MAP-01): the street line (or the full address when the street is
/// missing), the province matched to the catalogue (else [currentCity] is kept) and the ward matched inside that province;
/// a ward that cannot be matched is left empty rather than guessed, and an old ward never stays under a new point.
AddressFill fillFromGeocode(
  ReverseGeocodeResult result, {
  String? currentCity,
}) {
  final line = result.addressLine.trim().isNotEmpty
      ? result.addressLine.trim()
      : result.formattedAddress.trim();
  final city = matchAdminCity(result.city) ?? currentCity;
  final ward = matchAdminWard(
    city,
    result.district,
    addressLine: result.addressLine,
    formattedAddress: result.formattedAddress,
  );
  return AddressFill(
    addressLine: line.isEmpty ? null : line,
    city: city,
    ward: ward,
  );
}

/// One line for the bottom panel of the map: the full address when the server sent it, else its parts.
String placeText(ReverseGeocodeResult result) {
  if (result.formattedAddress.trim().isNotEmpty) {
    return result.formattedAddress.trim();
  }
  return [
    result.addressLine,
    result.district,
    result.city,
  ].map((s) => s.trim()).where((s) => s.isNotEmpty).join(', ');
}

/// Hands out a number per request and tells whether an answer belongs to the latest one, so an answer that arrives late
/// for an earlier tap never replaces the answer for the last tap.
class LatestRequest {
  int _last = 0;

  /// Starts a new request; earlier ones become stale.
  int next() => ++_last;

  bool isLatest(int ticket) => ticket == _last;
}
